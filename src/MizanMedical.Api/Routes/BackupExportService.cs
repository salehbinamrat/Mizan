using System.Diagnostics;
using Npgsql;
using MizanMedical.Domain;

namespace MizanMedical.Api.Routes;

internal static class BackupExportService
{
    public static async Task ExportAsync(Stream destination, string connectionString,
        string passphrase, CancellationToken cancellationToken)
    {
        var db = new NpgsqlConnectionStringBuilder(connectionString);
        var settings = new ProcessStartInfo("pg_dump")
        {
            UseShellExecute = false, RedirectStandardOutput = true,
            RedirectStandardError = true, CreateNoWindow = true
        };
        settings.ArgumentList.Add("--format=custom");
        settings.ArgumentList.Add("--no-owner");
        settings.ArgumentList.Add("--no-acl");
        settings.ArgumentList.Add("--host");
        settings.ArgumentList.Add(db.Host);
        settings.ArgumentList.Add("--port");
        settings.ArgumentList.Add(db.Port.ToString());
        settings.ArgumentList.Add("--username");
        settings.ArgumentList.Add(db.Username);
        settings.ArgumentList.Add("--dbname");
        settings.ArgumentList.Add(db.Database!);
        settings.Environment["PGPASSWORD"] = db.Password ?? "";
        using var process = Process.Start(settings)
            ?? throw new InvalidOperationException("pg_dump is missing on the API server.");
        Task<string> stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        string temp = Path.Combine(Path.GetTempPath(),
            $"mizan-{Guid.NewGuid():N}.mizanbak.partial");
        try
        {
            await using var archive = new FileStream(temp, FileMode.CreateNew,
                FileAccess.ReadWrite, FileShare.None, 1024 * 1024,
                FileOptions.Asynchronous | FileOptions.DeleteOnClose);
            // Never put the plaintext pg_dump on disk. Validate pg_dump succeeded
            // before allowing a client to download its encrypted output.
            await EncryptedBackupFormat.EncryptAsync(process.StandardOutput.BaseStream,
                archive, passphrase, cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0)
                throw new IOException("PostgreSQL full backup failed.");
            _ = await stderr;
            archive.Position = 0;
            await EncryptedBackupFormat.VerifyAsync(archive, passphrase, cancellationToken);
            archive.Position = 0;
            await archive.CopyToAsync(destination, cancellationToken);
        }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
    }
}
