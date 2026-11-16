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
        bool local = Uri.TryCreate(App.Api.ServerAddress, UriKind.Absolute, out var uri) && uri.IsLoopback;
        SingleMachineMode.IsChecked = local;
        LanMode.IsChecked = !local;
    }

    private void Mode_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || AddressBox is null) return;
        if (SingleMachineMode.IsChecked == true)
            AddressBox.Text = ServerEndpointRules.DefaultAddress;
        else if (Uri.TryCreate(AddressBox.Text, UriKind.Absolute, out var existing) && existing.IsLoopback)
            AddressBox.Clear();
    }

    private string ValidateSelectedEndpoint()
    {
        string normalized = ServerEndpointRules.Normalize(AddressBox.Text);
        bool local = new Uri(normalized).IsLoopback;
        if (SingleMachineMode.IsChecked == true && !local)
            throw new DomainException("وضع الجهاز الواحد يتطلب عنوان localhost على الجهاز نفسه.");
        if (LanMode.IsChecked == true && local)
            throw new DomainException("وضع الشبكة المحلية يتطلب عنوان الخادم الداخلي، وليس localhost.");
        return normalized;
    }

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        TestButton.IsEnabled = false;
        ResultText.Text = "جار اختبار الاتصال بالخادم...";
        try
        {
            await App.Api.TestServerAsync(ValidateSelectedEndpoint());
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
            App.Api.ConfigureServer(ValidateSelectedEndpoint());
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
