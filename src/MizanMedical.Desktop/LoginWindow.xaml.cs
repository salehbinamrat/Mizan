using System.Windows;
namespace MizanMedical.Desktop;

public partial class LoginWindow : Window
{
    public LoginWindow() => InitializeComponent();

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        LoginButton.IsEnabled = false;
        StatusText.Text = "جار التحقق من بيانات الدخول...";
        try
        {
            await App.Api.Login(UsernameBox.Text.Trim(), PasswordBox.Password);
            var main = new MainWindow();
            Application.Current.MainWindow = main;
            main.Show();
            Close();
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
        finally { LoginButton.IsEnabled = true; PasswordBox.Clear(); }
    }
}
