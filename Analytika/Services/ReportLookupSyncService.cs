using Analytika.Models;
using Microsoft.EntityFrameworkCore;

namespace Analytika.Services;

/// <summary>Maintains report filter IDs from actual parsed codes, without replacing existing IDs.</summary>
public sealed class ReportLookupSyncService(AppDbContext db)
{
    public async Task<int> UpsertAsync(IEnumerable<XmlParsedRecord> records, CancellationToken ct = default)
    {
        var batch = records.ToList();
        var inserted = 0;
        foreach (var codes in Codes(batch.Select(row => row.PayerId)).Chunk(500))
        {
            var existing = (await db.Payers.AsNoTracking().Where(row => codes.Contains(row.Name))
                .Select(row => row.Name).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var code in codes.Where(code => !existing.Contains(code)))
                inserted += await db.Database.ExecuteSqlInterpolatedAsync(
                    $"INSERT INTO \"Payers\" (\"Name\", \"IsActive\") SELECT {code}, {true} WHERE NOT EXISTS (SELECT 1 FROM \"Payers\" WHERE LOWER(\"Name\") = LOWER({code}))", ct);
        }
        foreach (var codes in Codes(batch.Select(row => row.ReceiverId)).Chunk(500))
        {
            var existing = (await db.Receivers.AsNoTracking().Where(row => codes.Contains(row.Name))
                .Select(row => row.Name).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var code in codes.Where(code => !existing.Contains(code)))
                inserted += await db.Database.ExecuteSqlInterpolatedAsync(
                    $"INSERT INTO \"Receivers\" (\"Name\", \"IsActive\") SELECT {code}, {true} WHERE NOT EXISTS (SELECT 1 FROM \"Receivers\" WHERE LOWER(\"Name\") = LOWER({code}))", ct);
        }
        foreach (var codes in Codes(batch.Select(row => row.Clinician)).Chunk(500))
        {
            var existing = (await db.Clinicians.AsNoTracking().Where(row => codes.Contains(row.Name))
                .Select(row => row.Name).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var code in codes.Where(code => !existing.Contains(code)))
                inserted += await db.Database.ExecuteSqlInterpolatedAsync(
                    $"INSERT INTO \"Clinicians\" (\"Name\", \"IsActive\") SELECT {code}, {true} WHERE NOT EXISTS (SELECT 1 FROM \"Clinicians\" WHERE LOWER(\"Name\") = LOWER({code}))", ct);
        }
        return inserted;
    }

    // Explicit maintenance hook: caller persists LastId only after success and
    // controls scheduling/throttling. Replaying a page is idempotent. No COUNT,
    // OFFSET, global DISTINCT, XML blobs, or unbounded ledger materialization.
    public async Task<ReportLookupBackfillPage> BackfillPageAsync(int afterId, int pageSize = 1000, CancellationToken ct = default, int? throughId = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(afterId);
        pageSize = Math.Clamp(pageSize, 1, 2000);
        var rows = await db.XmlParsedRecords.AsNoTracking().Where(row => row.Id > afterId)
            .Where(row => !throughId.HasValue || row.Id <= throughId.Value)
            .OrderBy(row => row.Id).Take(pageSize)
            .Select(row => new XmlParsedRecord { Id = row.Id, PayerId = row.PayerId, ReceiverId = row.ReceiverId, Clinician = row.Clinician })
            .ToListAsync(ct);
        var inserted = await UpsertAsync(rows, ct);
        return new(rows.Count == 0 ? afterId : rows[^1].Id, rows.Count, inserted, rows.Count < pageSize);
    }

    private static IEnumerable<string> Codes(IEnumerable<string?> values) => values
        .Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase);
}

public record ReportLookupBackfillPage(int LastId, int RowsRead, int Inserted, bool ReachedEnd);
