using System.Net.Http;
using System.Windows;
using MizanMedical.Domain;

namespace MizanMedical.Desktop;

public partial class FirstRunWindow : Window
{
    public string CreatedUsername { get; private set; } = "";

    public FirstRunWindow() => InitializeComponent();

    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = "";
        string password = PasswordBox.Password;
        string confirmation = ConfirmPasswordBox.Password;
        string normalized;
        try
        {
            normalized = InitialOwnerSetupRules.Validate(FullNameBox.Text,
                UsernameBox.Text, password, confirmation);
            if (string.IsNullOrWhiteSpace(PairingCodeBox.Password))
                throw new DomainException("أدخل رمز التهيئة المؤقت المعروض على شاشة الخادم.");
        }
        catch (DomainException ex)
        {
            ErrorText.Text = ex.Message;
            return;
        }
        CreateButton.IsEnabled = false;
        try
        {
            await App.Api.InitializeOwner(PairingCodeBox.Password,
                FullNameBox.Text.Trim(), normalized, password, confirmation);
            CreatedUsername = normalized;
            DialogResult = true;
        }
        catch (HttpRequestException)
        {
            ErrorText.Text = "تعذر الاتصال بخادم المستشفى. تحقق من التشغيل والاتصال الداخلي.";
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
        }
        finally
        {
            CreateButton.IsEnabled = true;
            PairingCodeBox.Clear();
            PasswordBox.Clear();
            ConfirmPasswordBox.Clear();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
