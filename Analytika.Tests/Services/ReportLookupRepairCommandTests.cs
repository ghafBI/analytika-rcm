using System.Text.Json;
using Analytika.Models;
using Analytika.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Analytika.Tests.Services;

public class ReportLookupRepairCommandTests
{
    [Fact]
    public async Task Busy_write_retries_then_succeeds()
    {
        var attempts = 0;
        var waits = new List<int>();
        var result = await ReportLookupRepairCommand.WithWriteRetryAsync(() =>
        {
            if (++attempts < 3) throw new SqliteException("busy", 5);
            return Task.FromResult(7);
        }, waits.Add, CancellationToken.None, delayMs: 0);
        Assert.Equal(7, result);
        Assert.Equal(new[] { 1, 2 }, waits);
    }

    [Theory]
    [InlineData(5, 3)]
    [InlineData(6, 3)]
    [InlineData(1, 1)]
    public async Task Retry_is_bounded_and_only_for_contention(int error, int expectedAttempts)
    {
        var attempts = 0;
        await Assert.ThrowsAsync<SqliteException>(() => ReportLookupRepairCommand.WithWriteRetryAsync<int>(() =>
        {
            attempts++;
            throw new SqliteException("fixture", error);
        }, _ => { }, CancellationToken.None, maxAttempts: 3, delayMs: 0));
        Assert.Equal(expectedAttempts, attempts);
    }

    [Fact]
    public async Task Missing_database_is_refused_without_creating_it()
    {
        var directory = Path.Combine(Path.GetTempPath(), "bix-lookup-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "missing.db");
            Assert.Equal(2, await ReportLookupRepairCommand.RunAsync(path, CancellationToken.None));
            Assert.False(File.Exists(path));
            Assert.Empty(Directory.GetFiles(directory));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task Existing_fixture_repairs_and_replays_completed_checkpoint_without_duplicates()
    {
        var directory = Path.Combine(Path.GetTempPath(), "bix-lookup-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "analytika.db");
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={path};Pooling=False;Foreign Keys=False").Options;
        try
        {
            await using (var db = new AppDbContext(options))
            {
                await db.Database.EnsureCreatedAsync();
                db.XmlParsedRecords.AddRange(Enumerable.Range(1, 3).Select(id => new XmlParsedRecord
                    { Id = id, PayerId = "INS" + id, ReceiverId = "TPA", Clinician = "DHA-P" }));
                await db.SaveChangesAsync();
            }

            Assert.Equal(0, await ReportLookupRepairCommand.RunAsync(path, CancellationToken.None));
            var checkpointPath = Path.Combine(directory, "report-lookup-repair.checkpoint.json");
            var checkpoint = JsonSerializer.Deserialize<ReportLookupRepairCommand.LookupRepairCheckpoint>(await File.ReadAllTextAsync(checkpointPath));
            Assert.NotNull(checkpoint);
            Assert.Equal(Path.GetFullPath(path), checkpoint.DatabasePath);
            Assert.Equal(3, checkpoint.HighWatermark);
            Assert.Equal(3, checkpoint.LastId);
            Assert.Equal(3, checkpoint.RowsRead);
            Assert.Equal(5, checkpoint.Inserted);
            Assert.False(string.IsNullOrWhiteSpace(checkpoint.FileIdentity));

            // Metadata changes must not invalidate the Linux device/inode identity.
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));

            Assert.Equal(0, await ReportLookupRepairCommand.RunAsync(path, CancellationToken.None));
            await using var verified = new AppDbContext(options);
            Assert.Equal(3, await verified.Payers.CountAsync());
            Assert.Equal(1, await verified.Receivers.CountAsync());
            Assert.Equal(1, await verified.Clinicians.CountAsync());
            Assert.Equal(3, await verified.XmlParsedRecords.CountAsync());
            var replayed = JsonSerializer.Deserialize<ReportLookupRepairCommand.LookupRepairCheckpoint>(await File.ReadAllTextAsync(checkpointPath));
            Assert.Equal(checkpoint, replayed);
            await verified.DisposeAsync();
            // A different database at the same pathname must still fail closed.
            var replacement = Path.Combine(directory, "replacement.db");
            File.Copy(path, replacement);
            if (OperatingSystem.IsWindows())
                File.SetCreationTimeUtc(replacement, DateTime.UtcNow.AddDays(-1));
            File.Move(replacement, path, overwrite: true);
            Assert.Equal(2, await ReportLookupRepairCommand.RunAsync(path, CancellationToken.None));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
