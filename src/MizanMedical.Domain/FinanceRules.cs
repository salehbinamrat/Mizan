namespace MizanMedical.Domain;

public static class FinanceRules
{
    // Store financial amounts as decimal, never double/float.
    public static decimal RequirePositiveMoney(decimal amount, string field)
    {
        if (amount <= 0m || amount > 1_000_000_000_000m || decimal.Round(amount, 2) != amount)
            throw new DomainException($"{field}: المبلغ يجب أن يكون موجبا بخانتين عشريتين على الأكثر.");
        return amount;
    }

    public static decimal RequireNonNegativeMoney(decimal amount, string field)
    {
        if (amount < 0m || amount > 1_000_000_000_000m || decimal.Round(amount, 2) != amount)
            throw new DomainException($"{field}: قيمة مالية غير صحيحة.");
        return amount;
    }

    public static decimal Total(IEnumerable<(decimal UnitPrice, int Quantity)> items)
    {
        decimal total = 0m;
        var hasLines = false;
        foreach (var (price, quantity) in items)
        {
            hasLines = true;
            RequirePositiveMoney(price, "سعر الخدمة");
            if (quantity is < 1 or > 1000) throw new DomainException("الكمية يجب أن تكون بين 1 و1000.");
            total = checked(total + price * quantity);
        }
        if (!hasLines) throw new DomainException("الفاتورة تحتاج إلى خدمة واحدة على الأقل.");
        RequirePositiveMoney(total, "إجمالي الفاتورة");
        return total;
    }

    public static void CheckPayment(decimal invoiceTotal, decimal alreadyPaid, decimal newPayment)
    {
        RequirePositiveMoney(invoiceTotal, "إجمالي الفاتورة");
        RequireNonNegativeMoney(alreadyPaid, "المدفوع سابقا");
        RequirePositiveMoney(newPayment, "الدفعة");
        if (alreadyPaid > invoiceTotal)
            throw new DomainException("رصيد الفاتورة غير صحيح.");
        if (newPayment > invoiceTotal - alreadyPaid)
            throw new DomainException("المبلغ يتجاوز المتبقي على الفاتورة.");
    }

    public static void RequireBalanced(IEnumerable<(decimal Debit, decimal Credit)> lines)
    {
        var list = lines.ToList();
        if (list.Count < 2) throw new DomainException("القيد المحاسبي يتطلب طرفين على الأقل.");
        foreach (var (debit, credit) in list)
        {
            RequireNonNegativeMoney(debit, "المدين");
            RequireNonNegativeMoney(credit, "الدائن");
            if ((debit > 0m && credit > 0m) || (debit == 0m && credit == 0m))
                throw new DomainException("أحد أسطر القيد غير صحيح.");
        }
        if (list.Sum(x => x.Debit) != list.Sum(x => x.Credit))
            throw new DomainException("القيد غير متوازن.");
    }
}

public static class Accounts
{
    public const string Cash = "1100";
    public const string PatientReceivables = "1200";
    public const string MedicalRevenue = "4100";
    public const string OperatingExpenses = "5100";
}

public sealed class DomainException(string message) : Exception(message);
