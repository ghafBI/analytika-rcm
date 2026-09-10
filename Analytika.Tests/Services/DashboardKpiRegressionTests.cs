using Analytika.Models;
using Analytika.Models.ViewModels;
using Analytika.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Analytika.Tests.Services;

public class DashboardKpiRegressionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rcm_kpis_translate_on_sqlite_and_preserve_filtered_totals(bool empty)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        if (!empty)
        {
            var today = DateTime.UtcNow.Date;
            XmlParsedRecord Row(int facility, string kind, decimal net, decimal gross, int days, bool ready = true) => new()
            {
                FacilityId = facility, RecordKind = kind, NetAmount = net, GrossAmount = gross,
                PaidAmount = net / 2, TreatmentDate = today.AddDays(-days).ToString("yyyy-MM-dd"),
                ReadyForReport = ready, EncounterType = "Outpatient"
            };
            db.XmlParsedRecords.AddRange(
                Row(1, "Submission", 100.25m, 110.25m, 5),
                Row(1, "Submission", 200.75m, 220.75m, 10),
                Row(1, "Submission", 300m, 330m, 45),
                Row(2, "Submission", 9999m, 9999m, 5),
                Row(1, "Submission", 9999m, 9999m, 5, false),
                Row(1, "Remittance", 9999m, 9999m, 5));
            await db.SaveChangesAsync();
        }

        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var services = new ServiceCollection()
            .AddSingleton<IMemoryCache>(cache)
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
            .AddLogging()
            .AddScoped(_ => new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options))
            .BuildServiceProvider();
        var sut = new DashboardService(db, cache, services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<DashboardService>.Instance, new ConfigurationBuilder().Build());
        async Task<RCMDashboardViewModel> Load(int facility)
        {
            using var limit = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            while (true)
            {
                try { return await sut.BuildRcmDashboardAsync("Submissions", new RcmDashboardFilters { FacilityIds = new() { facility } }, limit.Token); }
                catch (OperationCanceledException) when (!limit.IsCancellationRequested) { await Task.Delay(100, limit.Token); }
            }
        }
        var result = await Load(1);

        Assert.Equal(empty ? "0" : "3", result.Metrics.Single(m => m.Label == "Total Claims").Value);
        Assert.Equal(empty ? "—" : "+100.0%", result.Metrics.Single(m => m.Label == "Total Claims").Delta);
        Assert.Equal(empty ? "AED 0" : "AED 601", result.Metrics.Single(m => m.Label == "Net Value").Value);
        Assert.Equal(empty ? "AED 0" : "AED 661", result.Metrics.Single(m => m.Label == "Gross Value").Value);
        var cached = await sut.BuildRcmDashboardAsync("Submissions",
            new RcmDashboardFilters { FacilityIds = new() { 1 } });
        Assert.Same(result, cached);
        var otherScope = await Load(2);
        Assert.NotSame(result, otherScope);
        Assert.Equal(empty ? "0" : "1", otherScope.Metrics.Single(m => m.Label == "Total Claims").Value);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.BuildRcmDashboardAsync("Submissions",
            new RcmDashboardFilters { FacilityIds = new() { 3 } }, canceled.Token));
    }
}
