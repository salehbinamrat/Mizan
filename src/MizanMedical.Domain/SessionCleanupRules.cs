namespace MizanMedical.Domain;

/// <summary>
/// Bounded session garbage collection; no user account, permissions, or unexpired
/// session may be deleted as a side effect of retention maintenance.
/// </summary>
public static class SessionCleanupRules
{
    public const int BatchSize = 500;
    public const int MaxBatchesPerPass = 4;
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    public static bool IsExpired(DateTime expiresAtUtc, DateTime nowUtc) =>
        expiresAtUtc <= nowUtc;

    public static bool ContinueAfterBatch(int fetchedCount, int batchesCompleted) =>
        fetchedCount == BatchSize && batchesCompleted < MaxBatchesPerPass;
}
