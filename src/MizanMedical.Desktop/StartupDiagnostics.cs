using System.IO;
using System.Text;

namespace MizanMedical.Desktop;

/// <summary>
/// Never log passwords, authentication tokens, backup passphrases, or HTTP bodies.
/// Store exception stacks on the user's own Windows profile for troubleshooting.
/// </summary>
public static class StartupDiagnostics
{
    private static readonly object Gate = new();
    public static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MizanMedical", "logs", "startup.log");

    public static void Record(string stage, Exception error)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                string line = $"[{DateTimeOffset.Now:O}] {stage}: {error}\n\n";
                File.AppendAllText(LogPath, line, Encoding.UTF8);
            }
        }
        catch { /* Logging failure must never crash the UI or hide the initial error. */ }
    }
}
