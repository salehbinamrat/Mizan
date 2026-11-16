namespace MizanMedical.Domain;

/// <summary>
/// Expected PostgreSQL write conflicts are safe to return as HTTP 409 after
/// the active transaction rolls back. Other SQL errors must remain diagnosable.
/// </summary>
public static class DatabaseConflictCodeRules
{
    public static bool IsConcurrentWriteConflict(string? sqlState) =>
        sqlState is "40001" // serialization_failure
            or "40P01"     // deadlock_detected
            or "23505";    // unique_violation
}
