using System.Net.Http;
using System.Windows;
using MizanMedical.Domain;

namespace MizanMedical.Desktop;

public partial class ChangePasswordWindow : Window
{
    public ChangePasswordWindow() => InitializeComponent();

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = "";
        try
        {
            PasswordChangeRules.Validate(CurrentPasswordBox.Password,
                NewPasswordBox.Password, ConfirmPasswordBox.Password);
        }
        catch (DomainException ex)
        {
            ErrorText.Text = ex.Message;
            return;
        }

        SaveButton.IsEnabled = false;
        try
        {
            await App.Api.PostWithoutResult("api/auth/change-password", new
            {
                CurrentPassword = CurrentPasswordBox.Password,
                NewPassword = NewPasswordBox.Password,
                ConfirmPassword = ConfirmPasswordBox.Password
            });
            DialogResult = true;
        }
        catch (HttpRequestException)
        {
            ErrorText.Text = "تعذر الاتصال بالخادم. تحقق من الشبكة قبل إعادة الإرسال.";
        }
        catch (Exception)
        {
            ErrorText.Text = "تعذر تحديث كلمة المرور. تحقق من كلمة المرور الحالية أو اتصل بمسؤول البرنامج.";
        }
        finally
        {
            SaveButton.IsEnabled = true;
            CurrentPasswordBox.Clear();
            NewPasswordBox.Clear();
            ConfirmPasswordBox.Clear();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
