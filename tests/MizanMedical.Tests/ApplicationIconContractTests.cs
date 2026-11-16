using System.Xml.Linq;
using Xunit;

namespace MizanMedical.Tests;

/// <summary>Windows program, installer and all shell shortcuts use one branded identity.</summary>
public sealed class ApplicationIconContractTests
{
    private static readonly XNamespace P = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private static string RepoRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "MizanMedical.slnx")))
            current = current.Parent;
        return current?.FullName ?? throw new InvalidOperationException("Cannot find solution root.");
    }

    private static string FileAt(string path) =>
        Path.Combine(RepoRoot(), path.Replace('/', Path.DirectorySeparatorChar));

    [Fact]
    public void Installer_and_portable_executables_share_real_embedded_application_icon()
    {
        var project = XDocument.Load(FileAt("src/MizanMedical.Desktop/MizanMedical.Desktop.csproj"));
        Assert.Equal("Assets\\MizanMedical.ico", project.Descendants("ApplicationIcon").Single().Value.Trim());

        var embedded = project.Descendants("Resource").Select(x => (string?)x.Attribute("Include")).ToArray();
        Assert.Contains("Assets\\MizanMedical-UI.png", embedded);

        string setup = File.ReadAllText(FileAt("installer/MizanMedical.iss"));
        Assert.Contains("SetupIconFile={#IconFile}", setup);
        Assert.Contains("UninstallDisplayIcon={app}\\MizanMedical.Desktop.exe", setup);
        Assert.Contains("Filename: \"{app}\\MizanMedical.Desktop.exe\"", setup);

        string release = File.ReadAllText(FileAt("scripts/build-release.ps1"));
        Assert.Contains("/DIconFile=", release);
        Assert.Contains("MizanMedical.ico", release);
        Assert.Contains("MizanMedical.Desktop.exe", release);
    }

    [Fact]
    public void Windows_icon_has_multiple_sizes_and_valid_frame_ranges()
    {
        using var fs = File.OpenRead(FileAt("src/MizanMedical.Desktop/Assets/MizanMedical.ico"));
        using var reader = new BinaryReader(fs);
        Assert.Equal(0, reader.ReadUInt16()); // reserved
        Assert.Equal(1, reader.ReadUInt16()); // icon, not cursor
        ushort count = reader.ReadUInt16();
        Assert.InRange(count, (ushort)6, (ushort)20);
        var sizes = new HashSet<int>();
        for (int index = 0; index < count; index++)
        {
            int width = reader.ReadByte(); int height = reader.ReadByte();
            sizes.Add(width == 0 ? 256 : width);
            Assert.Equal(width, height);
            reader.ReadByte(); reader.ReadByte(); // color count, reserved
            reader.ReadUInt16(); reader.ReadUInt16(); // planes, bits
            uint length = reader.ReadUInt32();
            uint offset = reader.ReadUInt32();
            Assert.True(length > 32 && offset >= 6 + 16 * count);
            Assert.True((long)offset + length <= fs.Length, "ICO frame must stay inside file");
        }
        foreach (int s in new[] {16, 24, 32, 48, 64, 128, 256})
            Assert.Contains(s, sizes);
    }

    [Theory]
    [InlineData("LoginWindow.xaml")]
    [InlineData("MainWindow.xaml")]
    [InlineData("FirstRunWindow.xaml")]
    [InlineData("UsersWindow.xaml")]
    [InlineData("BackupWindow.xaml")]
    [InlineData("ChangePasswordWindow.xaml")]
    [InlineData("ServerSettingsWindow.xaml")]
    public void Every_window_inherits_the_executables_icon_instead_of_decoding_ico_resource(string file)
    {
        // WPF's BitmapDecoder rejects the embedded .ico container on some Windows
        // installations, throwing XamlParseException before the login is shown.
        // Without Window.Icon, Windows uses the real PE ApplicationIcon instead.
        var xml = XDocument.Load(FileAt("src/MizanMedical.Desktop/" + file));
        Assert.Null(xml.Root?.Attribute("Icon"));
    }

    [Theory]
    [InlineData("LoginWindow.xaml")]
    [InlineData("MainWindow.xaml")]
    [InlineData("FirstRunWindow.xaml")]
    public void Main_entry_windows_display_the_branded_image(string file)
    {
        var xml = XDocument.Load(FileAt("src/MizanMedical.Desktop/" + file));
        Assert.Contains(xml.Descendants(P + "Image"),
            i => (string?)i.Attribute("Source") == "pack://application:,,,/Assets/MizanMedical-UI.png");
    }

    [Fact]
    public void Brand_image_is_a_real_png_resource()
    {
        var path = FileAt("src/MizanMedical.Desktop/Assets/MizanMedical-UI.png");
        byte[] sig = File.ReadAllBytes(path).Take(8).ToArray();
        Assert.Equal(new byte[] {137,80,78,71,13,10,26,10}, sig);
    }
}
