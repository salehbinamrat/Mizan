using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using MizanMedical.Domain;

namespace MizanMedical.Desktop;

public partial class LoginWindow : Window
{
    public LoginWindow() => InitializeComponent();

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        UsernameBox.Focus();
        await RefreshServerState();
    }

    private async Task RefreshServerState()
    {
        ServerAddressText.Text = App.Api.ServerAddress;
        FirstSetupButton.Visibility = Visibility.Collapsed;
        LoginButton.IsEnabled = true;
        if (App.Api.ConfigurationIssue is { Length: > 0 } warning)
        {
            StatusText.Text = warning;
            LoginButton.IsEnabled = false;
            return;
        }
        StatusText.Text = "جار التحقق من الاتصال بالخادم...";
        try
        {
            var state = await App.Api.InitialSetupStatus();
            StatusText.Text = "تم الاتصال بخادم ميزان الطبي بنجاح.";
            if (state.Required)
            {
                LoginButton.IsEnabled = false;
                if (state.CanInitialize && App.Api.IsLocalServer)
                {
                    FirstSetupButton.Visibility = Visibility.Visible;
                    StatusText.Text = "خادم جديد: أنشئ حساب المسؤول الأول برمز التهيئة المعروض على جهاز الخادم.";
                }
                else
                    StatusText.Text = "خادم ميزان الطبي لم يهيئ حساب المسؤول الأول بعد. افتح المعالج من جهاز الخادم.";
            }
        }
        catch (HttpRequestException ex)
        {
            StartupDiagnostics.Record("server-unreachable", ex);
            StatusText.Text = "تعذر الاتصال بالخادم. تأكد من تشغيل خدمة API وصحة العنوان وشهادة HTTPS.";
        }
        catch (TaskCanceledException ex)
        {
            StartupDiagnostics.Record("server-timeout", ex);
            StatusText.Text = "انتهت مهلة الاتصال. تحقق من الشبكة والخادم.";
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Record("server-initialization-check", ex);
            StatusText.Text = "لم يمكن التحقق من خادم ميزان الطبي. راجع الإعدادات وسجل التشخيص.";
        }
    }

    private async void ServerSettings_Click(object sender, RoutedEventArgs e)
    {
        var window = new ServerSettingsWindow { Owner = this };
        if (window.ShowDialog() == true)
            await RefreshServerState();
    }

    private async void FirstSetup_Click(object sender, RoutedEventArgs e)
    {
        FirstSetupButton.IsEnabled = false;
        try
        {
            var wizard = new FirstRunWindow { Owner = this };
            if (wizard.ShowDialog() == true)
            {
                FirstSetupButton.Visibility = Visibility.Collapsed;
                UsernameBox.Text = wizard.CreatedUsername;
                LoginButton.IsEnabled = true;
                StatusText.Text = "تم إنشاء حساب المسؤول الرئيسي. سجل الدخول باستخدام كلمة المرور التي حددتها.";
                PasswordBox.Focus();
            }
        }
        finally { FirstSetupButton.IsEnabled = true; }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && LoginButton.IsEnabled)
        {
            e.Handled = true;
            Login_Click(LoginButton, new RoutedEventArgs());
        }
    }

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        string username = UsernameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(PasswordBox.Password))
        {
            StatusText.Text = "أدخل اسم المستخدم وكلمة المرور.";
            return;
        }
        LoginButton.IsEnabled = false;
        StatusText.Text = "جار التحقق من الحساب والصلاحيات...";
        try
        {
            await App.Api.Login(username, PasswordBox.Password);
            var staff = App.Api.Staff;
            if (staff is null || LoginNavigationRules.LandingModule(staff.Role, staff.IsSystemOwner, staff.Permissions) is null)
            {
                await App.Api.Logout();
                StatusText.Text = "هذا الحساب ليس لديه صفحات مصرح بها. راجع مسؤول البرنامج لتحديد الصلاحيات.";
                return;
            }
            var main = new MainWindow();
            Application.Current.MainWindow = main;
            main.Show();
            Close();
        }
        catch (HttpRequestException)
        {
            StatusText.Text = "تعذر الاتصال بالخادم الداخلي. تحقق من الشبكة وإعدادات الاتصال.";
        }
        catch (TaskCanceledException)
        {
            StatusText.Text = "انتهت مهلة الاتصال بالخادم. حاول مجددا.";
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Record("login-failure", ex);
            StatusText.Text = "تعذر تسجيل الدخول. تحقق من البيانات أو صلاحية الحساب، أو راجع سجل التشخيص.";
        }
        finally
        {
            LoginButton.IsEnabled = true;
            PasswordBox.Clear();
        }
    }
}
