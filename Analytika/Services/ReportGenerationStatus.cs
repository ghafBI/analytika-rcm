using Analytika.Models;

namespace Analytika.Services;

/// <summary>Combines durable queue state with optional process-local progress.</summary>
public static class ReportGenerationStatus
{
    public static bool IsVisible(ReportRequest report, IReadOnlyCollection<int>? facilityIds)
    {
        if (facilityIds == null) return true;
        if (facilityIds.Count == 0) return false;
        if (string.IsNullOrWhiteSpace(report.FacilityIdsCsv))
            return report.BranchId.HasValue && facilityIds.Contains(report.BranchId.Value);
        var ids = report.FacilityIdsCsv.Split(',', StringSplitOptions.TrimEntries);
        return ids.Length > 0 && ids.All(value => int.TryParse(value, out var id) && facilityIds.Contains(id));
    }

    public static object Build(IReadOnlyList<ReportRequest> queued, ReportGenerationSnapshot snapshot, string reportType)
    {
        var activeVisible = snapshot.IsRunning
            && snapshot.ReportType.Equals(reportType, StringComparison.OrdinalIgnoreCase)
            && queued.Any(report => report.Id == snapshot.ReportRequestId);
        var processing = queued.FirstOrDefault(report => report.Status == "Processing");
        var next = queued.FirstOrDefault(report => report.Status == "Pending");
        var current = processing ?? next;
        // A Processing row survives a worker crash. It is evidence of queue state,
        // not a heartbeat, percentage, or proof of how many workers are alive.
        return new
        {
            isRunning = activeVisible,
            reportRequestId = activeVisible ? snapshot.ReportRequestId : current?.Id ?? 0,
            reportId = activeVisible ? snapshot.ReportId : current?.ReportId ?? "",
            stage = activeVisible ? snapshot.Stage : processing != null ? "Processing" : next != null ? "Queued" : "Idle",
            message = activeVisible ? snapshot.Message : processing != null
                ? "Report is marked Processing. Live worker progress is unavailable; completion has not yet been recorded."
                : next != null ? "Report is queued and waiting for a worker." : "No report is currently queued or processing.",
            pct = activeVisible ? snapshot.Pct : 0,
            progressAvailable = activeVisible || current == null,
            done = activeVisible ? snapshot.Done : 0,
            total = activeVisible ? snapshot.Total : 0,
            facility = activeVisible ? snapshot.Facility : "",
            dateRange = activeVisible ? snapshot.DateRange : "",
            startedAt = activeVisible ? snapshot.StartedAt : (DateTime?)null,
            pendingCount = queued.Count(report => report.Status == "Pending"),
            processingCount = queued.Count(report => report.Status == "Processing"),
            activeAgents = activeVisible ? (int?)1 : current != null ? null : 0,
            configuredAgents = (int?)null,
            agentStatus = activeVisible ? "One report worker observed in this process" : current != null ? "Worker count unavailable" : "Idle",
            hasWork = queued.Count > 0
        };
    }
}
