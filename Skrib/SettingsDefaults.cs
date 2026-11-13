namespace Skrib;

/// <summary>
/// First-run defaults used when the user has no settings file yet.
/// Deliberately free of any WinUI dependency so the rules stay verifiable headlessly.
/// The XAML defaults (BackdropComboBox, EditorBgToggle) must stay in sync with these values.
/// </summary>
public static class SettingsDefaults
{
    public const string Mica = "Mica";
    public const string Acrylic = "Acrylic";

    /// <summary>Backdrop shown on first launch.</summary>
    public const string Backdrop = Mica;

    /// <summary>
    /// False means the text area keeps its own opaque background (#202020 / #FFFFFF)
    /// instead of showing the window backdrop through it.
    /// </summary>
    public const bool EditorTransparent = false;
}
