using Analytika.Models;
using Analytika.Services;
using Xunit;

namespace Analytika.Tests.Services;

public class ReportRequestEquivalenceTests
{
    private static ReportRequest Request() => new() { BranchId = 1, FacilityIdsCsv = "1,2", RequestedBy = "user", Template = "Template 1", EmailTo = "a@example.test,b@example.test" };

    [Fact]
    public void Reordered_sets_are_equivalent()
    {
        var right = Request();
        right.BranchId = 2;
        right.FacilityIdsCsv = "2, 1,2";
        right.EmailTo = "b@example.test, a@example.test";
        Assert.True(ReportRequestEquivalence.Matches(Request(), right));
    }

    [Theory]
    [InlineData("facility")]
    [InlineData("template")]
    [InlineData("email")]
    [InlineData("encounter")]
    [InlineData("user")]
    public void Different_request_is_not_suppressed(string change)
    {
        var right = Request();
        switch (change)
        {
            case "facility": right.FacilityIdsCsv = "1,3"; break;
            case "template": right.Template = "Template 2"; break;
            case "email": right.EmailTo = "c@example.test"; break;
            case "encounter": right.EncounterTypesCsv = "Outpatient"; break;
            case "user": right.RequestedBy = "other"; break;
        }
        Assert.False(ReportRequestEquivalence.Matches(Request(), right));
    }
}
