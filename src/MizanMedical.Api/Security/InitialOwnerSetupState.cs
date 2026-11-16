using MizanMedical.Domain;

namespace MizanMedical.Api.Security;

/// <summary>
/// Short-lived, process-local first-run pairing code. Never persisted, sent in status
/// responses, included in Git, or generated after any staff record exists.
/// </summary>
public sealed class InitialOwnerSetupState
{
    private string? _code;
    public bool Enabled => Volatile.Read(ref _code) is not null;

    public void Activate(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Pairing code required.", nameof(code));
        Volatile.Write(ref _code, code);
    }

    public bool Matches(string? submitted) =>
        InitialOwnerSetupRules.MatchesCode(Volatile.Read(ref _code), submitted);

    public void Disable() => Volatile.Write(ref _code, null);
}

public sealed record InitialOwnerSetupRequest(
    string? SetupCode, string? FullName, string? Username,
    string? Password, string? ConfirmPassword, string? HospitalName);
