using MizanMedical.Domain;
using Xunit;

namespace MizanMedical.Tests;

/// <summary>
/// Hospital identity is set once on the local server, never chosen by a remote caller.
/// These tests deliberately precede the implementation.
</summary>
public sealed class HospitalIdentityRulesTests
{
    [Theory]
    [InlineData(" مستشفى السلام ", "مستشفى السلام")]
    [InlineData("مستشفى   غيل\tبن يمين", "مستشفى غيل بن يمين")]
    [InlineData("  Al Noor  Hospital  ", "Al Noor Hospital")]
    public void Valid_hospital_name_is_normalized(string input, string expected)
        => Assert.Equal(expected, HospitalIdentityRules.Normalize(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("A")]
    [InlineData("AB")]
    public void Missing_or_too_short_hospital_name_is_rejected(string? input)
        => Assert.Throws<DomainException>(() => HospitalIdentityRules.Normalize(input));

    [Fact]
    public void Excessively_long_name_is_rejected()
        => Assert.Throws<DomainException>(() => HospitalIdentityRules.Normalize(new string('س', 201)));

    [Theory]
    [InlineData("مستشفى النور", "مستشفى النور", true)]
    [InlineData(" مستشفى   النور ", "مستشفى النور", true)]
    [InlineData("General Hospital", "general hospital", true)]
    [InlineData("مستشفى النور", "مستشفى السلام", false)]
    [InlineData("مستشفى النور", "", false)]
    [InlineData("", "مستشفى النور", false)]
    public void Login_must_match_configured_hospital_not_another(string stored, string submitted, bool expected)
        => Assert.Equal(expected, HospitalIdentityRules.Matches(stored, submitted));
}
