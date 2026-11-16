using MizanMedical.Domain;
using Xunit;

namespace MizanMedical.Tests;

public class FinanceRulesTests
{
    [Fact] public void InvoiceTotalUsesDecimalAndQuantities()
    {
        var total = FinanceRules.Total([(10.15m, 2), (3.40m, 3)]);
        Assert.Equal(30.50m, total);
    }

    [Theory]
    [InlineData("0")] [InlineData("-1")] [InlineData("1.001")]
    public void InvalidMoneyIsRejected(string value) =>
        Assert.Throws<DomainException>(() => FinanceRules.RequirePositiveMoney(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture), "amount"));

    [Fact] public void PaymentMayNotExceedRemainingBalance() =>
        Assert.Throws<DomainException>(() => FinanceRules.CheckPayment(100m, 80m, 21m));

    [Fact] public void PaymentEqualToRemainingBalanceIsAccepted() =>
        FinanceRules.CheckPayment(100m, 80m, 20m);

    [Fact] public void UnbalancedJournalIsRejected() =>
        Assert.Throws<DomainException>(() => FinanceRules.RequireBalanced([(100m, 0m), (0m, 90m)]));

    [Fact] public void BalancedJournalIsAccepted() =>
        FinanceRules.RequireBalanced([(100m, 0m), (0m, 60m), (0m, 40m)]);

    [Theory]
    [InlineData("100.001", "100.001")]
    [InlineData("1000000000001", "1000000000001")]
    public void BalancedJournalRejectsAmountsOutsideMoneyRules(string debit, string credit)
    {
        decimal amount1 = decimal.Parse(debit, System.Globalization.CultureInfo.InvariantCulture);
        decimal amount2 = decimal.Parse(credit, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Throws<DomainException>(() => FinanceRules.RequireBalanced([(amount1, 0m), (0m, amount2)]));
    }

    [Fact] public void BalancedJournalRejectsZeroLinesAndDoubleSidedLines()
    {
        Assert.Throws<DomainException>(() => FinanceRules.RequireBalanced([(100m, 0m), (0m, 0m), (0m, 100m)]));
        Assert.Throws<DomainException>(() => FinanceRules.RequireBalanced([(100m, 1m), (0m, 99m)]));
    }

    [Fact] public void PaymentValidatesExistingFinancialBalances()
    {
        Assert.Throws<DomainException>(() => FinanceRules.CheckPayment(100.001m, 0m, 10m));
        Assert.Throws<DomainException>(() => FinanceRules.CheckPayment(100m, 10.001m, 10m));
    }

    [Fact] public void InvoiceRequiresLines() =>
        Assert.Throws<DomainException>(() => FinanceRules.Total([]));

    [Fact] public void PasswordHashCannotBeUsedWithDifferentPassword()
    {
        var (hash, salt) = PasswordSecurity.Create("DemoPass1234#");
        Assert.True(PasswordSecurity.Verify("DemoPass1234#", hash, salt));
        Assert.False(PasswordSecurity.Verify("Different1234#", hash, salt));
    }

    [Fact] public void SessionsUseDifferentRandomTokensAndOneWayHashes()
    {
        string first = PasswordSecurity.NewSessionToken();
        string second = PasswordSecurity.NewSessionToken();
        Assert.NotEqual(first, second);
        Assert.NotEqual(first, PasswordSecurity.HashToken(first));
    }
}
