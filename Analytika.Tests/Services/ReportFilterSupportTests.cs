using Analytika.Models;
using Analytika.Services;
using Xunit;

namespace Analytika.Tests.Services;

public class ReportFilterSupportTests
{
    [Theory]
    [InlineData(1, null)]
    [InlineData(null, "1,2")]
    [InlineData(null, "invalid")]
    public void Department_filter_cannot_silently_generate_unfiltered_report(int? department, string? csv)
    {
        var error = Assert.Throws<InvalidOperationException>(() => ReportFilterSupport.Validate(
            new ReportRequest { DepartmentId = department, DepartmentIdsCsv = csv }));
        Assert.Equal(ReportFilterSupport.DepartmentUnavailable, error.Message);
    }

    [Fact]
    public void Report_without_department_filter_remains_supported()
        => ReportFilterSupport.Validate(new ReportRequest());
}
