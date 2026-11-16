using System.Security.Cryptography;

namespace MizanMedical.Domain;

public static class PasswordSecurity
{
    private const int Iterations = 310_000;
    private const int KeyLength = 32;

    public static (string Hash, string Salt) Create(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 12 || password.Length > 128)
            throw new DomainException("كلمة المرور يجب أن تتكون من 12 إلى 128 حرفا.");
        byte[] salt = RandomNumberGenerator.GetBytes(24);
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeyLength);
        return (Convert.ToBase64String(key), Convert.ToBase64String(salt));
    }

    public static bool IsAcceptableLoginPassword(string? password) =>
        !string.IsNullOrWhiteSpace(password) && password.Length <= 128;

    public static bool Verify(string password, string storedHash, string storedSalt)
    {
        // Do not perform expensive PBKDF2 on oversized untrusted input.
        if (!IsAcceptableLoginPassword(password)) return false;
        try
        {
            byte[] salt = Convert.FromBase64String(storedSalt);
            byte[] expected = Convert.FromBase64String(storedHash);
            byte[] computed = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeyLength);
            return CryptographicOperations.FixedTimeEquals(expected, computed);
        }
        catch (FormatException) { return false; }
        catch (ArgumentException) { return false; }
    }

    public static string NewSessionToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));
}
