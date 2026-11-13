namespace Skrib;

/// <summary>
/// Language tags and resolution rules shared by the UI and the tests.
/// Deliberately free of any WinUI dependency so the rules stay verifiable headlessly.
/// </summary>
public static class LanguageHelper
{
    /// <summary>Pseudo-tag meaning "follow the OS display language".</summary>
    public const string SystemTag = "System";

    public const string English = "en";
    public const string French = "fr";
    public const string Spanish = "es";

    /// <summary>Language tags the app can display.</summary>
    public static readonly string[] Supported = { English, French, Spanish };

    public static bool IsSupported(string lang)
    {
        if (string.IsNullOrEmpty(lang)) return false;
        foreach (var supported in Supported)
        {
            if (supported == lang) return true;
        }
        return false;
    }

    /// <summary>
    /// Maps a raw selection tag to the mode that gets persisted: "System" or a supported tag.
    /// Unknown tags fall back to French, the language the UI is authored in.
    /// </summary>
    public static string NormalizeMode(string lang)
    {
        if (string.IsNullOrEmpty(lang)) return SystemTag;
        if (lang == SystemTag) return SystemTag;
        return IsSupported(lang) ? lang : French;
    }

    /// <summary>
    /// Maps an OS culture name to one of the supported languages.
    /// Falls back to English when the OS language is not supported.
    /// </summary>
    public static string ResolveSystemLanguage(string cultureName)
    {
        if (string.IsNullOrEmpty(cultureName)) return English;
        string tag = cultureName.Split('-')[0].ToLowerInvariant();
        switch (tag)
        {
            case French: return French;
            case Spanish: return Spanish;
            default: return English;
        }
    }
}
