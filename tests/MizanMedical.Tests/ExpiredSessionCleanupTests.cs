using MizanMedical.Domain;
using Xunit;

namespace MizanMedical.Tests;

public sealed class ExpiredSessionCleanupTests
{
    private static readonly DateTime Now = new(2026, 11, 16, 17, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(60, false)]
    public void Only_expired_sessions_are_eligible_for_cleanup(int minutesFromNow, bool expected)
    {
        Assert.Equal(expected, SessionCleanupRules.IsExpired(
            Now.AddMinutes(minutesFromNow), Now));
    }

    [Fact]
    public void Batch_and_pass_limits_bound_housekeeping_work()
    {
        Assert.InRange(SessionCleanupRules.BatchSize, 1, 1000);
        Assert.InRange(SessionCleanupRules.MaxBatchesPerPass, 1, 10);
        Assert.False(SessionCleanupRules.ContinueAfterBatch(0, 1));
        Assert.False(SessionCleanupRules.ContinueAfterBatch(499, 1));
        Assert.True(SessionCleanupRules.ContinueAfterBatch(500, 1));
        Assert.False(SessionCleanupRules.ContinueAfterBatch(
            SessionCleanupRules.BatchSize, SessionCleanupRules.MaxBatchesPerPass));
    }

    [Fact]
    public void Expiration_comparison_does_not_change_active_accounts_or_permissions()
    {
        Assert.False(SessionCleanupRules.IsExpired(Now.AddHours(1), Now));
        Assert.True(SessionCleanupRules.IsExpired(Now.AddHours(-1), Now));
        Assert.False(AccessRules.ArePermissionsValid([HospitalPermissions.UsersManage]));
    }
}
