using System.Windows;
using System.Globalization;
namespace MizanMedical.Desktop;
public partial class App : Application
{
    internal static HospitalApi Api { get; } = new();
    protected override void OnStartup(StartupEventArgs e)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        base.OnStartup(e);
    }
}
