using Analytika.Models;
using Analytika.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Analytika.Tests.Services;

public class ReportLookupSyncServiceTests
{
    [Fact]
    public async Task Upsert_preserves_existing_ids_inactive_choices_and_uses_codes_not_descriptions()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Create(connection);
        await db.Database.EnsureCreatedAsync();
        db.Payers.Add(new Payer { Id = 42, Name = "INS1", IsActive = false });
        await db.SaveChangesAsync();
        var service = new ReportLookupSyncService(db);
        var rows = new[] { new XmlParsedRecord { PayerId = "INS1", PayerName = "Display only", ReceiverId = "TPA2", Clinician = "DHA-P-3" },
            new XmlParsedRecord { PayerId = "ins1", ReceiverId = "TPA2", Clinician = " " } };
        Assert.Equal(2, await service.UpsertAsync(rows));
        Assert.Equal(0, await service.UpsertAsync(rows));
        var payer = await db.Payers.SingleAsync();
        Assert.Equal(42, payer.Id);
        Assert.False(payer.IsActive);
        Assert.Equal("INS1", payer.Name);
        Assert.Equal("TPA2", (await db.Receivers.SingleAsync()).Name);
        Assert.Equal("DHA-P-3", (await db.Clinicians.SingleAsync()).Name);
        Assert.Empty(await db.Departments.ToListAsync());
    }

    [Fact]
    public async Task Backfill_reads_bounded_keyset_pages_and_can_resume_or_replay()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Create(connection);
        await db.Database.EnsureCreatedAsync();
        // These fixture rows need only the narrow lookup columns.
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=OFF");
        db.XmlParsedRecords.AddRange(Enumerable.Range(1, 5).Select(id => new XmlParsedRecord
            { Id = id * 10, PayerId = "INS" + id, ReceiverId = "TPA", Clinician = "DHA-P" }));
        await db.SaveChangesAsync();
        var service = new ReportLookupSyncService(db);
        var bounded = await service.BackfillPageAsync(0, 100, throughId: 20);
        Assert.Equal(2, bounded.RowsRead);
        Assert.Equal(20, bounded.LastId);
        Assert.Equal(2, await db.Payers.CountAsync());
        var first = await service.BackfillPageAsync(0, 2);
        Assert.Equal(20, first.LastId);
        Assert.Equal(2, first.RowsRead);
        Assert.False(first.ReachedEnd);
        Assert.Equal(0, (await service.BackfillPageAsync(0, 2)).Inserted);
        var second = await service.BackfillPageAsync(first.LastId, 2);
        Assert.Equal(40, second.LastId);
        var last = await service.BackfillPageAsync(second.LastId, 2);
        Assert.Equal(50, last.LastId);
        Assert.True(last.ReachedEnd);
        Assert.Equal(5, await db.Payers.CountAsync());
        Assert.Equal(1, await db.Receivers.CountAsync());
    }

    private static AppDbContext Create(SqliteConnection connection) => new(
        new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
}
