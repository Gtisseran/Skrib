using System.Reflection;

namespace Skrib.Tests;

/// <summary>
/// Guards the localization layer: every user-facing string must carry a translation for
/// every supported language, and every id used by the UI must exist in the table.
/// These checks run against the real sources, so adding a language cannot silently leave
/// a string untranslated.
/// </summary>
public class TranslationCoverageTests
{
    private static string RepoRoot =>
        typeof(TranslationCoverageTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "RepoRoot")?.Value
        ?? Directory.GetCurrentDirectory();

    private static string ReadSource(string relativePath)
    {
        var path = Path.Combine(RepoRoot, relativePath);
        Assert.True(File.Exists(path), $"Source introuvable : {path}");
        return File.ReadAllText(path);
    }

    [Fact]
    public void EveryUiString_HasATranslationForEachLanguage()
    {
        Assert.True(UiStrings.All.Length >= 40,
            $"Seulement {UiStrings.All.Length} chaînes dans la table UiStrings, la table a peut-être été vidée.");

        var ids = new HashSet<string>();
        var problems = new List<string>();
        foreach (var entry in UiStrings.All)
        {
            if (!ids.Add(entry.Id))
            {
                problems.Add($"Id en double : {entry.Id}");
            }

            var values = new[] { entry.En, entry.Fr, entry.Es };
            for (int i = 0; i < values.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(values[i]))
                {
                    problems.Add($"{entry.Id} : traduction {LanguageHelper.Supported[i]} vide");
                }
            }
        }

        Assert.True(problems.Count == 0,
            "Chaînes localisées incomplètes :\n" + string.Join("\n", problems));
    }

    [Fact]
    public void EveryStringIdUsedByTheMainWindow_ExistsInTheTable()
    {
        var source = ReadSource(Path.Combine("Skrib", "MainWindow.xaml.cs"));
        Assert.True(source.Contains("S(\"", StringComparison.Ordinal),
            "MainWindow n'utilise plus la table UiStrings via S(\"...\").");

        var knownIds = UiStrings.All.Select(entry => entry.Id).ToHashSet();
        var problems = new List<string>();

        int index = 0;
        while ((index = source.IndexOf("S(\"", index, StringComparison.Ordinal)) >= 0)
        {
            int start = index + 2;
            int end = source.IndexOf('"', start + 1);
            if (end < 0) break;

            var id = source.Substring(start + 1, end - start - 1);
            if (!knownIds.Contains(id))
            {
                problems.Add(id);
            }
            index = end + 1;
        }

        Assert.True(problems.Count == 0,
            "Ids absents de la table UiStrings : " + string.Join(", ", problems));
    }

    [Fact]
    public void EverySupportedLanguage_IsOfferedInThePicker()
    {
        var xaml = ReadSource(Path.Combine("Skrib", "MainWindow.xaml"));
        var missing = LanguageHelper.Supported
            .Concat(new[] { LanguageHelper.SystemTag })
            .Where(tag => !xaml.Contains($"<ComboBoxItem Tag=\"{tag}\">", StringComparison.Ordinal))
            .ToList();

        Assert.True(missing.Count == 0,
            "Langues absentes du sélecteur : " + string.Join(", ", missing));
    }

    /// <summary>
    /// The editor welcome message must exist in every supported language and be wired
    /// to the TextBox placeholder, otherwise an untranslated string leaks into the UI.
    /// </summary>
    [Fact]
    public void EditorPlaceholder_IsLocalizedForEveryLanguage()
    {
        var xaml = ReadSource(Path.Combine("Skrib", "MainWindow.xaml"));
        Assert.True(xaml.Contains("PlaceholderText=", StringComparison.Ordinal),
            "Le TextBox de l'éditeur ne définit pas PlaceholderText.");

        var entry = UiStrings.All.FirstOrDefault(e => e.Id == "editor.placeholder");
        Assert.NotNull(entry);
        Assert.Equal("Start writing…", entry!.En);
        Assert.Equal("Commencez à écrire…", entry.Fr);
        Assert.Equal("Empieza a escribir…", entry.Es);

        var source = ReadSource(Path.Combine("Skrib", "MainWindow.xaml.cs"));
        Assert.True(source.Contains("\"editor.placeholder\"", StringComparison.Ordinal),
            "L'id editor.placeholder n'est pas utilisé dans MainWindow.xaml.cs.");
    }

    [Fact]
    public void EverySupportedLanguage_HasALabelTextBlockInThePicker()
    {
        var xaml = ReadSource(Path.Combine("Skrib", "MainWindow.xaml"));
        var missing = LanguageHelper.Supported
            .Where(tag => !xaml.Contains($"x:Name=\"Lang{char.ToUpperInvariant(tag[0])}{tag[1..]}ComboText\"", StringComparison.Ordinal))
            .ToList();

        Assert.True(missing.Count == 0,
            "Libellé de langue absent du XAML : " + string.Join(", ", missing));
    }

    [Fact]
    public void EverySupportedLanguage_HasADisplayNameInThePicker()
    {
        var xaml = ReadSource(Path.Combine("Skrib", "MainWindow.xaml"));
        var missing = LanguageHelper.Supported
            .Where(tag => !xaml.Contains($"({tag.ToUpperInvariant()})", StringComparison.Ordinal))
            .ToList();

        Assert.True(missing.Count == 0,
            "Nom affiché absent du sélecteur : " + string.Join(", ", missing));
    }
}
