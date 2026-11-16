using MizanMedical.Domain;
using Xunit;

namespace MizanMedical.Tests;

public sealed class AuthenticationInputBoundsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Empty_login_password_is_rejected_before_hashing(string? value)
        => Assert.False(PasswordSecurity.IsAcceptableLoginPassword(value));

    [Fact]
    public void Excessively_long_password_is_rejected_before_pbkdf2()
    {
        Assert.False(PasswordSecurity.IsAcceptableLoginPassword(new string('x', 129)));
        Assert.False(PasswordSecurity.IsAcceptableLoginPassword(new string('x', 1024 * 1024)));
    }

    [Fact]
    public void Valid_length_password_remains_acceptable()
    {
        Assert.True(PasswordSecurity.IsAcceptableLoginPassword("Strong-login-password-2026!"));
        Assert.True(PasswordSecurity.IsAcceptableLoginPassword(new string('x', 128)));
    }

    [Fact]
    public void Password_verification_never_accepts_oversized_or_blank_input()
    {
        var (hash, salt) = PasswordSecurity.Create("Strong-login-password-2026!");
        Assert.False(PasswordSecurity.Verify(new string('x', 129), hash, salt));
        Assert.False(PasswordSecurity.Verify("", hash, salt));
        Assert.True(PasswordSecurity.Verify("Strong-login-password-2026!", hash, salt));
    }

    [Fact]
    public void Change_password_requires_bounded_current_password()
    {
        Assert.Throws<DomainException>(() => PasswordChangeRules.Validate(
            new string('x', 129), "New-safe-passphrase-2026!", "New-safe-passphrase-2026!"));
    }
}
