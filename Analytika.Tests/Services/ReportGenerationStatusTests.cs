using System.Text.Json;
using Analytika.Models;
using Analytika.Services;
using Xunit;

namespace Analytika.Tests.Services;

public class ReportGenerationStatusTests
{
    private static ReportRequest Request(string status = "Processing") => new()
        { Id = 7, ReportId = "report-7", Status = status, BranchId = 1 };

    [Fact]
    public void External_processing_is_not_idle_and_does_not_invent_worker_count()
    {
        var state = JsonSerializer.SerializeToElement(ReportGenerationStatus.Build([Request()], new(), "ClaimSummary"));
        Assert.Equal("Processing", state.GetProperty("stage").GetString());
        Assert.False(state.GetProperty("progressAvailable").GetBoolean());
        Assert.Equal(JsonValueKind.Null, state.GetProperty("activeAgents").ValueKind);
        Assert.Equal(JsonValueKind.Null, state.GetProperty("configuredAgents").ValueKind);
        Assert.Equal(1, state.GetProperty("processingCount").GetInt32());
    }

    [Fact]
    public void Visible_local_snapshot_supplies_progress()
    {
        var snapshot = new ReportGenerationSnapshot { IsRunning = true, ReportType = "ClaimSummary", ReportRequestId = 7, Pct = 45, Stage = "Parsing" };
        var state = JsonSerializer.SerializeToElement(ReportGenerationStatus.Build([Request()], snapshot, "ClaimSummary"));
        Assert.Equal("Parsing", state.GetProperty("stage").GetString());
        Assert.Equal(45, state.GetProperty("pct").GetInt32());
        Assert.Equal(1, state.GetProperty("activeAgents").GetInt32());
    }

    [Fact]
    public void Unrelated_snapshot_does_not_disclose_progress_or_identifier()
    {
        var snapshot = new ReportGenerationSnapshot { IsRunning = true, ReportType = "ClaimSummary", ReportRequestId = 99, ReportId = "private", Pct = 80 };
        var state = JsonSerializer.SerializeToElement(ReportGenerationStatus.Build([Request("Pending")], snapshot, "ClaimSummary"));
        Assert.Equal("Queued", state.GetProperty("stage").GetString());
        Assert.Equal("report-7", state.GetProperty("reportId").GetString());
        Assert.Equal(0, state.GetProperty("pct").GetInt32());
    }

    [Theory]
    [InlineData("1,2", false)]
    [InlineData("1", true)]
    [InlineData("1,bad", false)]
    [InlineData("", true)]
    public void Facility_scope_requires_access_to_every_report_facility(string ids, bool visible)
    {
        var report = Request();
        report.FacilityIdsCsv = ids;
        Assert.Equal(visible, ReportGenerationStatus.IsVisible(report, [1]));
        Assert.False(ReportGenerationStatus.IsVisible(report, []));
        Assert.True(ReportGenerationStatus.IsVisible(report, null));
    }

    [Fact]
    public void Empty_queue_is_idle_even_with_old_snapshot()
    {
        var state = JsonSerializer.SerializeToElement(ReportGenerationStatus.Build([], new() { IsRunning = true, ReportType = "ClaimSummary", ReportRequestId = 7 }, "ClaimSummary"));
        Assert.Equal("Idle", state.GetProperty("stage").GetString());
        Assert.False(state.GetProperty("hasWork").GetBoolean());
    }
}
