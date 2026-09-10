using Analytika.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Analytika.Services;

/// <summary>
/// Runs resource-intensive reports outside the web host. The worker and web app
/// share the durable ReportRequests queue, while report generation remains
/// serialized to protect SQLite from competing writers.
/// </summary>
public static class ExternalReportWorker
{
    public static async Task RunAsync(
        IServiceProvider services,
        IConfiguration configuration,
        ILogger logger,
        CancellationToken stoppingToken)
    {
        var reportTypes = (configuration.GetSection("Reports:ExternalWorkerReportTypes").Get<string[]>() ?? ["AuditFlags"])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pollSeconds = Math.Clamp(configuration.GetValue("Reports:ExternalWorkerPollSeconds", 3), 1, 60);

        logger.LogInformation(
            "External report worker started for {ReportTypes}; polling every {PollSeconds}s.",
            string.Join(", ", reportTypes), pollSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            if (await ProcessArchiveRequestAsync(services, logger, stoppingToken))
                continue;

            int? reportId;
            using (var scope = services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var cutoff = DateTime.UtcNow.AddDays(-1);
                reportId = await db.ReportRequests
                    .AsNoTracking()
                    .Where(report => reportTypes.Contains(report.ReportType))
                    .Where(report => report.FilePath == null)
                    .Where(report => report.RequestedAt >= cutoff)
                    .Where(report => report.Status == "Pending" || report.Status == "Processing")
                    .OrderBy(report => report.RequestedAt)
                    .Select(report => (int?)report.Id)
                    .FirstOrDefaultAsync(stoppingToken);
            }

            if (!reportId.HasValue)
            {
                await Task.Delay(TimeSpan.FromSeconds(pollSeconds), stoppingToken);
                continue;
            }

            try
            {
                using var scope = services.CreateScope();
                var reports = scope.ServiceProvider.GetRequiredService<IReportService>();
                logger.LogInformation("External worker processing report request {ReportRequestId}.", reportId.Value);
                await reports.GenerateReportAsync(reportId.Value);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "External worker failed while processing report request {ReportRequestId}.", reportId.Value);
                await Task.Delay(TimeSpan.FromSeconds(pollSeconds), stoppingToken);
            }
        }
    }

    private static async Task<bool> ProcessArchiveRequestAsync(
        IServiceProvider services, ILogger logger, CancellationToken stoppingToken)
    {
        int? requestId;
        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            requestId = await db.PortalFetchLogs.AsNoTracking()
                .Where(x => x.Operation == "ArchiveBackfillRequest" && x.Status == "Queued")
                .OrderBy(x => x.FetchedAt).Select(x => (int?)x.Id)
                .FirstOrDefaultAsync(stoppingToken);
            if (!requestId.HasValue) return false;
            var claimed = await db.PortalFetchLogs
                .Where(x => x.Id == requestId && x.Status == "Queued")
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "Processing"), stoppingToken);
            if (claimed == 0) return true;
        }

        try
        {
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var request = await db.PortalFetchLogs.SingleAsync(x => x.Id == requestId, stoppingToken);
            using var payload = JsonDocument.Parse(request.ResponseSummary ?? "{}");
            var root = payload.RootElement;
            var from = root.GetProperty("From").GetDateTime();
            var to = root.GetProperty("To").GetDateTime();
            int? facilityId = root.TryGetProperty("FacilityId", out var f) && f.ValueKind == JsonValueKind.Number
                ? f.GetInt32() : null;
            var sync = scope.ServiceProvider.GetRequiredService<PortalSyncService>();
            var (records, files) = await sync.RunDhaArchiveBackfillAsync(from, to, facilityId, "ArchiveBackfillManual");
            request.Status = "Success";
            request.RecordsFetched = records;
            request.ResponseSummary = $"Completed: {records} new records, {files} files downloaded";
            await db.SaveChangesAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var request = await db.PortalFetchLogs.SingleAsync(x => x.Id == requestId, stoppingToken);
            request.Status = "Failed";
            request.ResponseSummary = $"Failed: {ex.Message}";
            await db.SaveChangesAsync(stoppingToken);
            logger.LogError(ex, "External worker failed archive request {RequestId}", requestId);
        }
        return true;
    }
}
