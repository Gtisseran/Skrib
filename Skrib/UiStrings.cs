using System.Collections.Generic;
using System.Linq;

namespace Skrib;

/// <summary>
/// All user-facing UI strings in one table, keyed by stable ids, with one translation per
/// supported language. English is the source of truth; French and Spanish follow the same
/// order everywhere, so adding a language only means adding one column per entry.
/// Deliberately free of any WinUI dependency so coverage stays verifiable headlessly
/// (same contract as LanguageHelper / SettingsDefaults).
/// </summary>
public static class UiStrings
{
    /// <summary>One localizable string: stable id + one translation per supported language.</summary>
    public sealed record Entry(string Id, string En, string Fr, string Es);

    public static readonly Entry[] All =
    {
        new("about.version", "Version", "Version", "Versión"),
        new("about.rate", "Rate", "Noter", "Valorar"),
        new("doc.untitled", "Untitled", "Sans titre", "Sin título"),
        new("status.ready", "Ready", "Prêt", "Listo"),
        new("status.chars", "chars", "caractères", "caracteres"),
        new("status.preview", "Preview | ", "Aperçu | ", "Vista previa | "),
        new("menu.file", "File", "Fichier", "Archivo"),
        new("menu.new", "New", "Nouveau", "Nuevo"),
        new("menu.open", "Open...", "Ouvrir...", "Abrir..."),
        new("menu.save", "Save", "Enregistrer", "Guardar"),
        new("menu.saveAs", "Save As...", "Enregistrer sous...", "Guardar como..."),
        new("menu.exit", "Exit", "Quitter", "Salir"),
        new("menu.edit", "Edit", "Édition", "Editar"),
        new("menu.undo", "Undo", "Annuler", "Deshacer"),
        new("menu.redo", "Redo", "Rétablir", "Rehacer"),
        new("menu.cut", "Cut", "Couper", "Cortar"),
        new("menu.copy", "Copy", "Copier", "Copiar"),
        new("menu.paste", "Paste", "Coller", "Pegar"),
        new("menu.selectAll", "Select All", "Tout sélectionner", "Seleccionar todo"),
        new("menu.view", "View", "Affichage", "Ver"),
        new("menu.preview", "Markdown preview", "Aperçu Markdown", "Vista previa de Markdown"),
        new("settings.title", "Settings", "Paramètres", "Configuración"),
        new("settings.back", "Back", "Retour", "Atrás"),
        new("settings.section.appearance", "Appearance", "Apparence", "Apariencia"),
        new("settings.theme.title", "App theme", "Thème de l'application", "Tema de la aplicación"),
        new("settings.theme.desc", "Select which app theme to display in Skrib", "Choisissez le thème à afficher dans Skrib", "Elige el tema que se mostrará en Skrib"),
        new("settings.theme.light", "Light", "Clair", "Claro"),
        new("settings.theme.dark", "Dark", "Sombre", "Oscuro"),
        new("settings.theme.system", "Use system theme", "Utiliser le thème du système", "Usar el tema del sistema"),
        new("settings.backdrop.title", "Background", "Arrière-plan", "Fondo"),
        new("settings.backdrop.desc", "Choose the background shown in Skrib", "Choisissez l'arrière-plan affiché dans Skrib", "Elige el fondo que se mostrará en Skrib"),
        new("settings.backdrop.acrylic", "Acrylic", "Acrylique", "Acrílico"),
        new("settings.language.title", "Language", "Langue", "Idioma"),
        new("settings.language.desc", "Choose application display language", "Choisissez la langue de l'application", "Elige el idioma de la aplicación"),
        new("settings.lang.fr", "French (FR)", "Français (FR)", "Français (FR)"),
        new("settings.lang.es", "Spanish (ES)", "Español (ES)", "Español (ES)"),
        new("settings.lang.system", "System", "Système", "Sistema"),
        new("settings.section.editor", "Editor", "Éditeur", "Editor"),
        new("settings.wordWrap.title", "Word wrap", "Retour automatique à la ligne", "Ajuste de línea"),
        new("settings.wordWrap.desc", "Wrap long lines of text to fit the window width", "Renvoyer automatiquement le texte à la ligne", "Ajusta las líneas largas de texto al ancho de la ventana"),
        new("settings.editorBg.title", "Text area background", "Fond de la zone de texte", "Fondo del área de texto"),
        new("settings.editorBg.desc", "Show the app background behind the text", "Afficher l'arrière-plan de l'application derrière le texte", "Mostrar el fondo de la aplicación detrás del texto"),
        new("editor.placeholder", "Start writing…", "Commencez à écrire…", "Empieza a escribir…"),
        new("settings.section.about", "About this app", "À propos de cette application", "Acerca de esta aplicación"),
        new("common.on", "On", "Activé", "Activado"),
        new("common.off", "Off", "Désactivé", "Desactivado"),
        new("dialog.saveChanges.title", "Save changes?", "Enregistrer les modifications ?", "¿Guardar los cambios?"),
        new("dialog.saveChanges.body", "The document has been modified. Do you want to save it?", "Le document a été modifié. Voulez-vous l'enregistrer ?", "El documento se ha modificado. ¿Quieres guardarlo?"),
        new("dialog.dontSave", "Don't save", "Ne pas enregistrer", "No guardar"),
        new("dialog.cancel", "Cancel", "Annuler", "Cancelar"),
        new("error.readFile", "Unable to read file", "Impossible de lire le fichier", "No se puede leer el archivo"),
        new("error.open", "Open error", "Erreur lors de l'ouverture", "Error al abrir"),
        new("error.save", "Save error", "Erreur lors de l'enregistrement", "Error al guardar"),
        new("saveAs.txtType", "Text Document", "Document texte (.txt)", "Documento de texto (.txt)"),
        new("saveAs.mdType", "Markdown", "Markdown (.md)", "Markdown (.md)"),
    };

    private static readonly Dictionary<string, Entry> ById = All.ToDictionary(entry => entry.Id);

    /// <summary>
    /// Returns the translation matching the active language ("en"/"fr"/"es"; unknown
    /// languages fall back to French, the language the UI is authored in). An unknown id
    /// returns the id itself: visible in the UI, caught by the coverage tests in CI.
    /// </summary>
    public static string Get(string lang, string id)
    {
        if (!ById.TryGetValue(id, out var entry)) return id;
        return lang switch
        {
            LanguageHelper.English => entry.En,
            LanguageHelper.Spanish => entry.Es,
            _ => entry.Fr,
        };
    }
}
