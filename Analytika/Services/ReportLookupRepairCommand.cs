using System.Text.Json;
using Analytika.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Analytika.Services;

/// <summary>Explicit maintenance command; never starts a web host or normal workers.</summary>
public static class ReportLookupRepairCommand
{
    public static async Task<int> RunAsync(string dbPath, CancellationToken ct)
    {
        var path = Path.GetFullPath(dbPath);
        if (!File.Exists(path))
        {
            Console.WriteLine("Lookup repair refused: existing database required.");
            return 2;
        }
        var checkpointPath = Path.Combine(Path.GetDirectoryName(path)!, "report-lookup-repair.checkpoint.json");
        try
        {
            await using var repairLock = new FileStream(checkpointPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var connection = new SqliteConnectionStringBuilder
            {
                DataSource = path, Mode = SqliteOpenMode.ReadWrite, ForeignKeys = true,
                DefaultTimeout = 5, Pooling = false
            };
            await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection.ToString()).Options);
            var identity = new FileInfo(path).CreationTimeUtc.Ticks;
            var checkpoint = File.Exists(checkpointPath)
                ? JsonSerializer.Deserialize<LookupRepairCheckpoint>(await File.ReadAllTextAsync(checkpointPath, ct))
                : null;
            if (checkpoint != null && (!string.Equals(checkpoint.DatabasePath, path, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
                || checkpoint.CreationTicks != identity))
                throw new InvalidOperationException("Database identity differs from checkpoint.");
            if (checkpoint == null)
            {
                var highWatermark = await db.XmlParsedRecords.AsNoTracking().OrderByDescending(row => row.Id)
                    .Select(row => (int?)row.Id).FirstOrDefaultAsync(ct) ?? 0;
                checkpoint = new(path, identity, highWatermark, 0, 0, 0);
                await SaveAsync(checkpointPath, checkpoint, ct);
            }
            var sync = new ReportLookupSyncService(db);
            while (checkpoint.LastId < checkpoint.HighWatermark)
            {
                ct.ThrowIfCancellationRequested();
                var page = await sync.BackfillPageAsync(checkpoint.LastId, 500, ct, checkpoint.HighWatermark);
                checkpoint = checkpoint with
                {
                    LastId = page.ReachedEnd ? checkpoint.HighWatermark : page.LastId,
                    RowsRead = checkpoint.RowsRead + page.RowsRead,
                    Inserted = checkpoint.Inserted + page.Inserted
                };
                await SaveAsync(checkpointPath, checkpoint, ct);
                Console.WriteLine($"Lookup repair: rows={checkpoint.RowsRead}, inserted={checkpoint.Inserted}, cursor={checkpoint.LastId}, target={checkpoint.HighWatermark}.");
                if (checkpoint.LastId < checkpoint.HighWatermark) await Task.Delay(250, ct);
            }
            Console.WriteLine($"Lookup repair completed: rows={checkpoint.RowsRead}, inserted={checkpoint.Inserted}.");
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Lookup repair paused; rerun to resume the checkpoint.");
            return 130;
        }
        catch (Exception ex)
        {
            // No exception message/SQL, codes, credentials, or patient values in output.
            Console.WriteLine($"Lookup repair stopped ({ex.GetType().Name}); checkpoint retained. Verify database identity/schema/access and rerun.");
            return 2;
        }
    }

    private static async Task SaveAsync(string path, LookupRepairCheckpoint checkpoint, CancellationToken ct)
    {
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(checkpoint), ct);
        File.Move(temporary, path, overwrite: true);
    }

    public record LookupRepairCheckpoint(string DatabasePath, long CreationTicks, int HighWatermark, int LastId, long RowsRead, long Inserted);
}
