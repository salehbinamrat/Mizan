using System.Security.Cryptography;
using MizanMedical.Domain;
using Xunit;

namespace MizanMedical.Tests;

public sealed class BackupEncryptionTests
{
    private const string Secret = "A-local-demo-passphrase-12345";

    [Fact]
    public async Task Complete_file_is_verified()
    {
        using var source = new MemoryStream(RandomNumberGenerator.GetBytes(1024 * 1024 + 75));
        using var encrypted = new MemoryStream();
        await EncryptedBackupFormat.EncryptAsync(source, encrypted, Secret);
        encrypted.Position = 0;
        await EncryptedBackupFormat.VerifyAsync(encrypted, Secret);
    }

    [Fact]
    public async Task Wrong_password_is_rejected()
    {
        using var source = new MemoryStream(new byte[] { 1, 2, 3, 4 });
        using var encrypted = new MemoryStream();
        await EncryptedBackupFormat.EncryptAsync(source, encrypted, Secret);
        encrypted.Position = 0;
        await Assert.ThrowsAnyAsync<CryptographicException>(() =>
            EncryptedBackupFormat.VerifyAsync(encrypted, "A-different-password-12345"));
    }

    [Fact]
    public async Task Truncated_archive_is_rejected()
    {
        using var source = new MemoryStream(new byte[] { 1, 2, 3, 4 });
        using var encrypted = new MemoryStream();
        await EncryptedBackupFormat.EncryptAsync(source, encrypted, Secret);
        using var truncated = new MemoryStream(encrypted.ToArray()[..^20]);
        await Assert.ThrowsAnyAsync<EndOfStreamException>(() =>
            EncryptedBackupFormat.VerifyAsync(truncated, Secret));
    }

    [Fact]
    public async Task Tampered_archive_is_rejected()
    {
        using var source = new MemoryStream(new byte[] { 1, 2, 3, 4 });
        using var encrypted = new MemoryStream();
        await EncryptedBackupFormat.EncryptAsync(source, encrypted, Secret);
        var data = encrypted.ToArray();
        data[65] ^= 1;
        using var modified = new MemoryStream(data);
        await Assert.ThrowsAnyAsync<CryptographicException>(() =>
            EncryptedBackupFormat.VerifyAsync(modified, Secret));
    }
}
