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
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("en-US");

        if (e.Args.Contains("--startup-smoke", StringComparer.OrdinalIgnoreCase))
        {
            // Headless, real Windows executable smoke. It loads all login-window
            // resources but never contacts PostgreSQL or requires a running API.
            StartupUri = null;
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try
            {
                var client = Api;
                var login = new LoginWindow();
                if (login.FindName("ServerAddressText") is null ||
                    login.FindName("ServerSettingsButton") is null)
                    throw new InvalidOperationException("Login window controls are missing.");
                string path = Environment.GetEnvironmentVariable("MIZAN_STARTUP_PROBE_FILE")
                    ?? throw new InvalidOperationException("Missing startup probe output path.");
                File.WriteAllText(path, "PASS: Mizan Medical WPF login XAML and client initialized.");
                Shutdown(0);
            }
            catch (Exception ex)
            {
                StartupDiagnostics.Record("startup-smoke", ex);
                Shutdown(1);
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
