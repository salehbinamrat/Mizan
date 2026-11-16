using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace MizanMedical.Desktop;

public partial class App : Application
{
    // Lazy: never read client configuration or create HttpClient during the WPF
    // application type initializer. A malformed config must not terminate startup.
    private static readonly Lazy<HospitalApi> Client = new(() => new HospitalApi());
    internal static HospitalApi Api => Client.Value;

    public App()
    {
        DispatcherUnhandledException += OnDispatcherFailure;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                StartupDiagnostics.Record("unhandled-runtime", ex);
        };
    }

    private void OnDispatcherFailure(object sender, DispatcherUnhandledExceptionEventArgs args)
    {
        StartupDiagnostics.Record("wpf-dispatcher", args.Exception);
        args.Handled = true;
        MessageBox.Show(
            "تعذر تشغيل ميزان الطبي. تم تسجيل التفاصيل محليا في:\n" +
            StartupDiagnostics.LogPath + "\n\nيرجى إرسال ملف السجل للدعم الفني.",
            "خطأ في تشغيل ميزان الطبي", MessageBoxButton.OK, MessageBoxImage.Error);
        Shutdown(1);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

        if (e.Args.Contains("--startup-smoke", StringComparer.OrdinalIgnoreCase))
        {
            // Headless, real Windows executable smoke. It loads all login-window
            // resources but never contacts PostgreSQL or requires a running API.
            // Do not assign StartupUri = null: WPF rejects null immediately.
            // Environment.Exit terminates the smoke before StartupUri is used.
            string? path = Environment.GetEnvironmentVariable("MIZAN_STARTUP_PROBE_FILE");
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                    throw new InvalidOperationException("Missing startup probe output path.");
                File.WriteAllText(path, "BEGIN: App.OnStartup reached.\n");
                var client = Api;
                File.AppendAllText(path, "CLIENT: HospitalApi initialized.\n");
                var login = new LoginWindow();
                if (login.FindName("ServerAddressText") is null ||
                    login.FindName("ServerSettingsButton") is null ||
                    login.FindName("HospitalNameBox") is null)
                    throw new InvalidOperationException("Login window controls are missing.");
                var firstRun = new FirstRunWindow();
                if (firstRun.FindName("HospitalNameBox") is null)
                    throw new InvalidOperationException("First-run hospital name control missing.");
                var settings = new ServerSettingsWindow();
                if (settings.FindName("SingleMachineMode") is null ||
                    settings.FindName("LanMode") is null)
                    throw new InvalidOperationException("Offline/LAN settings controls missing.");
                File.AppendAllText(path, "PASS: Mizan Medical WPF login and setup XAML initialized.\n");
                // A headless CI process must terminate itself. Shutdown() is
                // dispatcher-driven and can leave a WinExe alive in runner session 0.
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                StartupDiagnostics.Record("startup-smoke", ex);
                if (!string.IsNullOrWhiteSpace(path))
                {
                    try { File.AppendAllText(path, "FAIL: " + ex.GetType().Name + "\n"); }
                    catch { /* capture diagnostic if possible */ }
                }
                Environment.Exit(1);
            }
            return;
        }

        try { base.OnStartup(e); }
        catch (Exception ex)
        {
            StartupDiagnostics.Record("app-startup", ex);
            MessageBox.Show(
                "حدث خطأ قبل عرض النافذة. تحقق من سجل التشغيل:\n" + StartupDiagnostics.LogPath,
                "تعذر بدء ميزان الطبي", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
