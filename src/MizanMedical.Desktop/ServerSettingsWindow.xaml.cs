using System.Net.Http;
using System.Windows;
using MizanMedical.Domain;

namespace MizanMedical.Desktop;

public partial class ServerSettingsWindow : Window
{
    public ServerSettingsWindow()
    {
        InitializeComponent();
        AddressBox.Text = App.Api.ServerAddress;
    }

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        TestButton.IsEnabled = false;
        ResultText.Text = "جار اختبار الاتصال بالخادم...";
        try
        {
            await App.Api.TestServerAsync(AddressBox.Text);
            ResultText.Text = "نجح الاتصال بخادم ميزان الطبي. يمكنك حفظ هذا العنوان.";
        }
        catch (DomainException ex)
        {
            ResultText.Text = ex.Message;
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Record("server-connection-test", ex);
            ResultText.Text = "تعذر الاتصال بالخادم أو التحقق من شهادة HTTPS. تأكد من تشغيل API والشهادة والبورت والشبكة.";
        }
        finally { TestButton.IsEnabled = true; }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            App.Api.ConfigureServer(AddressBox.Text);
            DialogResult = true;
        }
        catch (DomainException ex) { ResultText.Text = ex.Message; }
        catch (Exception ex)
        {
            StartupDiagnostics.Record("save-server-settings", ex);
            ResultText.Text = "تعذر حفظ الإعدادات في ملف المستخدم. راجع سجل التشغيل.";
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
