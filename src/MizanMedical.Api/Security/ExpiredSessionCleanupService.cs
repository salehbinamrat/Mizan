using Microsoft.EntityFrameworkCore;
using MizanMedical.Api.Data;
using MizanMedical.Domain;

namespace MizanMedical.Api.Security;

/// <summary>
/// Housekeeping is out of the interactive request path so it never slows down
/// sign-in, accounts, or financial operations.
/// </summary>
public sealed class ExpiredSessionCleanupService(
    IServiceScopeFactory scopes, ILogger<ExpiredSessionCleanupService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                int removed = await DeleteExpiredInBatches(stoppingToken);
                if (removed > 0)
                    logger.LogInformation("Purged {Count} expired login sessions", removed);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                logger.LogWarning(error,
                    "Expired session maintenance failed; regular login and finance paths are unaffected.");
            }

            try { await Task.Delay(SessionCleanupRules.Interval, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task<int> DeleteExpiredInBatches(CancellationToken cancellation)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        int removed = 0;
        for (int pass = 1; pass <= SessionCleanupRules.MaxBatchesPerPass; pass++)
        {
            DateTime nowUtc = DateTime.UtcNow;
            var ids = await db.LoginSessions.AsNoTracking()
                .Where(x => x.ExpiresAtUtc <= nowUtc)
                .OrderBy(x => x.ExpiresAtUtc).ThenBy(x => x.Id)
                .Select(x => x.Id).Take(SessionCleanupRules.BatchSize)
                .ToListAsync(cancellation);
            if (ids.Count == 0) break;

            // The cutoff is rechecked when deleting in case another node has
            // already removed a subset of the expired session rows.
            removed += await db.LoginSessions
                .Where(x => ids.Contains(x.Id) && x.ExpiresAtUtc <= nowUtc)
                .ExecuteDeleteAsync(cancellation);

            if (!SessionCleanupRules.ContinueAfterBatch(ids.Count, pass)) break;
        }
        return removed;
    }
}
