using System.Text.Json;
using Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Api.Services;

/// <summary>
/// Periodically writes per-user export files to disk so a recent snapshot always
/// exists (see docs/data-export-import.md §7). Off unless BACKUP_ENABLED=true.
/// Backups reuse the export builder + naming convention, so they re-import through
/// the same reviewed-patch path. Future: additional destinations (NAS, S3).
/// </summary>
public class BackupService(
    IServiceScopeFactory scopeFactory,
    IConfiguration config,
    ILogger<BackupService> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var enabled = string.Equals(config["BACKUP_ENABLED"], "true", StringComparison.OrdinalIgnoreCase);
        if (!enabled)
        {
            logger.LogInformation("Backups disabled (BACKUP_ENABLED not true).");
            return;
        }

        var interval = ParseInterval(config["BACKUP_INTERVAL"]);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var written = await RunBackupAsync(DateTime.UtcNow, ct);
                logger.LogInformation("Backup sweep wrote {Count} file(s).", written.Count);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Backup sweep failed.");
            }

            try { await Task.Delay(interval, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    internal static TimeSpan ParseInterval(string? value) => (value ?? "monthly").ToLowerInvariant() switch
    {
        "daily" => TimeSpan.FromDays(1),
        "weekly" => TimeSpan.FromDays(7),
        _ => TimeSpan.FromDays(30), // "monthly" / default
    };

    /// <summary>
    /// Writes one backup file per active user into BACKUP_DIR, pruning to the last
    /// BACKUP_KEEP per user. Returns the paths written. cycle count comes from
    /// BACKUP_CYCLE_COUNT (a patch) or null (full history).
    /// </summary>
    public async Task<List<string>> RunBackupAsync(DateTime nowUtc, CancellationToken ct)
    {
        var dir = config["BACKUP_DIR"];
        if (string.IsNullOrWhiteSpace(dir))
            dir = "backups";
        Directory.CreateDirectory(dir);

        int? cycleCount = int.TryParse(config["BACKUP_CYCLE_COUNT"], out var n) && n > 0 ? n : null;
        var keep = int.TryParse(config["BACKUP_KEEP"], out var k) && k > 0 ? k : 12;
        var appVersion = config["APP_VERSION"];

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cycleService = scope.ServiceProvider.GetRequiredService<ICycleService>();

        var userIds = await db.Users.Where(u => u.IsActive).Select(u => u.Id).ToListAsync(ct);

        var written = new List<string>();
        foreach (var userId in userIds)
        {
            var doc = await cycleService.BuildExportAsync(userId, cycleCount, "backup", appVersion);
            if (doc.CycleCount == 0)
                continue; // nothing to back up for this user yet

            var userShort = userId.ToString("N")[..8];
            var fileName = CycleService.ExportFileName(doc, userShort);
            var path = Path.Combine(dir, fileName);
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(doc, Json), ct);
            written.Add(path);

            Prune(dir, userShort, keep);
        }

        return written;
    }

    /// <summary>Keep only the most recent `keep` backups for a user; delete older ones.</summary>
    private static void Prune(string dir, string userShort, int keep)
    {
        var files = Directory.GetFiles(dir, $"thosedays-backup-{userShort}-*.json")
            .OrderByDescending(f => f)
            .ToList();
        foreach (var old in files.Skip(keep))
        {
            try { File.Delete(old); } catch { /* best effort */ }
        }
    }
}
