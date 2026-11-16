using System.Text.RegularExpressions;

namespace MizanMedical.Domain;

/// <summary>Restrict encrypted-backup restoration to disposable test databases.</summary>
public static class RestoreSafetyRules
{
    public static bool IsDisposableTargetName(string? databaseName) =>
        databaseName is { Length: >= 17 and <= 63 } &&
        databaseName.EndsWith("_restore_test", StringComparison.Ordinal) &&
        Regex.IsMatch(databaseName, "^[a-z][a-z0-9_]*$", RegexOptions.CultureInvariant);
}
