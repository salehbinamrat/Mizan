namespace MizanMedical.Domain;

/// <summary>
/// Shift opening money must not appear from nowhere between completed shifts.
/// Existing first-ever float policy is kept for backwards compatibility and
/// needs a separate, supervisor-approved funding workflow before production.
/// </summary>
public static class CashShiftContinuityRules
{
    public static void RequireOpening(decimal requested, bool hasPreviousClose, decimal? lastCountedCash)
    {
        FinanceRules.RequireNonNegativeMoney(requested, "رصيد افتتاح الصندوق");
        if (!hasPreviousClose) return;

        if (lastCountedCash is not decimal counted)
            throw new DomainException("إقفال الصندوق السابق يفتقد الرصيد النقدي الفعلي. راجع الإدارة قبل فتح مناوبة أخرى.");
        FinanceRules.RequireNonNegativeMoney(counted, "آخر رصيد فعلي مسجل");
        if (requested != counted)
            throw new DomainException("رصيد الافتتاح يجب أن يطابق النقد المعدود عند إقفال آخر مناوبة لنفس الصندوق. لا يجوز تعديل الرصيد دون تسوية موثقة.");
    }

    public static bool CanDeactivateDesk(bool hasLastClose, decimal? lastCountedCash, decimal? lastVariance)
    {
        if (!hasLastClose) return true;
        return lastCountedCash == 0m && lastVariance == 0m;
    }
}
