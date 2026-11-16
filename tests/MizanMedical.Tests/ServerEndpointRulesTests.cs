using MizanMedical.Domain;
using Xunit;

namespace MizanMedical.Tests;

public sealed class ServerEndpointRulesTests
{
    [Theory]
    [InlineData("https://hospital.example:5081", "https://hospital.example:5081")]
    [InlineData(" https://HOSPITAL.example:5081/ ", "https://hospital.example:5081")]
    [InlineData("https://10.0.0.5:5081", "https://10.0.0.5:5081")]
    [InlineData("https://localhost:5081", "https://localhost:5081")]
    [InlineData("http://localhost:5081", "http://localhost:5081")]
    [InlineData("http://127.0.0.1:5081", "http://127.0.0.1:5081")]
    public void Accepts_valid_server_authority(string input, string expected)
        => Assert.Equal(expected, ServerEndpointRules.Normalize(input));

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("localhost:5081")]
    [InlineData("http://10.0.0.5:5081")]
    [InlineData("http://hospital.example:5081")]
    [InlineData("https://user:secret@hospital.example")]
    [InlineData("https://hospital.example/api")]
    [InlineData("https://hospital.example?token=secret")]
    [InlineData("https://hospital.example/#anchor")]
    [InlineData("file:///c:/something")]
    public void Rejects_unsafe_or_invalid_server_urls(string input)
        => Assert.Throws<DomainException>(() => ServerEndpointRules.Normalize(input));

    [Fact]
    public void Transport_connection_settings_are_separate_from_credentials()
        => Assert.DoesNotContain("password", ServerEndpointRules.Normalize("https://hospital.example:5081"));
}
