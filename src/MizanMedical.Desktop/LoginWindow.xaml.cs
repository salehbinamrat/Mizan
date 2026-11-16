using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using MizanMedical.Domain;

namespace MizanMedical.Desktop;

public partial class LoginWindow : Window
{
    public LoginWindow() => InitializeComponent();

    private void Window_Loaded(object sender, RoutedEventArgs e) => UsernameBox.Focus();

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
        catch (Exception)
        {
            StatusText.Text = "تعذر تسجيل الدخول. تحقق من البيانات أو صلاحية الحساب واتصل بالمسؤول.";
        }
        finally
        {
            LoginButton.IsEnabled = true;
            PasswordBox.Clear();
        }
    }
}
