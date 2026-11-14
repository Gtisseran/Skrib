using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Windowing;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;
using Windows.System;

namespace Skrib
{
    public sealed partial class MainWindow : Window
    {
        private StorageFile? _currentFile;
        private bool _isDirty;
        private bool _showMarkdownPreview;
        private readonly System.Collections.Generic.Dictionary<string, Microsoft.UI.Xaml.Media.Brush?> _origEditorBgBrushes = new();
        private string _currentLang = "fr";
        private bool _isInitialized = false;
        private bool _suppressDirty;
        private bool _titleRefreshQueued;
        private MenuFlyoutItem? _contextUndo;
        private MenuFlyoutItem? _contextRedo;
        private MenuFlyoutItem? _contextCut;
        private MenuFlyoutItem? _contextCopy;
        private MenuFlyoutItem? _contextPaste;
        private MenuFlyoutItem? _contextSelectAll;
        private bool? _settingsWideLayout;
        private bool? _settingsComboBoxesStacked;
        private ScrollViewer? _editorScrollViewer;
        private double _smoothVerticalTarget;
        private double _smoothHorizontalTarget;
        private bool _smoothVerticalAnimating;
        private bool _smoothHorizontalAnimating;

        public MainWindow()
        {
            this.InitializeComponent();
            ConfigureCustomTitleBar();
            InitializeContextMenu();
            SetMinimumWindowSize();
            SetWindowIcon();
            ApplySavedTheme();
            ApplySavedBackdrop();
            ApplySavedLanguage();
            ApplySavedWordWrap();
            ApplySavedEditorBg();
            UpdateAboutVersion();
            UpdateCaretInfo();
            UpdateWordWrapState();
            Editor.Loaded += Editor_Loaded;
            _isInitialized = true;
        }

        // Smooth editor scrolling. The preview's ScrollViewer animates mouse-wheel
        // input, but the TextBox scrolls its inner ScrollViewer one line at a time
        // with no animation, which feels jerky. Relay wheel input (already handled
        // deeper in the TextBox) to that inner ScrollViewer through ChangeView,
        // whose animated path matches the preview's fluid scroll.
        private void Editor_Loaded(object sender, RoutedEventArgs e)
        {
            _editorScrollViewer = FindDescendant<ScrollViewer>(Editor);
            if (_editorScrollViewer is null) return;
            _editorScrollViewer.ViewChanged += EditorScrollViewer_ViewChanged;
            Editor.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler(Editor_PointerWheelChanged), true);
        }

        private static bool IsShiftDown()
        {
            var state = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift);
            return (state & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
        }

        private void Editor_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            var sv = _editorScrollViewer;
            if (sv is null) return;
            var props = e.GetCurrentPoint(Editor).Properties;

