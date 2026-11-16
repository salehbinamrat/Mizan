using MizanMedical.Domain;
using Xunit;

namespace MizanMedical.Tests;

public sealed class PersonalCashDeskTests
{
    private readonly Guid _department = Guid.NewGuid();
    private readonly Guid _otherDepartment = Guid.NewGuid();
    private readonly Guid _cashier = Guid.NewGuid();
    private readonly Guid _otherCashier = Guid.NewGuid();

    [Fact]
    public void Cashier_can_operate_only_own_active_desk()
    {
        Assert.True(CashDeskRules.CanOperate(_cashier, _department, _cashier, _department, true));
        Assert.False(CashDeskRules.CanOperate(_cashier, _department, _otherCashier, _department, true));
        Assert.False(CashDeskRules.CanOperate(_cashier, _department, _cashier, _otherDepartment, true));
        Assert.False(CashDeskRules.CanOperate(_cashier, _department, _cashier, _department, false));
        Assert.False(CashDeskRules.CanOperate(_cashier, null, _cashier, _department, true));
        Assert.False(CashDeskRules.CanOperate(_cashier, _department, null, _department, true));
    }

    [Theory]
    [InlineData(0, 1, true)]
    [InlineData(1, 1, false)]
    [InlineData(9, 10, true)]
    [InlineData(10, 10, false)]
    [InlineData(0, 0, false)]
    [InlineData(-1, 10, false)]
    public void Active_user_capacity_is_enforced(int activeUsers, int limit, bool allowed)
        => Assert.Equal(allowed, CashDeskRules.WithinDepartmentLimit(activeUsers, limit));

    [Fact]
    public void Cannot_lower_limit_below_current_staff_count()
    {
        Assert.False(CashDeskRules.CanSetUserLimit(3, 4));
        Assert.True(CashDeskRules.CanSetUserLimit(4, 4));
        Assert.False(CashDeskRules.CanSetUserLimit(0, 0));
    }
}
