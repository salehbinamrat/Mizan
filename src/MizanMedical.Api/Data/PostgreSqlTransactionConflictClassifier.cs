using MizanMedical.Domain;
using Npgsql;

namespace MizanMedical.Api.Data;

public static class PostgreSqlTransactionConflictClassifier
{
    /// <summary>
    /// EF Core / Npgsql can wrap PostgreSQL serialization failures in
    /// InvalidOperationException and DbUpdateException. Inspect the causal
    /// exception chain; never infer a business conflict from exception text.
    /// </summary>
    public static bool IsConflict(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
            if (current is PostgresException databaseError &&
                DatabaseConflictCodeRules.IsConcurrentWriteConflict(databaseError.SqlState))
                return true;
        return false;
    }
}