            if (IsShiftDown())
            {
                // Shift+wheel: horizontal, forward scrolls left (Windows convention).
                if (sv.ScrollableWidth <= 0) return;
                if (!_smoothHorizontalAnimating) _smoothHorizontalTarget = sv.HorizontalOffset;
                _smoothHorizontalTarget = Math.Clamp(_smoothHorizontalTarget - props.MouseWheelDelta, 0, sv.ScrollableWidth);
                _smoothHorizontalAnimating = true;
                sv.ChangeView(_smoothHorizontalTarget, null, null, false);
            }
            else
            {
                if (sv.ScrollableHeight <= 0) return;
                if (!_smoothVerticalAnimating) _smoothVerticalTarget = sv.VerticalOffset;
                _smoothVerticalTarget = Math.Clamp(_smoothVerticalTarget - props.MouseWheelDelta, 0, sv.ScrollableHeight);
                _smoothVerticalAnimating = true;
                sv.ChangeView(null, _smoothVerticalTarget, null, false);
            }
            e.Handled = true;
        }

        private void EditorScrollViewer_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
        {
            // Reset the accumulated targets once each animation settles, so the
            // next wheel burst starts from the actual on-screen offset.
            if (e.IsIntermediate) return;
            _smoothVerticalAnimating = false;
            _smoothHorizontalAnimating = false;
        }

        private static T? FindDescendant<T>(DependencyObject parent) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T match) return match;
                var nested = FindDescendant<T>(child);
                if (nested is not null) return nested;
            }
            return null;
        }

        private void ConfigureCustomTitleBar()
        {
            this.ExtendsContentIntoTitleBar = true;
            if (this.Content is FrameworkElement root)
            {
                // Keeps caption buttons readable when the OS theme changes under the system theme mode.
                // Also re-asserts the editor background: opaque mode uses an explicit
                // theme-matching brush, so it must follow OS theme switches.
                root.ActualThemeChanged += (s, e) =>
                {
                    UpdateTitleBarButtons();
                    try { ApplyEditorBg(EditorBgToggle != null && EditorBgToggle.IsOn, save: false); } catch (Exception ex)
                    {
                        System.Diagnostics.Debug.Write($"Error in theme change handler: {ex.Message}");
                    }
                    if (_showMarkdownPreview)
                    {
                        RenderMarkdownPreview();
                    }
                };
                if (root.FindName("AppTitleBar") is Border titleBar)
                {
                    this.SetTitleBar(titleBar);
                }
            }

            UpdateTitleBarButtons();
        }

        // Adapts caption button hover colors so they stay visible in both light and dark themes.
        private void UpdateTitleBarButtons()
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            if (appWindow == null)
            {
                return;
            }

            var titleBarCtrl = appWindow.TitleBar;
            titleBarCtrl.ExtendsContentIntoTitleBar = true;
            titleBarCtrl.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
            titleBarCtrl.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;

            bool isLight = true;
            if (this.Content is FrameworkElement root)
            {
                isLight = root.ActualTheme != ElementTheme.Dark;
            }

            if (isLight)
            {
                titleBarCtrl.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(20, 0, 0, 0);
                titleBarCtrl.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(35, 0, 0, 0);
                titleBarCtrl.ButtonHoverForegroundColor = Microsoft.UI.Colors.Black;
                titleBarCtrl.ButtonPressedForegroundColor = Microsoft.UI.Colors.Black;
            }
            else
            {
                titleBarCtrl.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(40, 255, 255, 255);
                titleBarCtrl.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(60, 255, 255, 255);
                titleBarCtrl.ButtonHoverForegroundColor = Microsoft.UI.Colors.White;
                titleBarCtrl.ButtonPressedForegroundColor = Microsoft.UI.Colors.White;
            }
        }

        private void InitializeContextMenu()
        {
            _contextUndo = new MenuFlyoutItem { Text = "Annuler", Icon = new SymbolIcon(Symbol.Undo), Tag = "undo" };
            _contextUndo.Click += Undo_Click;

            _contextRedo = new MenuFlyoutItem { Text = "Rétablir", Icon = new SymbolIcon(Symbol.Redo), Tag = "redo" };
            _contextRedo.Click += Redo_Click;

            _contextCut = new MenuFlyoutItem { Text = "Couper", Icon = new SymbolIcon(Symbol.Cut), Tag = "cut" };
            _contextCut.Click += Cut_Click;

            _contextCopy = new MenuFlyoutItem { Text = "Copier", Icon = new SymbolIcon(Symbol.Copy), Tag = "copy" };
            _contextCopy.Click += Copy_Click;

            _contextPaste = new MenuFlyoutItem { Text = "Coller", Icon = new SymbolIcon(Symbol.Paste), Tag = "paste" };
            _contextPaste.Click += Paste_Click;

            _contextSelectAll = new MenuFlyoutItem { Text = "Tout sélectionner", Icon = new SymbolIcon(Symbol.SelectAll), Tag = "select-all" };
            _contextSelectAll.Click += SelectAll_Click;

            var flyout = new MenuFlyout();
            flyout.Items.Add(_contextUndo);
            flyout.Items.Add(_contextRedo);
            flyout.Items.Add(new MenuFlyoutSeparator());
            flyout.Items.Add(_contextCut);
            flyout.Items.Add(_contextCopy);
            flyout.Items.Add(_contextPaste);
            flyout.Items.Add(new MenuFlyoutSeparator());
            flyout.Items.Add(_contextSelectAll);
            Editor.ContextFlyout = flyout;
        }

        private void SetMinimumWindowSize()
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            if (appWindow?.Presenter is OverlappedPresenter presenter)
            {
                presenter.PreferredMinimumWidth = 600;
                presenter.PreferredMinimumHeight = 450;
            }
        }

        // Uses only AppWindow.SetIcon (no user32 P/Invoke, no icon handle leak).
        private void SetWindowIcon()
        {
            try
            {
                var hwnd = WindowNative.GetWindowHandle(this);
                var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
                var appWindow = AppWindow.GetFromWindowId(windowId);

                string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
                if (!File.Exists(iconPath))
                {
                    try
                    {
                        iconPath = Path.Combine(Windows.ApplicationModel.Package.Current.InstalledLocation.Path, "Assets", "app.ico");
                    }
                    catch (Exception) { // Log would be added here in production
                    }
                }

                if (File.Exists(iconPath) && appWindow != null)
                {
                    try
                    {
                        appWindow.SetIcon(iconPath);
                    }
                    catch (Exception ex)
                    {
                        // Log error details for debugging: Console.WriteLine(ex.ToString());
                        System.Diagnostics.Debug.Write($"Error setting window icon: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                // Log root cause for debugging
                System.Diagnostics.Debug.Write($"Error in SetWindowIcon: {ex.Message}");
            }
        }

        private void UpdateAboutVersion()
        {
            AboutDesc.Text = Loc("Version", "Version", "Versión") + " " + GetVersionString();
        }

        #region Settings Page Navigation

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            MainView.Visibility = Visibility.Collapsed;
            SettingsView.Visibility = Visibility.Visible;
            _ = DispatcherQueue.TryEnqueue(() => UpdateSettingsLayout(SettingsContentGrid.ActualWidth));
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            CloseSettings();
        }

        private void CloseSettings()
        {
            SettingsView.Visibility = Visibility.Collapsed;
            MainView.Visibility = Visibility.Visible;
        }

        private void RootGrid_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Escape && SettingsView.Visibility == Visibility.Visible)
            {
                CloseSettings();
                e.Handled = true;
                return;
            }
            // Ctrl+A while previewing selects the whole rendered document.
            if (_showMarkdownPreview && e.Key == VirtualKey.A && IsControlDown())
            {
                SelectAllInPreview();
                e.Handled = true;
            }
        }

        private static bool IsControlDown()
        {
            return Microsoft.UI.Input.InputKeyboardSource
                .GetKeyStateForCurrentThread(VirtualKey.Control)
                .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        }

        // Keeps the settings page usable at every window size:
        // - wide: main column and "About" column side by side;
        // - medium: "About" moves below the main column;
        // - narrow: dropdowns also move below their labels and stretch to the card width.
        private void SettingsContentGrid_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateSettingsLayout(SettingsContentGrid.ActualWidth);
        }

        private void SettingsContentGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateSettingsLayout(e.NewSize.Width);
        }

        private void UpdateSettingsLayout(double width)
        {
            if (double.IsNaN(width) || width <= 0)
            {
                return;
            }

            bool isWide = width >= 900;
            bool stackComboBoxes = width < 560;

            double aboutWidth = 0;
            if (isWide)
            {
                // Scale the "About" column continuously with the window instead of
                // snapping between breakpoints, so dragging the window stays fluid.
                aboutWidth = Math.Clamp(width * 0.30, 280, 340);
                AboutColumn.Width = new GridLength(aboutWidth);
            }

            if (_settingsWideLayout != isWide)
            {
                _settingsWideLayout = isWide;
                if (isWide)
                {
                    SettingsContentGrid.ColumnSpacing = 24;
                    SettingsContentGrid.RowSpacing = 0;
                    Grid.SetRow(SettingsAboutColumn, 0);
                    Grid.SetColumn(SettingsAboutColumn, 1);
                    Grid.SetColumnSpan(SettingsAboutColumn, 1);
                }
                else
                {
                    AboutColumn.Width = new GridLength(0);
                    SettingsContentGrid.ColumnSpacing = 0;
                    SettingsContentGrid.RowSpacing = 24;
                    Grid.SetRow(SettingsAboutColumn, 1);
                    Grid.SetColumn(SettingsAboutColumn, 0);
                    Grid.SetColumnSpan(SettingsAboutColumn, 2);
                }
            }

            // The dropdowns shrink with the card and only move below the labels
            // when there is no longer enough room to keep them inline.
            double mainWidth = isWide ? width - aboutWidth - 24 : width;
            double themeWidth = Math.Clamp(mainWidth * 0.38, 220, 280);
            double comboWidth = Math.Clamp(mainWidth * 0.32, 200, 240);

            if (_settingsComboBoxesStacked != stackComboBoxes)
            {
                _settingsComboBoxesStacked = stackComboBoxes;
                ApplySettingsComboBoxLayout(ThemeComboBox, stackComboBoxes);
                ApplySettingsComboBoxLayout(BackdropComboBox, stackComboBoxes);
                ApplySettingsComboBoxLayout(LangComboBox, stackComboBoxes);
            }

            if (!stackComboBoxes)
            {
                ThemeComboBox.Width = themeWidth;
                BackdropComboBox.Width = comboWidth;
                LangComboBox.Width = comboWidth;
            }
        }

        private static void ApplySettingsComboBoxLayout(ComboBox comboBox, bool stacked)
        {
            if (stacked)
            {
                Grid.SetRow(comboBox, 1);
                Grid.SetColumn(comboBox, 0);
                Grid.SetColumnSpan(comboBox, 3);
                comboBox.Width = double.NaN;
                comboBox.HorizontalAlignment = HorizontalAlignment.Stretch;
                comboBox.Margin = new Thickness(0, 8, 0, 0);
            }
            else
            {
                Grid.SetRow(comboBox, 0);
                Grid.SetColumn(comboBox, 2);
                Grid.SetColumnSpan(comboBox, 1);
                comboBox.HorizontalAlignment = HorizontalAlignment.Right;
                comboBox.Margin = new Thickness(0);
            }
        }

        #endregion

        #region Theme Management

        private void ApplySavedTheme()
        {
            var theme = LoadTheme();
            ApplyTheme(theme, save: false);
            switch (theme)
            {
                case ElementTheme.Light:
                    ThemeComboBox.SelectedIndex = 0;
                    break;
                case ElementTheme.Dark:
                    ThemeComboBox.SelectedIndex = 1;
                    break;
                default:
                    ThemeComboBox.SelectedIndex = 2;
                    break;
            }
        }

        private static ElementTheme LoadTheme()
        {
            try
            {
                var v = ApplicationData.Current.LocalSettings.Values["AppTheme"] as string;
                if (Enum.TryParse(v, out ElementTheme t)) return t;
            }
            catch { }
            return ElementTheme.Default;
        }

        private void ApplyTheme(ElementTheme theme, bool save = true)
        {
            if (this.Content is FrameworkElement root)
            {
                root.RequestedTheme = theme;
            }
            if (save)
            {
                try
                {
                    ApplicationData.Current.LocalSettings.Values["AppTheme"] = theme.ToString();
                }
                catch (Exception ex)
                {
                    // Log error details for debugging: Console.WriteLine(ex.ToString());
                    System.Diagnostics.Debug.Write($"Error saving theme: {ex.Message}");
                }
            }
            UpdateTitleBarButtons();
            // A theme switch re-resolves TextBox brushes: re-assert the transparent editor mode.
            try
            {
                ApplyEditorBg(EditorBgToggle != null && EditorBgToggle.IsOn, save: false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.Write($"Error applying editor bg after theme change: {ex.Message}");
            }
        }

        private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized) return;
            if (ThemeComboBox.SelectedItem is ComboBoxItem item &&
                item.Tag is string tag)
            {
                try
                {
                    Enum.TryParse(tag, out ElementTheme theme);
                    ApplyTheme(theme);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.Write($"Error parsing theme: {ex.Message}");
                }
            }
        }

        #endregion

        #region Backdrop Management

        private void ApplySavedBackdrop()
        {
            var backdrop = LoadBackdrop();
            ApplyBackdrop(backdrop, save: false);
            BackdropComboBox.SelectedIndex = (backdrop == "Acrylic") ? 1 : 0;
        }

        private static string LoadBackdrop()
        {
            try
            {
                var v = ApplicationData.Current.LocalSettings.Values["AppBackdrop"] as string;
                if (v == "Acrylic") return "Acrylic";
            }
            catch { }
            return SettingsDefaults.Backdrop;
        }

        // Switches the window backdrop at runtime (Mica default, like Windows Notepad).
        private void ApplyBackdrop(string backdrop, bool save = true)
        {
            if (backdrop == "Acrylic")
                SystemBackdrop = new Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop();
            else
                SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
            if (save)
            {
                try
                {
                    ApplicationData.Current.LocalSettings.Values["AppBackdrop"] = backdrop;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.Write($"Error saving backdrop: {ex.Message}");
                }
            }
        }

        private void BackdropComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized) return;
            if (BackdropComboBox.SelectedItem is ComboBoxItem item &&
                item.Tag is string backdrop)
            {
                ApplyBackdrop(backdrop);
            }
        }

        #endregion

        #region Language Management

        // Returns the string matching the active language. English is the source of truth,
        // French and Spanish follow the same order everywhere so a new language only needs
        // one extra argument per string.
        private string Loc(string en, string fr, string es)
        {
            switch (_currentLang)
            {
                case LanguageHelper.English: return en;
                case LanguageHelper.Spanish: return es;
                default: return fr;
            }
        }

        private void ApplySavedLanguage()
        {
            var mode = LoadLanguage();
            ApplyLanguage(mode, save: false);
            LangComboBox.SelectedIndex = FindLanguageIndex(mode);
        }

        // Resolves the dropdown entry matching the saved mode (stays correct if items get reordered).
        private int FindLanguageIndex(string mode)
        {
            string tag = (mode == LanguageHelper.SystemTag) ? LanguageHelper.SystemTag : mode;
            for (int i = 0; i < LangComboBox.Items.Count; i++)
            {
                if (LangComboBox.Items[i] is ComboBoxItem item && item.Tag as string == tag)
                    return i;
            }
            return 0;
        }

        private static string LoadLanguage()
        {
            try
            {
                var lang = ApplicationData.Current.LocalSettings.Values["AppLanguage"] as string;
                if (!string.IsNullOrEmpty(lang)) return lang;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.Write($"Error loading language: {ex.Message}");
            }
            return LanguageHelper.SystemTag;
        }

        private void LangComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized) return;
            if (LangComboBox.SelectedItem is ComboBoxItem item &&
                item.Tag is string lang)
            {
                try
                {
                    ApplyLanguage(lang);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.Write($"Error applying language: {ex.Message}");
                }
            }
        }

        // Resolves the OS UI language; falls back to English when unsupported.
        private static string ResolveSystemLanguage()
        {
            try
            {
                return LanguageHelper.ResolveSystemLanguage(CultureInfo.CurrentUICulture.Name);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.Write($"Error resolving system language: {ex.Message}");
            }
            return LanguageHelper.English;
        }

        private void ApplyLanguage(string lang, bool save = true)
        {
            string mode = LanguageHelper.NormalizeMode(lang);
            _currentLang = (mode == LanguageHelper.SystemTag) ? ResolveSystemLanguage() : mode;
            if (save)
            {
                try
                {
                    ApplicationData.Current.LocalSettings.Values["AppLanguage"] = mode;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.Write($"Error saving language: {ex.Message}");
                }
            }

            MenuFile.Title = Loc("File", "Fichier", "Archivo");
            MenuNew.Text = Loc("New", "Nouveau", "Nuevo");
            MenuOpen.Text = Loc("Open...", "Ouvrir...", "Abrir...");
            MenuSave.Text = Loc("Save", "Enregistrer", "Guardar");
            MenuSaveAs.Text = Loc("Save As...", "Enregistrer sous...", "Guardar como...");
            MenuExit.Text = Loc("Exit", "Quitter", "Salir");

            MenuEdit.Title = Loc("Edit", "Édition", "Editar");
            MenuUndo.Text = Loc("Undo", "Annuler", "Deshacer");
            MenuRedo.Text = Loc("Redo", "Rétablir", "Rehacer");
            MenuCut.Text = Loc("Cut", "Couper", "Cortar");
            MenuCopy.Text = Loc("Copy", "Copier", "Copiar");
            MenuPaste.Text = Loc("Paste", "Coller", "Pegar");
            MenuSelectAll.Text = Loc("Select All", "Tout sélectionner", "Seleccionar todo");
            MenuView.Title = Loc("View", "Affichage", "Ver");
            MenuPreview.Text = Loc("Markdown preview", "Aperçu Markdown", "Vista previa de Markdown");
            ToolTipService.SetToolTip(PreviewToggleButton, Loc("Markdown preview", "Aperçu Markdown", "Vista previa de Markdown"));

            if (_contextUndo != null) _contextUndo.Text = Loc("Undo", "Annuler", "Deshacer");
            if (_contextRedo != null) _contextRedo.Text = Loc("Redo", "Rétablir", "Rehacer");
            if (_contextCut != null) _contextCut.Text = Loc("Cut", "Couper", "Cortar");
            if (_contextCopy != null) _contextCopy.Text = Loc("Copy", "Copier", "Copiar");
            if (_contextPaste != null) _contextPaste.Text = Loc("Paste", "Coller", "Pegar");
            if (_contextSelectAll != null) _contextSelectAll.Text = Loc("Select All", "Tout sélectionner", "Seleccionar todo");

            ToolTipService.SetToolTip(SettingsButton, Loc("Settings", "Paramètres", "Configuración"));
            ToolTipService.SetToolTip(BackButton, Loc("Back", "Retour", "Atrás"));
            SettingsTitleText.Text = Loc("Settings", "Paramètres", "Configuración");

            SectionAppearanceTitle.Text = Loc("Appearance", "Apparence", "Apariencia");
            ThemeHeaderTitle.Text = Loc("App theme", "Thème de l'application", "Tema de la aplicación");
            ThemeHeaderDesc.Text = Loc("Select which app theme to display in Skrib", "Choisissez le thème à afficher dans Skrib", "Elige el tema que se mostrará en Skrib");
            ThemeLightComboText.Text = Loc("Light", "Clair", "Claro");
            ThemeDarkComboText.Text = Loc("Dark", "Sombre", "Oscuro");
            ThemeSystemComboText.Text = Loc("Use system theme", "Utiliser le thème du système", "Usar el tema del sistema");
            BackdropHeaderTitle.Text = Loc("Background", "Arrière-plan", "Fondo");
            BackdropHeaderDesc.Text = Loc("Choose the background shown in Skrib", "Choisissez l'arrière-plan affiché dans Skrib", "Elige el fondo que se mostrará en Skrib");
            BackdropMicaComboText.Text = "Mica";
            BackdropAcrylicComboText.Text = Loc("Acrylic", "Acrylique", "Acrílico");

            LangHeaderTitle.Text = Loc("Language", "Langue", "Idioma");
            LangHeaderDesc.Text = Loc("Choose application display language", "Choisissez la langue de l'application", "Elige el idioma de la aplicación");
            LangFrComboText.Text = Loc("French (FR)", "Français (FR)", "Français (FR)");
            LangEsComboText.Text = Loc("Spanish (ES)", "Español (ES)", "Español (ES)");
            LangEnComboText.Text = "English (EN)";
            LangSystemComboText.Text = Loc("System", "Système", "Sistema");

            SectionEditorTitle.Text = Loc("Editor", "Éditeur", "Editor");
            WordWrapTitle.Text = Loc("Word wrap", "Retour automatique à la ligne", "Ajuste de línea");
            WordWrapDesc.Text = Loc("Wrap long lines of text to fit the window width", "Renvoyer automatiquement le texte à la ligne", "Ajusta las líneas largas de texto al ancho de la ventana");
            EditorBgTitle.Text = Loc("Text area background", "Fond de la zone de texte", "Fondo del área de texto");
            EditorBgDesc.Text = Loc("Show the app background behind the text", "Afficher l'arrière-plan de l'application derrière le texte", "Mostrar el fondo de la aplicación detrás del texto");

            // The editor welcome message is only visible while the document is empty.
            Editor.PlaceholderText = Loc("Start writing…", "Commencez à écrire…", "Empieza a escribir…");

            SectionAboutTitle.Text = Loc("About this app", "À propos de cette application", "Acerca de esta aplicación");
            AboutTitle.Text = "Skrib";
            GitHubButtonText.Text = "GitHub";
            RateButtonText.Text = Loc("Rate", "Noter", "Valorar");

            UpdateAboutVersion();
            UpdateTitle();
            UpdateCaretInfo();
            UpdateWordWrapState();
            UpdateEditorBgState();
            UpdateMarkdownViewState();
        }

        #endregion

        #region Editor and Word Wrap

        private void ApplySavedWordWrap()
        {
            try
            {
                var wrap = ApplicationData.Current.LocalSettings.Values["WordWrap"];
                if (wrap is bool isWrap)
                {
                    WordWrapToggle.IsOn = isWrap;
                    Editor.TextWrapping = isWrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
                    UpdateWordWrapState();
                }
            }
            catch { }
        }

        private void WordWrapToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            Editor.TextWrapping = WordWrapToggle.IsOn ? TextWrapping.Wrap : TextWrapping.NoWrap;
            try { ApplicationData.Current.LocalSettings.Values["WordWrap"] = WordWrapToggle.IsOn; } catch { }
            UpdateWordWrapState();
        }

        // Keeps the toggle state label in sync (fixed-width label so the toggle never shifts).
        private void UpdateWordWrapState()
        {
            bool isOn = WordWrapToggle != null && WordWrapToggle.IsOn;
            WordWrapStateText.Text = isOn ? Loc("On", "Activé", "Activado") : Loc("Off", "Désactivé", "Desactivado");
        }


        // Transparent mode lets the window backdrop show through the text area.
        // On first run (no settings file yet) the default is the opaque editor
        // background, which is what the XAML theme dictionaries already provide.
        private void ApplySavedEditorBg()
        {
            bool transparent = SettingsDefaults.EditorTransparent;
            try
            {
                var v = ApplicationData.Current.LocalSettings.Values["EditorTransparent"];
                if (v is bool on) transparent = on;
            }
            catch { }

            EditorBgToggle.IsOn = transparent;
            ApplyEditorBg(transparent, save: false);
        }

        private void EditorBgToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            ApplyEditorBg(EditorBgToggle.IsOn);
        }

        // Transparent mode overrides every TextBox background state on both resource lookup paths
        // (plain local entries and the Light/Dark theme dictionaries), so hover/focus can never
        // repaint the text area. Opaque mode sets the theme-matching brush explicitly instead of
        // relying on ClearValue + style resolution.
        private void ApplyEditorBg(bool transparent, bool save = true)
        {
            var bg = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            string[] keys =
            {
                "TextControlBackground",
                "TextControlBackgroundPointerOver",
                "TextControlBackgroundFocused",
                "TextControlBackgroundDisabled"
            };
            if (transparent)
            {
                Editor.Background = bg;
                MarkdownPreviewHost.Background = bg;
                foreach (var key in keys)
                {
                    Editor.Resources[key] = bg;
                    SetEditorThemeBrush("Light", key, bg);
                    SetEditorThemeBrush("Dark", key, bg);
                }
            }
            else
            {
                // Explicit opaque brush for the current theme (no ClearValue: the final
                // background must not depend on default style resolution).
                var opaqueBrush = GetOpaqueTextBrush();
                Editor.Background = opaqueBrush;
                // Restore the opaque brush matching Editor.Resources (#FFFFFF light / #202020 dark).
                // Application.Current.Resources does NOT contain our local overrides, so resolve by theme.
                MarkdownPreviewHost.Background = opaqueBrush;
                foreach (var key in keys)
                {
                    Editor.Resources.Remove(key);
                    RestoreEditorThemeBrush("Light", key);
                    RestoreEditorThemeBrush("Dark", key);
                }
            }
            if (save)
            {
                try { ApplicationData.Current.LocalSettings.Values["EditorTransparent"] = transparent; } catch { }
            }
            // Force ThemeResource re-resolution so the overridden brushes apply immediately.
            var currentTheme = Editor.RequestedTheme;
            Editor.RequestedTheme = currentTheme == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
            Editor.RequestedTheme = currentTheme;
            UpdateEditorBgState();
        }

        // Opaque brush shared by the editor and the Markdown preview host.
        // Must match the XAML ThemeDictionaries in Editor.Resources (#FFFFFF light / #202020 dark).
        private Microsoft.UI.Xaml.Media.Brush GetOpaqueTextBrush()
        {
            return new Microsoft.UI.Xaml.Media.SolidColorBrush(GetOpaqueEditorColor());
        }

        // The opaque editor background color for the current theme.
        private Windows.UI.Color GetOpaqueEditorColor()
        {
            bool isDark = false;
            try
            {
                if (this.Content is FrameworkElement root)
                {
                    if (root.RequestedTheme == ElementTheme.Dark) isDark = true;
                    else if (root.RequestedTheme == ElementTheme.Light) isDark = false;
                    else isDark = root.ActualTheme == ElementTheme.Dark;
                }
                else
                {
                    isDark = Editor.ActualTheme == ElementTheme.Dark;
                }
            }
            catch { }
            return isDark
                ? Windows.UI.Color.FromArgb(0xFF, 0x20, 0x20, 0x20)
                : Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
        }

        // Saves the XAML-defined theme brush once, then overrides it.
        private void SetEditorThemeBrush(string theme, string key, Microsoft.UI.Xaml.Media.Brush brush)
        {
            try
            {
                if (Editor.Resources.ThemeDictionaries.TryGetValue(theme, out var entry) && entry is ResourceDictionary rd)
                {
                    string slot = theme + "/" + key;
                    if (!_origEditorBgBrushes.ContainsKey(slot))
                        _origEditorBgBrushes[slot] = rd.TryGetValue(key, out var current) ? current as Microsoft.UI.Xaml.Media.Brush : null;
                    rd[key] = brush;
                }
            }
            catch { }
        }

        // Restores the XAML-defined theme brush; leaves it alone if we never overrode it.
        private void RestoreEditorThemeBrush(string theme, string key)
        {
            try
            {
                if (!_origEditorBgBrushes.TryGetValue(theme + "/" + key, out var orig)) return;
                if (Editor.Resources.ThemeDictionaries.TryGetValue(theme, out var entry) && entry is ResourceDictionary rd)
                {
                    if (orig != null) rd[key] = orig;
                    else rd.Remove(key);
                }
            }
            catch { }
        }

        // Keeps the toggle state label in sync (fixed-width label so the toggle never shifts).
        private void UpdateEditorBgState()
        {
            bool isOn = EditorBgToggle != null && EditorBgToggle.IsOn;
            EditorBgStateText.Text = isOn ? Loc("On", "Activé", "Activado") : Loc("Off", "Désactivé", "Desactivado");
        }
        private void UpdateTitle()
        {
            var untitled = Loc("Untitled", "Sans titre", "Sin título");
            var ready = Loc("Ready", "Prêt", "Listo");
            var name = _currentFile != null ? _currentFile.Name : untitled;
            var dirtyMark = _isDirty ? "*" : "";
            this.Title = $"Skrib - {name}{dirtyMark}";
            if (WindowTitleText != null)
            {
                WindowTitleText.Text = "Skrib";
            }
            if (WindowTitleStatus != null)
            {
                // Show the dirty marker in the visible custom title bar, not only in the OS title.
                WindowTitleStatus.Text = $"{name}{dirtyMark}";
            }
            try
            {
                StatusText.Text = _currentFile != null ? _currentFile.Path : ready;
            }
            catch (Exception)
            {
                // Log would be added here for debugging
                StatusText.Text = _currentFile != null ? _currentFile.Name : ready;
            }
            UpdateMarkdownViewState();
        }

        // Sets editor text without flagging the document as dirty.
        private void SetEditorText(string text)
        {
            _suppressDirty = true;
            try
            {
                Editor.Text = text;
            }
            finally
            {
                _suppressDirty = false;
            }
            _isDirty = false;
            if (_showMarkdownPreview)
            {
                if (IsMarkdownFile()) RenderMarkdownPreview();
                else SetMarkdownPreview(false);
            }
            UpdateTitle();
            UpdateCaretInfo();
        }

        private void Editor_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressDirty)
            {
                return;
            }
            _isDirty = true;
            if (_titleRefreshQueued)
            {
                return;
            }

            _titleRefreshQueued = true;
            _ = DispatcherQueue.TryEnqueue(() =>
            {
                _titleRefreshQueued = false;
                UpdateTitle();
                UpdateCaretInfo();
            });
        }

        private void Editor_SelectionChanged(object sender, RoutedEventArgs e)
        {
            UpdateCaretInfo();
        }

        // Updates the line/column/character count shown in the status bar.
        private void UpdateCaretInfo()
        {
            string text = Editor?.Text ?? string.Empty;
            int pos = Editor != null ? Editor.SelectionStart : 0;
            pos = Math.Max(0, Math.Min(pos, text.Length));
            int line = 1;
            int lastBreak = -1;
            for (int i = 0; i < pos; i++)
            {
                if (text[i] == '\n')
                {
                    line++;
                    lastBreak = i;
                }
            }
            int col = pos - lastBreak;
            string countLabel = Loc("chars", "caractères", "caracteres");
            if (_showMarkdownPreview)
                CaretInfoText.Text = Loc("Preview | ", "Aperçu | ", "Vista previa | ") + text.Length + " " + countLabel;
            else
                CaretInfoText.Text = $"Ln {line}, Col {col} | {text.Length} {countLabel}";
        }

        private async void NewFile_Click(object sender, RoutedEventArgs e)
        {
            if (!await AskSaveIfNeededAsync()) return;
            SetEditorText(string.Empty);
            _currentFile = null;
            _isDirty = false;
            UpdateTitle();
            UpdateCaretInfo();
        }

        private async void OpenFile_Click(object sender, RoutedEventArgs e)
        {
            if (!await AskSaveIfNeededAsync()) return;

            try
            {
                var picker = new FileOpenPicker();
                try { InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this)); } catch { }
                picker.FileTypeFilter.Clear();
                picker.FileTypeFilter.Add(".txt");
                picker.FileTypeFilter.Add(".md");

                var file = await picker.PickSingleFileAsync();
                if (file != null)
                {
                    try
                    {
                        var text = await FileIO.ReadTextAsync(file);
                        _currentFile = file;
                        SetEditorText(text);
                    }
                    catch (Exception ex)
                    {
                        await ShowErrorAsync(Loc("Unable to read file", "Impossible de lire le fichier", "No se puede leer el archivo"), ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                await ShowErrorAsync(Loc("Open error", "Erreur lors de l'ouverture", "Error al abrir"), ex.Message);
            }
        }

        private async void SaveFile_Click(object sender, RoutedEventArgs e)
        {
            try { await SaveAsync(); } catch (Exception ex) { await ShowErrorAsync(Loc("Save error", "Erreur lors de l'enregistrement", "Error al guardar"), ex.Message); }
        }

        private async void SaveAsFile_Click(object sender, RoutedEventArgs e)
        {
            try { await SaveAsAsync(); } catch (Exception ex) { await ShowErrorAsync(Loc("Save error", "Erreur lors de l'enregistrement", "Error al guardar"), ex.Message); }
        }

        private async Task<bool> SaveAsync()
        {
            if (_currentFile == null)
            {
                return await SaveAsAsync();
            }
            try
            {
                await FileIO.WriteTextAsync(_currentFile, Editor.Text);
            }
            catch (FileNotFoundException)
            {
                // The original file was moved or deleted: fall back to Save As.
                return await SaveAsAsync();
            }
            catch (Exception ex) when ((uint)ex.HResult == 0x80070005)
            {
                // Access denied: fall back to Save As so the user can pick a new location.
                return await SaveAsAsync();
            }
            _isDirty = false;
            UpdateTitle();
            return true;
        }

        private async Task<bool> SaveAsAsync()
        {
            var picker = new FileSavePicker();
            try { InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this)); } catch { }
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.DefaultFileExtension = ".txt";
            var untitled = Loc("Untitled", "Sans titre", "Sin título");
            var txtLabel = Loc("Text Document", "Document texte (.txt)", "Documento de texto (.txt)");
            var mdLabel = Loc("Markdown", "Markdown (.md)", "Markdown (.md)");
            picker.SuggestedFileName = _currentFile != null ? Path.GetFileNameWithoutExtension(_currentFile.Name) : untitled;
            picker.FileTypeChoices.Clear();
            picker.FileTypeChoices.Add(txtLabel, new System.Collections.Generic.List<string>() { ".txt" });
            picker.FileTypeChoices.Add(mdLabel, new System.Collections.Generic.List<string>() { ".md" });
            var file = await picker.PickSaveFileAsync();
            if (file == null) return false;
            await FileIO.WriteTextAsync(file, Editor.Text);
            _currentFile = file;
            _isDirty = false;
            UpdateTitle();
            return true;
        }

        private async Task<bool> AskSaveIfNeededAsync()
        {
            if (!_isDirty) return true;
            var dialog = new ContentDialog
            {
                Title = Loc("Save changes?", "Enregistrer les modifications ?", "¿Guardar los cambios?"),
                Content = Loc("The document has been modified. Do you want to save it?", "Le document a été modifié. Voulez-vous l'enregistrer ?", "El documento se ha modificado. ¿Quieres guardarlo?"),
                PrimaryButtonText = Loc("Save", "Enregistrer", "Guardar"),
                SecondaryButtonText = Loc("Don't save", "Ne pas enregistrer", "No guardar"),
                CloseButtonText = Loc("Cancel", "Annuler", "Cancelar"),
                XamlRoot = this.Content.XamlRoot
            };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                return await SaveAsync();
            }
            else if (result == ContentDialogResult.Secondary)
            {
                return true;
            }
            return false;
        }

        // Opens the project page on GitHub in the default browser.
        private async void GitHubButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await Launcher.LaunchUriAsync(new Uri("https://github.com/Gtisseran/Skrib"));
            }
            catch { }
        }

        // Opens the Store review page so users can rate the app (reviews are answered in Partner Center).
        private async void RateButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                bool launched = await Launcher.LaunchUriAsync(new Uri("ms-windows-store://review/?productid=9p9tb8st018k"));
                if (!launched)
                {
                    await Launcher.LaunchUriAsync(new Uri("https://apps.microsoft.com/detail/9p9tb8st018k"));
                }
            }
            catch { }
        }

        private static string GetVersionString()
        {
            try
            {
                var v = Windows.ApplicationModel.Package.Current.Id.Version;
                return v.Major + "." + v.Minor + "." + v.Build + "." + v.Revision;
            }
            catch { }
            try
            {
                var asm = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                if (asm != null) return asm.Major + "." + asm.Minor + "." + asm.Build + "." + asm.Revision;
            }
            catch { }
            return "1.0.0.0";
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            _ = ExitAsync();
        }

        private async Task ExitAsync()
        {
            if (!await AskSaveIfNeededAsync()) return;
            Application.Current.Exit();
        }

        private void Undo_Click(object sender, RoutedEventArgs e)
        {
            if (Editor.CanUndo)
            {
                Editor.Undo();
            }
        }

        private void Redo_Click(object sender, RoutedEventArgs e)
        {
            if (Editor.CanRedo)
            {
                Editor.Redo();
            }
        }

        // NOTE: Edit menu items have no KeyboardAccelerators on purpose.
        // The TextBox already handles Ctrl+Z/Y/X/C/V/A natively; adding
        // accelerators would run the action twice (e.g. pasted text duplicated).
        private void Cut_Click(object sender, RoutedEventArgs e)
        {
            Editor.CutSelectionToClipboard();
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            Editor.CopySelectionToClipboard();
        }

        private void Paste_Click(object sender, RoutedEventArgs e)
        {
            Editor.PasteFromClipboard();
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            Editor.SelectAll();
            Editor.Focus(FocusState.Programmatic);
        }

        private async Task ShowErrorAsync(string title, string message)
        {
            // ContentDialog requires a valid XamlRoot; skip if the window is not ready yet.
            if (this.Content?.XamlRoot == null)
            {
                return;
            }
            var dlg = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "OK",
                RequestedTheme = LoadTheme(),
                XamlRoot = this.Content.XamlRoot
            };
            try
            {
                await dlg.ShowAsync();
            }
            catch (Exception ex)
            {
                // Log dialog failure details for debugging: Console.WriteLine(ex.ToString());
                System.Diagnostics.Debug.Write($"Error showing dialog: {ex.Message}");
            }
        }

        #region Markdown Preview

        // The preview is only available for Markdown files; other files always use the editor.
        private bool IsMarkdownFile()
        {
            try
            {
                if (_currentFile == null) return false;
                if (!string.IsNullOrEmpty(_currentFile.FileType))
                    return string.Equals(_currentFile.FileType, ".md", StringComparison.OrdinalIgnoreCase);
                return _currentFile.Name.EndsWith(".md", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        // Syncs the View menu, the header toggle and the editor/preview visibility.
        private void UpdateMarkdownViewState()
        {
            bool isMd = IsMarkdownFile();
            if (MenuPreview != null) MenuPreview.IsEnabled = isMd;
            if (PreviewToggleButton != null)
                PreviewToggleButton.Visibility = isMd ? Visibility.Visible : Visibility.Collapsed;
            if (!isMd && _showMarkdownPreview)
            {
                SetMarkdownPreview(false);
            }
            else
            {
                if (MenuPreview != null) MenuPreview.IsChecked = _showMarkdownPreview;
                if (PreviewToggleButton != null) PreviewToggleButton.IsChecked = _showMarkdownPreview;
            }
        }

        private void SetMarkdownPreview(bool show)
        {
            if (show && !IsMarkdownFile()) return;
            _showMarkdownPreview = show;
            try
            {
                if (show) RenderMarkdownPreview();
                Editor.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
                MarkdownPreviewHost.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                if (MenuPreview != null) MenuPreview.IsChecked = show;
                if (PreviewToggleButton != null) PreviewToggleButton.IsChecked = show;
                // The edit menu targets the hidden editor, so disable it while previewing.
                if (MenuEdit != null) MenuEdit.IsEnabled = !show;
            }
            catch { }
            UpdateCaretInfo();
        }

        // Builds the native preview content from the current document. Synchronous on
        // purpose: parsing and building paragraphs is instant for note-sized documents,
        // and there is no browser process to keep alive anymore.
        private void RenderMarkdownPreview()
        {
            try
            {
                bool light = (this.Content as FrameworkElement)?.ActualTheme != ElementTheme.Dark;
                var blocks = MarkdownPreviewModel.Parse(Editor.Text ?? string.Empty);
                MarkdownPreviewBuilder.Fill(MarkdownPreview, blocks, light);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.Write($"Error rendering Markdown preview: {ex.Message}");
            }
        }

        // Ctrl+A in the preview selects the whole document (RichTextBlock has no built-in
        // SelectAll; TextPointer-based selection spans every block).
        private void SelectAllInPreview()
        {
            try
            {
                MarkdownPreview.Select(MarkdownPreview.ContentStart, MarkdownPreview.ContentEnd);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.Write($"Error selecting preview text: {ex.Message}");
            }
        }

        private void MenuPreview_Click(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleMenuFlyoutItem item) SetMarkdownPreview(item.IsChecked);
        }

        private void PreviewToggleButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton button) SetMarkdownPreview(button.IsChecked == true);
        }

        #endregion

        public async Task OpenFileFromStorageFileAsync(StorageFile file)
        {
            try
            {
                // Prompt to save current work before replacing it with the activated file.
                if (!await AskSaveIfNeededAsync())
                {
                    return;
                }
                var text = await FileIO.ReadTextAsync(file);
                _currentFile = file;
                SetEditorText(text);
            }
            catch (Exception ex)
            {
                await ShowErrorAsync(Loc("Unable to read file", "Impossible de lire le fichier", "No se puede leer el archivo"), ex.Message);
            }
        }

        #endregion
    }
}
