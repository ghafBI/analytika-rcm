using System.Runtime.CompilerServices;
using Xunit;

namespace Analytika.Tests.Services;

public class ReportPreparationGuardTests
{
    [Fact]
    public void All_facility_report_preparation_does_not_reset_global_match_flags()
    {
        var source = File.ReadAllText(Path.Combine(SourceDirectory(), "..", "..",
            "Analytika", "Services", "ReportService.cs"));
        Assert.DoesNotContain("_xmlParsingService.MatchParsedRecordsAsync(", source);
        Assert.Contains("Global matching was not run", source);
        // Reports must continue reading both source kinds for their own matching.
        Assert.Contains("r.ReadyForReport && r.RecordKind == \"Submission\"", source);
        Assert.Contains("r.ReadyForReport && r.RecordKind == \"Remittance\"", source);
    }

    private static string SourceDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
