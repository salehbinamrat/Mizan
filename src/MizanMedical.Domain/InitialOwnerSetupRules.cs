using System.Security.Cryptography;
using System.Text;

namespace MizanMedical.Domain;

/// <summary>
/// The server alone may create the original system owner, only while the database
/// has no staff records. This does not grant any privileges from the desktop client.
/// </summary>
public static class InitialOwnerSetupRules
{
    public static bool CanInitialize(bool isLocalConnection, bool anyStaffExists, bool setupCodeActive) =>
        isLocalConnection && !anyStaffExists && setupCodeActive;

    public static string Validate(string? fullName, string? username,
        string? password, string? confirmation)
    {
        string name = fullName?.Trim() ?? "";
        if (name.Length is < 3 or > 200)
            throw new DomainException("اسم المسؤول يجب أن يتكون من 3 إلى 200 حرف.");
        string login = username?.Trim().ToLowerInvariant() ?? "";
        if (login.Length is < 3 or > 80 ||
            !login.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '.'))
            throw new DomainException("اسم الدخول يجب أن يتكون من 3 إلى 80 حرفا إنجليزيا صغيرا أو رقما أو _ أو نقطة.");
        if (string.IsNullOrWhiteSpace(password) || password.Length is < 12 or > 128)
            throw new DomainException("كلمة المرور يجب أن تتكون من 12 إلى 128 حرفا.");
        if (!string.Equals(password, confirmation, StringComparison.Ordinal))
            throw new DomainException("كلمتا المرور غير متطابقتين.");
        return login;
    }

    public static bool MatchesCode(string? expected, string? submitted)
    {
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(submitted)) return false;
        byte[] known = Encoding.UTF8.GetBytes(expected);
        byte[] provided = Encoding.UTF8.GetBytes(submitted.Trim());
        return known.Length == provided.Length &&
            CryptographicOperations.FixedTimeEquals(known, provided);
    }
}
