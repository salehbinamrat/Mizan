using MizanMedical.Domain;
using Xunit;

namespace MizanMedical.Tests;

public sealed class InitialOwnerSetupTests
{
    [Fact]
    public void Valid_owner_identity_is_normalized_with_no_default_account()
    {
        var username = InitialOwnerSetupRules.Validate("مسؤول المستشفى", " Admin.Head ",
            "A-unique-strong-password-2026!", "A-unique-strong-password-2026!");
        Assert.Equal("admin.head", username);
    }

    [Theory]
    [InlineData("", "admin", "Secure-password-2026!", "Secure-password-2026!")]
    [InlineData("مسؤول", "x", "Secure-password-2026!", "Secure-password-2026!")]
    [InlineData("مسؤول", "admin admin", "Secure-password-2026!", "Secure-password-2026!")]
    [InlineData("مسؤول", "admin", "short", "short")]
    [InlineData("مسؤول", "admin", "Secure-password-2026!", "different-password-2026!")]
    public void Invalid_owner_identity_is_rejected(string fullName, string username,
        string password, string confirmation)
    {
        Assert.Throws<DomainException>(() =>
            InitialOwnerSetupRules.Validate(fullName, username, password, confirmation));
    }

    [Theory]
    [InlineData(true, false, true, true)]
    [InlineData(false, false, true, false)]
    [InlineData(true, true, true, false)]
    [InlineData(true, false, false, false)]
    public void Setup_is_local_only_and_only_on_empty_database(bool loopback,
        bool anyStaffExists, bool codeActive, bool expected)
    {
        Assert.Equal(expected, InitialOwnerSetupRules.CanInitialize(
            loopback, anyStaffExists, codeActive));
    }

    [Fact]
    public void Pairing_code_must_match_exactly_and_cannot_be_empty()
    {
        const string code = "7C6B20D402A25EE9759AD0380D91907B9F1A5E6BD109A713";
        Assert.True(InitialOwnerSetupRules.MatchesCode(code, code));
        Assert.False(InitialOwnerSetupRules.MatchesCode(code, "7C6B20D402A25EE9759AD0380D91907B9F1A5E6BD109A714"));
        Assert.False(InitialOwnerSetupRules.MatchesCode(code, null));
        Assert.False(InitialOwnerSetupRules.MatchesCode(null, code));
        Assert.False(InitialOwnerSetupRules.MatchesCode(code, " "));
    }

    [Fact]
    public void Name_limits_are_enforced()
    {
        Assert.Throws<DomainException>(() => InitialOwnerSetupRules.Validate(
            new string('أ', 201), "admin", "Strong-password-2026!", "Strong-password-2026!"));
    }
}
