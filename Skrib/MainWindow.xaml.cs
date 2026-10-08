using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Windowing;
using System;
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

        public MainWindow()
        {
            this.InitializeComponent();
            ConfigureCustomTitleBar();
            InitializeContextMenu();
            SetMinimumWindowSize();
            SetWindowIcon();
            ApplySavedTheme();
            ApplySavedLanguage();
            ApplySavedWordWrap();
            UpdateAboutVersion();
            UpdateCaretInfo();
            UpdateWordWrapState();
            _isInitialized = true;
        }

        private void ConfigureCustomTitleBar()
        {
            try
            {
                this.ExtendsContentIntoTitleBar = true;
                if (this.Content is FrameworkElement root && root.FindName("AppTitleBar") is Border titleBar)
                {
                    this.SetTitleBar(titleBar);
                }

                UpdateTitleBarButtons();
            }
            catch { }
        }

        // Adapts caption button hover colors so they stay visible in both light and dark themes.
        private void UpdateTitleBarButtons()
        {
            try
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
            catch { }
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
            try
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
            catch { }
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
                    catch { }
                }

                if (File.Exists(iconPath) && appWindow != null)
                {
                    try { appWindow.SetIcon(iconPath); } catch { }
                }
            }
            catch { }
        }

        private void UpdateAboutVersion()
        {
            try
            {
                var v = Windows.ApplicationModel.Package.Current.Id.Version;
                var versionStr = $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";
                AboutDesc.Text = $"Version {versionStr}";
            }
            catch
            {
                AboutDesc.Text = "Version 1.0.2.0";
            }
        }

        #region Settings Page Navigation

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            MainView.Visibility = Visibility.Collapsed;
            SettingsView.Visibility = Visibility.Visible;
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
                try { ApplicationData.Current.LocalSettings.Values["AppTheme"] = theme.ToString(); } catch { }
            }
            UpdateTitleBarButtons();
        }

        private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized) return;
            if (ThemeComboBox.SelectedItem is ComboBoxItem item &&
                item.Tag is string tag &&
                Enum.TryParse(tag, out ElementTheme theme))
            {
                ApplyTheme(theme);
            }
        }

        #endregion

        #region Language Management

        private void ApplySavedLanguage()
        {
            var lang = LoadLanguage();
            ApplyLanguage(lang, save: false);
            LangComboBox.SelectedIndex = (lang == "en") ? 1 : 0;
        }

        private static string LoadLanguage()
        {
            try
            {
                var lang = ApplicationData.Current.LocalSettings.Values["AppLanguage"] as string;
                if (!string.IsNullOrEmpty(lang)) return lang;
            }
            catch { }
            return "fr";
        }

        private void LangComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized) return;
            if (LangComboBox.SelectedItem is ComboBoxItem item &&
                item.Tag is string lang)
            {
                ApplyLanguage(lang);
            }
        }

        private void ApplyLanguage(string lang, bool save = true)
        {
            _currentLang = (lang == "en") ? "en" : "fr";
            if (save)
            {
                try { ApplicationData.Current.LocalSettings.Values["AppLanguage"] = _currentLang; } catch { }
            }

            bool isEn = _currentLang == "en";

            if (isEn)
            {
                MenuFile.Title = "File";
                MenuNew.Text = "New";
                MenuOpen.Text = "Open...";
                MenuSave.Text = "Save";
                MenuSaveAs.Text = "Save As...";
                MenuExit.Text = "Exit";

                MenuEdit.Title = "Edit";
                MenuUndo.Text = "Undo";
                MenuRedo.Text = "Redo";
                MenuCut.Text = "Cut";
                MenuCopy.Text = "Copy";
                MenuPaste.Text = "Paste";
                MenuSelectAll.Text = "Select All";

                if (_contextUndo != null) _contextUndo.Text = "Undo";
                if (_contextRedo != null) _contextRedo.Text = "Redo";
                if (_contextCut != null) _contextCut.Text = "Cut";
                if (_contextCopy != null) _contextCopy.Text = "Copy";
                if (_contextPaste != null) _contextPaste.Text = "Paste";
                if (_contextSelectAll != null) _contextSelectAll.Text = "Select All";

                ToolTipService.SetToolTip(SettingsButton, "Settings");
                ToolTipService.SetToolTip(BackButton, "Back");
                SettingsTitleText.Text = "Settings";

                SectionAppearanceTitle.Text = "Appearance";
                ThemeHeaderTitle.Text = "App theme";
                ThemeHeaderDesc.Text = "Select which app theme to display in Skrib";
                ThemeLightComboText.Text = "Light";
                ThemeDarkComboText.Text = "Dark";
                ThemeSystemComboText.Text = "Use system theme";

                LangHeaderTitle.Text = "Language";
                LangHeaderDesc.Text = "Choose application display language";
                LangFrComboText.Text = "French (FR)";
                LangEnComboText.Text = "English (EN)";

                SectionEditorTitle.Text = "Editor";
                WordWrapTitle.Text = "Word wrap";
                WordWrapDesc.Text = "Wrap long lines of text to fit the window width";

                SectionAboutTitle.Text = "About this app";
                AboutTitle.Text = "Skrib";
                GitHubButtonText.Text = "View on GitHub";
                StoreButtonText.Text = "Check for updates";
                ToolTipService.SetToolTip(StoreButton, "Open the Microsoft Store page (updates install from the Store)");
            }
            else
            {
                MenuFile.Title = "Fichier";
                MenuNew.Text = "Nouveau";
                MenuOpen.Text = "Ouvrir...";
                MenuSave.Text = "Enregistrer";
                MenuSaveAs.Text = "Enregistrer sous...";
                MenuExit.Text = "Quitter";

                MenuEdit.Title = "Édition";
                MenuUndo.Text = "Annuler";
                MenuRedo.Text = "Rétablir";
                MenuCut.Text = "Couper";
                MenuCopy.Text = "Copier";
                MenuPaste.Text = "Coller";
                MenuSelectAll.Text = "Tout sélectionner";

                if (_contextUndo != null) _contextUndo.Text = "Annuler";
                if (_contextRedo != null) _contextRedo.Text = "Rétablir";
                if (_contextCut != null) _contextCut.Text = "Couper";
                if (_contextCopy != null) _contextCopy.Text = "Copier";
                if (_contextPaste != null) _contextPaste.Text = "Coller";
                if (_contextSelectAll != null) _contextSelectAll.Text = "Tout sélectionner";

                ToolTipService.SetToolTip(SettingsButton, "Paramètres");
                ToolTipService.SetToolTip(BackButton, "Retour");
                SettingsTitleText.Text = "Paramètres";

                SectionAppearanceTitle.Text = "Apparence";
                ThemeHeaderTitle.Text = "Thème de l'application";
                ThemeHeaderDesc.Text = "Choisissez le thème à afficher dans Skrib";
                ThemeLightComboText.Text = "Clair";
                ThemeDarkComboText.Text = "Sombre";
                ThemeSystemComboText.Text = "Utiliser le thème du système";

                LangHeaderTitle.Text = "Langue";
                LangHeaderDesc.Text = "Choisissez la langue de l'application";
                LangFrComboText.Text = "Français (FR)";
                LangEnComboText.Text = "English (EN)";

                SectionEditorTitle.Text = "Éditeur";
                WordWrapTitle.Text = "Retour automatique à la ligne";
                WordWrapDesc.Text = "Ajuster le texte pour qu'il tienne dans la largeur de la fenêtre";

                SectionAboutTitle.Text = "À propos de cette application";
                AboutTitle.Text = "Skrib";
                GitHubButtonText.Text = "Voir sur GitHub";
                StoreButtonText.Text = "Vérifier les mises à jour";
                ToolTipService.SetToolTip(StoreButton, "Ouvrir la page Microsoft Store (mises é jour installées via le Store)");
            }

            UpdateAboutVersion();
            UpdateTitle();
            UpdateCaretInfo();
            UpdateWordWrapState();
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
            try
            {
                bool isOn = WordWrapToggle != null && WordWrapToggle.IsOn;
                bool isEn = _currentLang == "en";
                WordWrapStateText.Text = isOn ? (isEn ? "On" : "Activé") : (isEn ? "Off" : "Désactivé");
            }
            catch { }
        }

        private void UpdateTitle()
        {
            var untitled = _currentLang == "en" ? "Untitled" : "Sans titre";
            var ready = _currentLang == "en" ? "Ready" : "Prêt";
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
            catch
            {
                StatusText.Text = _currentFile != null ? _currentFile.Name : ready;
            }
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
            try
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
                bool isEn = _currentLang == "en";
                string countLabel = isEn ? "chars" : "caractères";
                CaretInfoText.Text = $"Ln {line}, Col {col} | {text.Length} {countLabel}";
            }
            catch { }
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
                        await ShowErrorAsync(_currentLang == "en" ? "Unable to read file" : "Impossible de lire le fichier", ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                await ShowErrorAsync(_currentLang == "en" ? "Open error" : "Erreur ouverture", ex.Message);
            }
        }

        private async void SaveFile_Click(object sender, RoutedEventArgs e)
        {
            try { await SaveAsync(); } catch (Exception ex) { await ShowErrorAsync(_currentLang == "en" ? "Save error" : "Erreur sauvegarde", ex.Message); }
        }

        private async void SaveAsFile_Click(object sender, RoutedEventArgs e)
        {
            try { await SaveAsAsync(); } catch (Exception ex) { await ShowErrorAsync(_currentLang == "en" ? "Save error" : "Erreur sauvegarde", ex.Message); }
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
            var untitled = _currentLang == "en" ? "Untitled" : "Sans titre";
            var txtLabel = _currentLang == "en" ? "Text Document" : "Document texte (.txt)";
            var mdLabel = _currentLang == "en" ? "Markdown" : "Markdown (.md)";
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
            bool isEn = _currentLang == "en";
            var dialog = new ContentDialog
            {
                Title = isEn ? "Save changes?" : "Enregistrer les modifications ?",
                Content = isEn ? "The document has been modified. Do you want to save it?" : "Le document a été modifié. Voulez-vous l'enregistrer ?",
                PrimaryButtonText = isEn ? "Save" : "Enregistrer",
                SecondaryButtonText = isEn ? "Don't save" : "Ne pas enregistrer",
                CloseButtonText = isEn ? "Cancel" : "Annuler",
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

        // Opens the Microsoft Store listing (updates are handled by the Store, no GitHub updater).
        private async void StoreButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                bool launched = await Launcher.LaunchUriAsync(new Uri("ms-windows-store://pdp/?productid=9p9tb8st018k"));
                if (!launched)
                {
                    await Launcher.LaunchUriAsync(new Uri("https://apps.microsoft.com/detail/9p9tb8st018k"));
                }
            }
            catch { }
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
            try
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
                    XamlRoot = this.Content.XamlRoot
                };
                await dlg.ShowAsync();
            }
            catch { }
        }

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
                await ShowErrorAsync(_currentLang == "en" ? "Unable to read file" : "Impossible de lire le fichier", ex.Message);
            }
        }

        #endregion
    }
}