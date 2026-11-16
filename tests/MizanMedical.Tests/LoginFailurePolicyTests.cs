using MizanMedical.Domain;
using Xunit;

namespace MizanMedical.Tests;

public sealed class LoginFailurePolicyTests
{
    private static readonly DateTime UtcNow = new(2026, 11, 16, 17, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Invalid_password_must_not_persistently_lock_original_system_owner()
    {
        for (int previous = 0; previous < 20; previous++)
        {
            var outcome = LoginFailurePolicy.NextFailure(true, previous, UtcNow);
            Assert.Null(outcome.LockedUntilUtc);
            Assert.InRange(outcome.FailedLogins, 0, 4);
        }
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    [InlineData(3, 4)]
    public void Regular_staff_remains_subject_to_failures_before_lockout(int previous, int expected)
    {
        var result = LoginFailurePolicy.NextFailure(false, previous, UtcNow);
        Assert.Equal(expected, result.FailedLogins);
        Assert.Null(result.LockedUntilUtc);
    }

    [Fact]
    public void Fifth_invalid_attempt_temporarily_locks_regular_staff()
    {
        var result = LoginFailurePolicy.NextFailure(false, 4, UtcNow);
        Assert.Equal(0, result.FailedLogins);
        Assert.Equal(UtcNow.AddMinutes(15), result.LockedUntilUtc);
    }

    [Fact]
    public void Owner_must_not_be_denied_by_old_legacy_account_lock()
    {
        Assert.False(LoginFailurePolicy.IsAccountLocked(true, UtcNow.AddMinutes(10), UtcNow));
        Assert.True(LoginFailurePolicy.IsAccountLocked(false, UtcNow.AddMinutes(10), UtcNow));
        Assert.False(LoginFailurePolicy.IsAccountLocked(false, UtcNow.AddMinutes(-1), UtcNow));
    }
}
