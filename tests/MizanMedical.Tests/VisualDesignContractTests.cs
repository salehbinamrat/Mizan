using System.Xml.Linq;
using Xunit;

namespace MizanMedical.Tests;

public sealed class VisualDesignContractTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private static string Root()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MizanMedical.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Solution root is missing.");
    }

    private static XDocument Read(string relativePath) => XDocument.Load(Path.Combine(Root(), relativePath));

    [Fact]
    public void Shared_theme_is_loaded_and_defines_consistent_inputs_tables_and_navigation()
    {
        var app = Read("src/MizanMedical.Desktop/App.xaml");
        Assert.Contains(app.Descendants(Presentation + "ResourceDictionary"),
            x => (string?)x.Attribute("Source") == "Themes/MedicalTheme.xaml");
        var theme = Read("src/MizanMedical.Desktop/Themes/MedicalTheme.xaml");
        var keys = theme.Root!.Elements().Select(x => (string?)x.Attribute(Xaml + "Key")).ToHashSet();
        foreach (var key in new[] { "Ocean", "Silver", "MedicalNavy", "MedicalCanvas", "MedicalLine", "MedicalCard", "MedicalMetricCard" })
            Assert.Contains(key, keys);
        var styles = theme.Root!.Elements(Presentation + "Style")
            .Select(x => (string?)x.Attribute("TargetType") ?? "").ToArray();
        foreach (var type in new[] { "Button", "TextBox", "PasswordBox", "ComboBox", "TabItem", "GroupBox", "DataGrid" })
            Assert.Contains(styles, s => s.EndsWith(" " + type + "}", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("MainWindow.xaml")]
    [InlineData("UsersWindow.xaml")]
    [InlineData("LoginWindow.xaml")]
    [InlineData("BackupWindow.xaml")]
    public void Every_window_preserves_arabic_layout_and_valid_xaml(string file)
    {
        var window = Read($"src/MizanMedical.Desktop/{file}");
        Assert.Equal("Window", window.Root?.Name.LocalName);
        Assert.Equal("RightToLeft", (string?)window.Root?.Attribute("FlowDirection"));
    }

    [Fact]
    public void Dashboard_uses_four_consistent_metric_cards()
    {
        var main = Read("src/MizanMedical.Desktop/MainWindow.xaml");
        var cards = main.Descendants(Presentation + "Border")
            .Where(x => (string?)x.Attribute("Style") == "{StaticResource MedicalMetricCard}").ToList();
        Assert.Equal(4, cards.Count);
        Assert.Contains(main.Descendants(Presentation + "UniformGrid"),
            x => (string?)x.Attribute("Columns") == "4");
    }

    [Fact]
    public void Input_widgets_have_visible_keyboard_focus_and_tables_have_alternating_rows()
    {
        var theme = Read("src/MizanMedical.Desktop/Themes/MedicalTheme.xaml");
        string text = theme.ToString();
        Assert.Contains("IsKeyboardFocusWithin", text);
        Assert.Contains("AlternatingRowBackground", text);
        Assert.Contains("MedicalLine", text);
        Assert.Contains("CornerRadius", text);
    }
}
