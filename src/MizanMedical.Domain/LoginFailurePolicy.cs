namespace MizanMedical.Domain;

/// <summary>
/// Preserve staff lockouts while preventing remote attackers from persistently
/// locking the sole bootstrap owner. Request-level IP rate limiting is separate.
/// </summary>
public static class LoginFailurePolicy
{
    public static (int FailedLogins, DateTime? LockedUntilUtc) NextFailure(
        bool isSystemOwner, int currentFailures, DateTime nowUtc)
    {
        if (isSystemOwner)
            return (Math.Min(Math.Max(currentFailures, 0) + (currentFailures < 4 ? 1 : 0), 4), null);

        int next = Math.Clamp(currentFailures, 0, 4) + 1;
        return next == 5 ? (0, nowUtc.AddMinutes(15)) : (next, null);
    }

    public static bool IsAccountLocked(bool isSystemOwner, DateTime? lockedUntilUtc, DateTime nowUtc) =>
        !isSystemOwner && lockedUntilUtc.HasValue && lockedUntilUtc.Value > nowUtc;
}
