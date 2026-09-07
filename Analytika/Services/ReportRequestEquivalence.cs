using Analytika.Models;

namespace Analytika.Services;

public static class ReportRequestEquivalence
{
    public static bool Matches(ReportRequest left, ReportRequest right)
        => left.ReportType == right.ReportType
        && left.RequestedBy == right.RequestedBy
        && left.DateFrom.Date == right.DateFrom.Date && left.DateTo.Date == right.DateTo.Date
        && left.SearchCriteria == right.SearchCriteria && left.Template == right.Template
        && left.FileFormat == right.FileFormat
        && Set(left.FacilityIdsCsv, left.BranchId?.ToString()) == Set(right.FacilityIdsCsv, right.BranchId?.ToString())
        && Set(left.ReceiverIdsCsv, left.ReceiverId?.ToString()) == Set(right.ReceiverIdsCsv, right.ReceiverId?.ToString())
        && Set(left.PayerIdsCsv, left.PayerId?.ToString()) == Set(right.PayerIdsCsv, right.PayerId?.ToString())
        && Set(left.ClinicianIdsCsv, left.ClinicianId?.ToString()) == Set(right.ClinicianIdsCsv, right.ClinicianId?.ToString())
        && Set(left.DepartmentIdsCsv, left.DepartmentId?.ToString()) == Set(right.DepartmentIdsCsv, right.DepartmentId?.ToString())
        && Set(left.EncounterTypesCsv, left.EncounterType) == Set(right.EncounterTypesCsv, right.EncounterType)
        && Set(left.EmailTo) == Set(right.EmailTo);

    private static string Set(string? csv, string? fallback = null)
        => string.Join(",", (string.IsNullOrWhiteSpace(csv) ? fallback ?? "" : csv)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal));
}
