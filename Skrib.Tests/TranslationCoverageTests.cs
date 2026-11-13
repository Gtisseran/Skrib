using System.Reflection;
using System.Text;

namespace Skrib.Tests;

/// <summary>
/// Guards the localization layer: every user-facing string must carry a translation for
/// every supported language, and every supported language must be offered in the picker.
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
    public void EveryLocalizedString_HasATranslationForEachLanguage()
    {
        var calls = ParseLocCalls(ReadSource(Path.Combine("Skrib", "MainWindow.xaml.cs")));

        // A Loc() call is one localized string: one argument per language.
        Assert.True(calls.Count >= 40,
            $"Seulement {calls.Count} chaînes localisées trouvées, la zone de langue a peut-être été déplacée.");

        var problems = new List<string>();
        foreach (var call in calls)
        {
            if (call.Length != LanguageHelper.Supported.Length)
            {
                problems.Add($"{call.Length} argument(s) au lieu de {LanguageHelper.Supported.Length} : {string.Join(" | ", call)}");
                continue;
            }

            for (int i = 0; i < call.Length; i++)
            {
                var value = call[i].Trim();
                if (!value.StartsWith('"') || !value.EndsWith('"') || value.Length <= 2)
                {
                    problems.Add($"Argument {i + 1} vide ou non littéral : {value}");
                }
            }
        }

        Assert.True(problems.Count == 0,
            "Chaînes localisées incomplètes :\n" + string.Join("\n", problems));
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

    /// <summary>
    /// Extracts the argument list of every Loc(...) call, skipping the method declaration.
    /// </summary>
    private static List<string[]> ParseLocCalls(string source)
    {
        var calls = new List<string[]>();
        int index = 0;

        while (true)
        {
            int start = source.IndexOf("Loc(", index, StringComparison.Ordinal);
            if (start < 0) break;

            index = start + 4;
            int depth = 1;
            var arguments = new List<string>();
            var current = new StringBuilder();

            while (index < source.Length && depth > 0)
            {
                char c = source[index];

                if (c == '"')
                {
                    current.Append(c);
                    index++;
                    while (index < source.Length)
                    {
                        current.Append(source[index]);
                        if (source[index] == '"' && source[index - 1] != '\\')
                        {
                            index++;
                            break;
                        }
                        index++;
                    }
                    continue;
                }

                if (c == '(') depth++;
                else if (c == ')')
                {
                    depth--;
                    if (depth == 0) break;
                }
                else if (c == ',' && depth == 1)
                {
                    arguments.Add(current.ToString().Trim());
                    current.Clear();
                    index++;
                    continue;
                }

                current.Append(c);
                index++;
            }

            arguments.Add(current.ToString().Trim());

            // A real call starts with a string literal; the declaration starts with a type.
            if (arguments.Count > 0 && arguments[0].StartsWith('"'))
            {
                calls.Add(arguments.ToArray());
            }
        }

        return calls;
    }
}
