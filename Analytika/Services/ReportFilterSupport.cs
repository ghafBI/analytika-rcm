using Analytika.Models;

namespace Analytika.Services;

public static class ReportFilterSupport
{
    public const string DepartmentUnavailable = "Department filtering is unavailable because claims do not have a verified department mapping. Clear the Department selection to generate a report.";

    public static void Validate(ReportRequest request)
    {
        if (request.DepartmentId.HasValue || !string.IsNullOrWhiteSpace(request.DepartmentIdsCsv))
            throw new InvalidOperationException(DepartmentUnavailable);
    }
}
