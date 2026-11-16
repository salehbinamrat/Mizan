using MizanMedical.Domain;
using Xunit;

namespace MizanMedical.Tests;

public sealed class FinancialLockScopeTests
{
    private static readonly Guid Id = Guid.Parse("01234567-89ab-4cde-8fab-0123456789ab");

    [Fact]
    public void Same_financial_record_produces_same_cross_process_lock_key()
    {
        Assert.Equal(FinancialLockScope.Key("shift", Id), FinancialLockScope.Key("shift", Id));
        Assert.Contains(Id.ToString("N"), FinancialLockScope.Key("shift", Id));
    }

    [Fact]
    public void Locks_are_namespaced_by_entity_kind_and_business_operation()
    {
        var names = new[] { "shift", "invoice", "receipt-request", "expense-request" }
            .Select(kind => FinancialLockScope.Key(kind, Id)).ToArray();
        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
        Assert.All(names, name => Assert.StartsWith("mizan:finance:", name));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("")]
    public void Unknown_lock_kind_is_rejected(string kind)
    {
        Assert.Throws<DomainException>(() => FinancialLockScope.Key(kind, Id));
    }

    [Fact]
    public void Empty_identifier_is_rejected()
    {
        Assert.Throws<DomainException>(() => FinancialLockScope.Key("shift", Guid.Empty));
    }
}
