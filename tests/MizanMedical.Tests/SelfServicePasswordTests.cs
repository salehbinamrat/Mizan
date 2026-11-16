using MizanMedical.Domain;
using Xunit;

namespace MizanMedical.Tests;

public sealed class SelfServicePasswordTests
{
    [Fact]
    public void Strong_different_password_can_be_saved_by_signed_in_owner_or_staff()
        => PasswordChangeRules.Validate(
            "Current-safe-passphrase-2026!", "New-safe-passphrase-2026!", "New-safe-passphrase-2026!");

    [Theory]
    [InlineData("Current-safe-passphrase-2026!", "New-safe-passphrase-2026!", "different")]
    [InlineData("Current-safe-passphrase-2026!", "short", "short")]
    [InlineData("Current-safe-passphrase-2026!", "Current-safe-passphrase-2026!", "Current-safe-passphrase-2026!")]
    [InlineData("", "New-safe-passphrase-2026!", "New-safe-passphrase-2026!")]
    public void Invalid_password_change_is_rejected(string current, string newPassword, string confirmation)
    {
        Assert.Throws<DomainException>(() =>
            PasswordChangeRules.Validate(current, newPassword, confirmation));
    }
}
