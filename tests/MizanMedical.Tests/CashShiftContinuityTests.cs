using MizanMedical.Domain;
using Xunit;

namespace MizanMedical.Tests;

public sealed class CashShiftContinuityTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void First_opening_preserves_existing_startup_float_behavior(decimal opening) =>
        CashShiftContinuityRules.RequireOpening(opening, hasPreviousClose: false, lastCountedCash: null);

    [Theory]
    [InlineData(0)]
    [InlineData(40)]
    [InlineData(140.25)]
    public void Next_opening_must_equal_last_verified_cash_count(decimal balance) =>
        CashShiftContinuityRules.RequireOpening(balance, hasPreviousClose: true, lastCountedCash: balance);

    [Theory]
    [InlineData(0, 140)]
    [InlineData(145, 140)]
    [InlineData(140.01, 140)]
    public void Undocumented_opening_change_is_rejected(decimal requested, decimal previous) =>
        Assert.Throws<DomainException>(() =>
            CashShiftContinuityRules.RequireOpening(requested, hasPreviousClose: true, lastCountedCash: previous));

    [Fact]
    public void Legacy_closed_shift_without_count_cannot_seed_a_new_shift() =>
        Assert.Throws<DomainException>(() =>
            CashShiftContinuityRules.RequireOpening(0m, hasPreviousClose: true, lastCountedCash: null));

    [Theory]
    [InlineData(-1)]
    [InlineData(10.001)]
    public void Invalid_cash_values_are_rejected_even_for_first_shift(decimal opening) =>
        Assert.Throws<DomainException>(() =>
            CashShiftContinuityRules.RequireOpening(opening, hasPreviousClose: false, lastCountedCash: null));

    [Fact]
    public void Cannot_move_a_user_with_outstanding_untransferred_cash_or_variance()
    {
        Assert.True(CashShiftContinuityRules.CanDeactivateDesk(hasLastClose: false, null, null));
        Assert.True(CashShiftContinuityRules.CanDeactivateDesk(hasLastClose: true, 0m, 0m));
        Assert.False(CashShiftContinuityRules.CanDeactivateDesk(hasLastClose: true, 40m, 0m));
        Assert.False(CashShiftContinuityRules.CanDeactivateDesk(hasLastClose: true, 0m, -40m));
        Assert.False(CashShiftContinuityRules.CanDeactivateDesk(hasLastClose: true, null, null));
    }
}
