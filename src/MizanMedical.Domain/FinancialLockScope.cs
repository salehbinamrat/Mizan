namespace MizanMedical.Domain;

/// <summary>
/// Stable PostgreSQL advisory-lock namespace used only inside transactions.
/// Distinct operation keys avoid confusing a request replay with its cash shift
/// or invoice, and identifiers are never derived from untrusted freeform text.
/// </summary>
public static class FinancialLockScope
{
    private static readonly HashSet<string> Kinds = new(StringComparer.Ordinal)
    {
        "shift", "invoice", "receipt-request", "expense-request"
    };

    public static string Key(string kind, Guid id)
    {
        if (id == Guid.Empty || !Kinds.Contains(kind))
            throw new DomainException("معرف العملية المالية أو نوع القفل غير صالح.");
        return $"mizan:finance:{kind}:{id:N}";
    }
}
