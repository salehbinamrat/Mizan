using System.Security.Cryptography;
using MizanMedical.Domain;
using Xunit;

namespace MizanMedical.Tests;

public sealed class VerifiedRestoreTests
{
    private const string Passphrase = "Safe-test-backup-passphrase-2026!";

    [Theory]
    [InlineData(0)]
    [InlineData(127)]
    [InlineData(1024 * 1024 + 37)]
    public async Task Encrypted_export_can_be_verified_and_fully_decrypted(int length)
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(length);
        using var source = new MemoryStream(bytes);
        using var encrypted = new MemoryStream();
        await EncryptedBackupFormat.EncryptAsync(source, encrypted, Passphrase);
        encrypted.Position = 0;
        await EncryptedBackupFormat.VerifyAsync(encrypted, Passphrase);
        encrypted.Position = 0;
        using var restored = new MemoryStream();
        await EncryptedBackupFormat.DecryptAsync(encrypted, restored, Passphrase);
        Assert.Equal(bytes, restored.ToArray());
    }

    [Fact]
    public async Task Modified_payload_is_never_accepted_for_restore()
    {
        using var encrypted = new MemoryStream();
        await EncryptedBackupFormat.EncryptAsync(new MemoryStream([3, 6, 9]),
            encrypted, Passphrase);
        var tampered = encrypted.ToArray();
        tampered[65] ^= 0x20;
        using var damaged = new MemoryStream(tampered);
        await Assert.ThrowsAnyAsync<CryptographicException>(async () =>
            await EncryptedBackupFormat.DecryptAsync(damaged, new MemoryStream(), Passphrase));
    }

    [Fact]
    public async Task Incorrect_passphrase_cannot_decrypt_backup()
    {
        using var encrypted = new MemoryStream();
        await EncryptedBackupFormat.EncryptAsync(new MemoryStream([4, 5]),
            encrypted, Passphrase);
        encrypted.Position = 0;
        await Assert.ThrowsAnyAsync<CryptographicException>(async () =>
            await EncryptedBackupFormat.DecryptAsync(encrypted, new MemoryStream(),
                "Another-strong-passphrase-2026!"));
    }

    [Theory]
    [InlineData("hospital_prod")]
    [InlineData("production")]
    [InlineData("hospital_restore_test;DROP DATABASE")]
    [InlineData("hospital_restore_test\"")]
    [InlineData("hospital-restore_test")]
    [InlineData("")]
    public void Production_or_unsafe_database_names_must_not_be_restore_targets(string name)
        => Assert.False(RestoreSafetyRules.IsDisposableTargetName(name));

    [Fact]
    public void Only_whitelisted_disposable_test_database_name_is_accepted()
    {
        Assert.True(RestoreSafetyRules.IsDisposableTargetName("mizan_restore_test"));
        Assert.True(RestoreSafetyRules.IsDisposableTargetName("hospital_2026_restore_test"));
    }
}
