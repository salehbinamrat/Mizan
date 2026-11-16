using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using MizanMedical.Domain;

// OFFLINE OPERATOR TOOL: an encrypted backup can only be restored into a fresh
// disposable test database. Production restoration requires separate approval,
// a maintenance window, and the hospital's documented data-recovery procedure.
if (args.Length != 2 || !RestoreSafetyRules.IsDisposableTargetName(args[1]))
{
    Console.Error.WriteLine("Usage: MizanMedical.RestoreTool <encrypted-backup.mizanbak> <name_restore_test>");
    return 2;
}
if (!string.Equals(Environment.GetEnvironmentVariable("MIZAN_RESTORE_CONFIRM"),
    "YES_DISPOSABLE", StringComparison.Ordinal))
{
    Console.Error.WriteLine("Set MIZAN_RESTORE_CONFIRM=YES_DISPOSABLE only for an isolated test database.");
    return 2;
}
string? password = Environment.GetEnvironmentVariable("MIZAN_RESTORE_PASSPHRASE");
if (string.IsNullOrWhiteSpace(password) || password.Length < 16)
{
    Console.Error.WriteLine("Supply the backup passphrase in MIZAN_RESTORE_PASSPHRASE, not the command line.");
    return 2;
}
string archivePath = Path.GetFullPath(args[0]);
string checksumPath = archivePath + ".sha256";
if (!File.Exists(archivePath) || !File.Exists(checksumPath))
{
    Console.Error.WriteLine("Encrypted backup or its SHA-256 manifest is missing.");
    return 2;
}
try
{
    string expected = (await File.ReadAllTextAsync(checksumPath)).Trim().ToUpperInvariant();
    if (expected.Length != 64 || !expected.All(Uri.IsHexDigit))
        throw new InvalidDataException("Invalid checksum format.");
    await using var archive = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
    string calculated = Convert.ToHexString(await SHA256.HashDataAsync(archive));
    if (!CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(calculated)))
        throw new InvalidDataException("Checksum mismatch.");
    archive.Position = 0;
    await EncryptedBackupFormat.VerifyAsync(archive, password);
    archive.Position = 0;

    var start = new ProcessStartInfo("pg_restore")
    {
        UseShellExecute = false, CreateNoWindow = true,
        RedirectStandardInput = true, RedirectStandardError = true
    };
    foreach (string arg in new[] { "--format=custom", "--no-owner", "--no-acl",
                 "--exit-on-error", "--clean", "--if-exists", "--dbname", args[1] })
        start.ArgumentList.Add(arg);
    using var process = Process.Start(start)
        ?? throw new InvalidOperationException("pg_restore is missing from PATH.");
    try
    {
        Task<string> errors = process.StandardError.ReadToEndAsync();
        await EncryptedBackupFormat.DecryptAsync(archive,
            process.StandardInput.BaseStream, password);
        await process.StandardInput.BaseStream.FlushAsync();
        process.StandardInput.Close();
        await process.WaitForExitAsync();
        _ = await errors;
        if (process.ExitCode != 0)
            throw new IOException("pg_restore rejected the archive. Discard the test database.");
        Console.WriteLine("Restore completed into disposable database. Validate data counts and financial totals.");
        return 0;
    }
    catch
    {
        if (!process.HasExited) process.Kill(entireProcessTree: true);
        throw;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine("Restore failed: " + ex.GetType().Name +
        ". No production database was selected. Discard any partially restored disposable database.");
    return 1;
}
