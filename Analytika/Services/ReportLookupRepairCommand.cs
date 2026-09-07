using System.Text.Json;
using Analytika.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Analytika.Services;

/// <summary>Explicit maintenance command; never starts a web host or normal workers.</summary>
public static class ReportLookupRepairCommand
{
    public static async Task<int> RunAsync(string dbPath, CancellationToken ct, int pageSize = 500, int delayMs = 250)
    {
        pageSize = Math.Clamp(pageSize, 1, 2000);
        delayMs = Math.Max(50, delayMs);
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
            db.Database.SetCommandTimeout(5);
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
                var page = await WithWriteRetryAsync(
                    () => sync.BackfillPageAsync(checkpoint.LastId, pageSize, ct, checkpoint.HighWatermark),
                    attempt => Console.WriteLine($"Lookup repair waiting for database writer: attempt={attempt}/20; checkpoint unchanged."), ct);
                checkpoint = checkpoint with
                {
                    LastId = page.ReachedEnd ? checkpoint.HighWatermark : page.LastId,
                    RowsRead = checkpoint.RowsRead + page.RowsRead,
                    Inserted = checkpoint.Inserted + page.Inserted
                };
                await SaveAsync(checkpointPath, checkpoint, ct);
                Console.WriteLine($"Lookup repair: rows={checkpoint.RowsRead}, inserted={checkpoint.Inserted}, cursor={checkpoint.LastId}, target={checkpoint.HighWatermark}.");
                if (checkpoint.LastId < checkpoint.HighWatermark) await Task.Delay(delayMs, ct);
            }
            Console.WriteLine($"Lookup repair completed: rows={checkpoint.RowsRead}, inserted={checkpoint.Inserted}.");
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Lookup repair paused; rerun to resume the checkpoint.");
            return 130;
        }
        catch (SqliteException ex)
        {
            var diagnostic = ex.Message.Replace('\r', ' ').Replace('\n', ' ');
            // SQLite diagnostics for these parameterized lookup statements contain
            // schema/error descriptions, never interpolated patient/code values.
            Console.WriteLine($"Lookup repair stopped: SQLite code={ex.SqliteErrorCode}, extended={ex.SqliteExtendedErrorCode}, detail={diagnostic}. Checkpoint retained.");
            return 2;
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

    public static async Task<T> WithWriteRetryAsync<T>(Func<Task<T>> action, Action<int> waiting,
        CancellationToken ct, int maxAttempts = 20, int delayMs = 3000)
    {
        maxAttempts = Math.Clamp(maxAttempts, 1, 20);
        delayMs = Math.Max(0, delayMs);
        for (var attempt = 1; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try { return await action(); }
            catch (SqliteException ex) when ((ex.SqliteErrorCode == 5 || ex.SqliteErrorCode == 6) && attempt < maxAttempts)
            {
                waiting(attempt);
                await Task.Delay(delayMs, ct);
            }
        }
    }

    public record LookupRepairCheckpoint(string DatabasePath, long CreationTicks, int HighWatermark, int LastId, long RowsRead, long Inserted);
}
