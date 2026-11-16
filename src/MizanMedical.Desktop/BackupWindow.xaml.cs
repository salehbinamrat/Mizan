using System.Windows;
using Microsoft.Win32;

namespace MizanMedical.Desktop;

public partial class BackupWindow : Window
{
    public BackupWindow() => InitializeComponent();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        string passphrase = PassphraseBox.Password;
        if (passphrase.Length < 16 || passphrase != ConfirmationBox.Password)
        {
            StatusText.Text = "كلمة التشفير أقل من 16 حرفا أو لا تطابق التأكيد.";
            return;
        }
        var picker = new SaveFileDialog
        {
            Title = "اختر موقع نسخة ميزان الطبي",
            FileName = $"Mizan-Full-{DateTime.Now:yyyyMMdd-HHmmss}.mizanbak",
            DefaultExt = ".mizanbak",
            AddExtension = true,
            Filter = "Mizan encrypted backup (*.mizanbak)|*.mizanbak",
            OverwritePrompt = true
        };
        if (picker.ShowDialog(this) != true) return;
        SaveButton.IsEnabled = false;
        PassphraseBox.IsEnabled = false;
        ConfirmationBox.IsEnabled = false;
        StatusText.Text = "ينشئ خادم المستشفى النسخة المشفرة ثم تُحفظ في المجلد المختار.";
        try
        {
            string checksum = await App.Api.DownloadFullBackup(passphrase, picker.FileName);
            StatusText.Text = $"تم حفظ النسخة والتحقق منها:\n{picker.FileName}\nSHA-256: {checksum}";
            MessageBox.Show(this, "اكتمل التنزيل والتحقق من سلامة النسخة. احتفظ بكلمة التشفير واختبر الاستعادة في بيئة معزولة.",
                "النسخ الاحتياطي", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText.Text = "فشل تنزيل النسخة أو التحقق منها: " + ex.Message;
            MessageBox.Show(this, "لم تُعتمد نسخة احتياطية. " + ex.Message,
                "خطأ النسخ", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            PassphraseBox.Clear();
            ConfirmationBox.Clear();
            PassphraseBox.IsEnabled = true;
            ConfirmationBox.IsEnabled = true;
            SaveButton.IsEnabled = true;
        }
    }
}
