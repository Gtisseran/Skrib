using System.Reflection;
using Skrib;

namespace Skrib.Tests;

/// <summary>
/// Covers the first-run defaults of the settings screen: the values applied when the user
/// has no settings file yet, and their consistency with the XAML authoring defaults.
/// </summary>
public class SettingsDefaultsTests
{
    private static string RepoRoot =>
        typeof(SettingsDefaultsTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "RepoRoot")?.Value
        ?? Directory.GetCurrentDirectory();

    private static string ReadXaml()
    {
        var path = Path.Combine(RepoRoot, "Skrib", "MainWindow.xaml");
        Assert.True(File.Exists(path), $"Source introuvable : {path}");
        return File.ReadAllText(path);
    }

    [Fact]
    public void Backdrop_DefaultsToMica()
    {
        Assert.Equal(SettingsDefaults.Mica, SettingsDefaults.Backdrop);
    }

    [Fact]
    public void EditorBackground_DefaultsToDisabled()
    {
        Assert.False(SettingsDefaults.EditorTransparent);
    }

    [Fact]
    public void XamlBackdropComboBox_OffersMicaAsTheDefaultBackdrop()
    {
        var xaml = ReadXaml();
        Assert.True(xaml.Contains($"<ComboBoxItem Tag=\"{SettingsDefaults.Mica}\">", StringComparison.Ordinal),
            "L'arrière-plan par défaut (Mica) est absent du sélecteur.");
        Assert.True(xaml.Contains($"<ComboBoxItem Tag=\"{SettingsDefaults.Acrylic}\">", StringComparison.Ordinal),
            "L'arrière-plan Acrylique est absent du sélecteur.");
    }

    [Fact]
    public void XamlEditorBgToggle_StartsInTheDefaultState()
    {
        var xaml = ReadXaml();
        var start = xaml.IndexOf("x:Name=\"EditorBgToggle\"", StringComparison.Ordinal);
        Assert.True(start >= 0, "EditorBgToggle est absent du XAML.");

        // The toggle's own attributes span from its name up to the closing "/>" of its tag.
        var end = xaml.IndexOf("/>", start, StringComparison.Ordinal);
        Assert.True(end > start, "Balise de EditorBgToggle introuvable.");
        var element = xaml.Substring(start, end - start);

        var expected = SettingsDefaults.EditorTransparent ? "True" : "False";
        Assert.True(element.Contains($"IsOn=\"{expected}\"", StringComparison.Ordinal),
            $"EditorBgToggle doit démarrer sur IsOn=\"{expected}\" (trouvé : {element.Trim()}).");
    }
}
