using MizanMedical.Domain;
using Npgsql;

namespace MizanMedical.Api.Data;

public static class DatabaseSchemaCompatibilityGuard
{
    public static async Task EnsureCompatibleAsync(string connectionString, CancellationToken cancellation = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellation);
        const string sql = """
            SELECT table_name, column_name
            FROM information_schema.columns
            WHERE table_schema = current_schema()
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellation);
        var existing = new List<(string Table, string Column)>();
        while (await reader.ReadAsync(cancellation))
            existing.Add((reader.GetString(0), reader.GetString(1)));

        var missing = SchemaCompatibilityRules.Missing(existing);
        if (missing.Count == 0) return;
        string sample = string.Join(", ", missing.Take(10).Select(x => $"{x.Table}.{x.Column}"));
        throw new InvalidOperationException(
            $"Hospital schema is outdated or incompatible ({missing.Count} required columns missing: {sample}). " +
            "Stop the server. Verify a full encrypted backup and restore it to staging, " +
            "apply reviewed numbered migrations to staging, and confirm data integrity before production upgrades.");
    }
}
