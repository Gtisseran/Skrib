using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Windowing;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.ApplicationModel.DataTransfer;
using WinRT.Interop;
using System.Runtime.InteropServices;
using Windows.System;

namespace Skrib
{
    public sealed partial class MainWindow : Window
    {
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr LoadImage(IntPtr hinst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

        private const uint WM_SETICON = 0x0080;
        private const IntPtr ICON_SMALL = 0;
        private const IntPtr ICON_BIG = (IntPtr)1;
        private const uint IMAGE_ICON = 1;
        private const uint LR_LOADFROMFILE = 0x0010;
        private const uint LR_DEFAULTSIZE = 0x0040;

        private StorageFile? _currentFile;
        private bool _isDirty;
        private string _currentLang = "fr";
        private bool _isInitialized = false;

        public MainWindow()
        {
            this.InitializeComponent();
            SetMinimumWindowSize();
            SetWindowIcon();
            ApplySavedTheme();
            ApplySavedLanguage();
            ApplySavedWordWrap();
            UpdateAboutVersion();
            _isInitialized = true;
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
                    presenter.PreferredMinimumWidth = 500;
                    presenter.PreferredMinimumHeight = 350;
                }
            }
            catch { }
        }

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

                if (File.Exists(iconPath))
                {
                    if (appWindow != null)
                    {
                        try { appWindow.SetIcon(iconPath); } catch { }
                    }

                    try
                    {
                        IntPtr hSmallIcon = LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, 16, 16, LR_LOADFROMFILE);
                        if (hSmallIcon != IntPtr.Zero)
                        {
                            SendMessage(hwnd, WM_SETICON, ICON_SMALL, hSmallIcon);
                        }
                        IntPtr hBigIcon = LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, 32, 32, LR_LOADFROMFILE);
                        if (hBigIcon != IntPtr.Zero)
                        {
                            SendMessage(hwnd, WM_SETICON, ICON_BIG, hBigIcon);
                        }
                    }
                    catch { }
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
                AboutDesc.Text = "Version 1.0.4";
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

        #region GitHub Updates

        private async void CheckUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            bool isEn = _currentLang == "en";
            CheckUpdateButton.IsEnabled = false;
            UpdateCheckProgress.Visibility = Visibility.Visible;
            UpdateCheckProgress.IsActive = true;
            UpdateInfoBar.IsOpen = false;

            try
            {
                var result = await GitHubUpdateService.CheckAsync();
                switch (result.Status)
                {
                    case UpdateCheckStatus.UpToDate:
                    case UpdateCheckStatus.NoRelease:
                        UpdateInfoBar.Severity = InfoBarSeverity.Success;
                        UpdateInfoBar.Title = isEn ? "No update available" : "Aucune mise à jour disponible";
                        UpdateInfoBar.Message = isEn
                            ? $"You already have the latest version ({result.LocalVersion})."
                            : $"Vous avez déjà la dernière version ({result.LocalVersion}).";
                        UpdateInfoBar.IsOpen = true;
                        break;

                    case UpdateCheckStatus.UpdateAvailable:
                        await ShowUpdateAvailableAsync(result);
                        break;

                    default:
                        UpdateInfoBar.Severity = InfoBarSeverity.Error;
                        UpdateInfoBar.Title = isEn ? "Update check failed" : "Vérification impossible";
                        UpdateInfoBar.Message = isEn
                            ? "Unable to check for updates. Try again later."
                            : "Impossible de vérifier les mises à jour. Réessayez plus tard.";
                        UpdateInfoBar.IsOpen = true;
                        break;
                }
            }
            finally
            {
                UpdateCheckProgress.IsActive = false;
                UpdateCheckProgress.Visibility = Visibility.Collapsed;
                CheckUpdateButton.IsEnabled = true;
            }
        }

        private async Task ShowUpdateAvailableAsync(UpdateCheckResult result)
        {
            bool isEn = _currentLang == "en";
            var notes = string.IsNullOrWhiteSpace(result.Release?.Body)
                ? (isEn ? "No release notes." : "Pas de notes de version.")
                : result.Release!.Body.Trim();

            var panel = new StackPanel { Spacing = 12 };
            panel.Children.Add(new TextBlock
            {
                Text = isEn
                    ? $"Version {result.RemoteVersion} is available (you have {result.LocalVersion})."
                    : $"La version {result.RemoteVersion} est disponible (vous avez {result.LocalVersion}).",
                TextWrapping = TextWrapping.Wrap
            });
            panel.Children.Add(new ScrollViewer
            {
                MaxHeight = 220,
                Content = new TextBlock
                {
                    Text = notes,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12,
                    Opacity = 0.9
                }
            });

            var dialog = new ContentDialog
            {
                Title = isEn ? "Update available" : "Mise à jour disponible",
                Content = panel,
                PrimaryButtonText = result.Asset != null
                    ? (isEn ? "Download and install" : "Télécharger et installer")
                    : (isEn ? "OK" : "OK"),
                CloseButtonText = isEn ? "Later" : "Plus tard",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.Content.XamlRoot
            };

            var choice = await dialog.ShowAsync();
            if (choice == ContentDialogResult.Primary && result.Asset != null)
            {
                await DownloadAndInstallAsync(result.Asset);
            }
        }

        private async Task DownloadAndInstallAsync(GitHubAsset asset)
        {
            bool isEn = _currentLang == "en";
            UpdateInfoBar.Severity = InfoBarSeverity.Informational;
            UpdateInfoBar.Title = isEn ? "Downloading…" : "Téléchargement…";
            UpdateInfoBar.Message = asset.Name;
            UpdateInfoBar.IsOpen = true;

            try
            {
                var progress = new Progress<double>(p =>
                {
                    UpdateInfoBar.Message = $"{asset.Name} — {(int)(p * 100)} %";
                });
                var path = await GitHubUpdateService.DownloadAssetAsync(asset, progress);

                UpdateInfoBar.Severity = InfoBarSeverity.Success;
                UpdateInfoBar.Title = isEn ? "Download complete" : "Téléchargement terminé";
                UpdateInfoBar.Message = isEn
                    ? "The installer will open. Close Skrib if the setup asks you to."
                    : "L’installateur va s’ouvrir. Fermez Skrib si l’installation le demande.";

                var started = Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });

                if (started == null)
                {
                    await Launcher.LaunchUriAsync(new Uri(path));
                }
            }
            catch (Exception ex)
            {
                UpdateInfoBar.Severity = InfoBarSeverity.Error;
                UpdateInfoBar.Title = isEn ? "Download failed" : "Téléchargement impossible";
                UpdateInfoBar.Message = ex.Message;
                UpdateInfoBar.IsOpen = true;
            }
        }

        #endregion

        #region Theme Management

        private void ApplySavedTheme()
        {
            var theme = LoadTheme();
            ApplyTheme(theme);
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

        private void ApplyTheme(ElementTheme theme)
        {
            if (this.Content is FrameworkElement root)
            {
                root.RequestedTheme = theme;
            }
            try { ApplicationData.Current.LocalSettings.Values["AppTheme"] = theme.ToString(); } catch { }
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
            ApplyLanguage(lang);
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

        private void ApplyLanguage(string lang)
        {
            _currentLang = (lang == "en") ? "en" : "fr";
            try { ApplicationData.Current.LocalSettings.Values["AppLanguage"] = _currentLang; } catch { }

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
                MenuCut.Text = "Cut";
                MenuCopy.Text = "Copy";
                MenuPaste.Text = "Paste";
                MenuSelectAll.Text = "Select All";

                ToolTipService.SetToolTip(SettingsButton, "Settings");
                ToolTipService.SetToolTip(BackButton, "Back");
                SettingsTitleText.Text = "Settings";

                SectionAppearanceTitle.Text = "Appearance";
                ThemeHeaderTitle.Text = "App theme";
                ThemeHeaderDesc.Text = "Select which app theme to display in Skrib";
                ThemeLightComboText.Text = "Light";
                ThemeDarkComboText.Text = "Dark";
                ThemeSystemComboText.Text = "Use system setting";

                LangHeaderTitle.Text = "Language";
                LangHeaderDesc.Text = "Choose application display language";
                LangFrComboText.Text = "French (FR)";
                LangEnComboText.Text = "English (EN)";

                SectionEditorTitle.Text = "Editor";
                WordWrapTitle.Text = "Word wrap";
                WordWrapDesc.Text = "Wrap long lines of text to fit the window width";

                SectionUpdateTitle.Text = "Updates";
                UpdateHeaderTitle.Text = "Updates";
                UpdateHeaderDesc.Text = "Check whether a newer version is available";
                CheckUpdateButtonText.Text = "Check";

                SectionAboutTitle.Text = "About";
                AboutTitle.Text = "Skrib";
                SetBetaNoticeText("Beta build: bugs may occur.");
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
                MenuCut.Text = "Couper";
                MenuCopy.Text = "Copier";
                MenuPaste.Text = "Coller";
                MenuSelectAll.Text = "Tout sélectionner";

                ToolTipService.SetToolTip(SettingsButton, "Paramètres");
                ToolTipService.SetToolTip(BackButton, "Retour");
                SettingsTitleText.Text = "Paramètres";

                SectionAppearanceTitle.Text = "Apparence";
                ThemeHeaderTitle.Text = "Thème de l'application";
                ThemeHeaderDesc.Text = "Choisissez le thème à afficher dans Skrib";
                ThemeLightComboText.Text = "Clair";
                ThemeDarkComboText.Text = "Sombre";
                ThemeSystemComboText.Text = "Utiliser le paramètre système";

                LangHeaderTitle.Text = "Langue";
                LangHeaderDesc.Text = "Choisissez la langue de l'application";
                LangFrComboText.Text = "Français (FR)";
                LangEnComboText.Text = "English (EN)";

                SectionEditorTitle.Text = "Éditeur";
                WordWrapTitle.Text = "Retour automatique à la ligne";
                WordWrapDesc.Text = "Ajuster le texte pour qu'il tienne dans la largeur de la fenêtre";

                SectionUpdateTitle.Text = "Mises à jour";
                UpdateHeaderTitle.Text = "Mises à jour";
                UpdateHeaderDesc.Text = "Vérifiez si une nouvelle version est disponible";
                CheckUpdateButtonText.Text = "Vérifier";

                SectionAboutTitle.Text = "À propos";
                AboutTitle.Text = "Skrib";
                SetBetaNoticeText("Version bêta : des bugs peuvent survenir.");
            }

            UpdateAboutVersion();
            UpdateTitle();
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
                }
            }
            catch { }
        }

        private void WordWrapToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            Editor.TextWrapping = WordWrapToggle.IsOn ? TextWrapping.Wrap : TextWrapping.NoWrap;
            try { ApplicationData.Current.LocalSettings.Values["WordWrap"] = WordWrapToggle.IsOn; } catch { }
        }

        private void UpdateTitle()
        {
            var untitled = _currentLang == "en" ? "Untitled" : "Sans titre";
            var ready = _currentLang == "en" ? "Ready" : "Prêt";
            var name = _currentFile != null ? _currentFile.Name : untitled;
            this.Title = $"Skrib - {name}{(_isDirty ? "*" : "")}";
            try
            {
                StatusText.Text = _currentFile != null ? _currentFile.Path : ready;
            }
            catch
            {
                StatusText.Text = _currentFile != null ? _currentFile.Name : ready;
            }
        }

        private bool _titleRefreshQueued;

        private void SetBetaNoticeText(string text)
        {
            if (RootGrid.FindName("BetaNoticeText") is TextBlock betaNoticeText)
            {
                betaNoticeText.Text = text;
            }
        }

        private void Editor_TextChanged(object sender, TextChangedEventArgs e)
        {
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
            });
        }

        private async void NewFile_Click(object sender, RoutedEventArgs e)
        {
            if (!await AskSaveIfNeededAsync()) return;
            Editor.Text = string.Empty;
            _currentFile = null;
            _isDirty = false;
            UpdateTitle();
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
                        Editor.Text = text;
                        _currentFile = file;
                        _isDirty = false;
                        UpdateTitle();
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
            await FileIO.WriteTextAsync(_currentFile, Editor.Text);
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
            var filterName = _currentLang == "en" ? "Text Document" : "Texte";
            picker.SuggestedFileName = _currentFile != null ? Path.GetFileNameWithoutExtension(_currentFile.Name) : untitled;
            picker.FileTypeChoices.Clear();
            picker.FileTypeChoices.Add(filterName, new System.Collections.Generic.List<string>() { ".txt" });
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

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            _ = ExitAsync();
        }

        private async Task ExitAsync()
        {
            if (!await AskSaveIfNeededAsync()) return;
            Application.Current.Exit();
        }

        private void Cut_Click(object sender, RoutedEventArgs e)
        {
            var sel = Editor.SelectedText;
            if (!string.IsNullOrEmpty(sel))
            {
                var dp = new DataPackage();
                dp.SetText(sel);
                Clipboard.SetContent(dp);
                var start = Editor.SelectionStart;
                Editor.Text = Editor.Text.Remove(start, Editor.SelectionLength);
                _isDirty = true;
                UpdateTitle();
            }
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            var sel = Editor.SelectedText;
            if (!string.IsNullOrEmpty(sel))
            {
                var dp = new DataPackage();
                dp.SetText(sel);
                Clipboard.SetContent(dp);
            }
        }

        private async void Paste_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var content = Clipboard.GetContent();
                if (content.Contains(StandardDataFormats.Text))
                {
                    var text = await content.GetTextAsync();
                    var start = Editor.SelectionStart;
                    Editor.Text = Editor.Text.Substring(0, start) + text + Editor.Text.Substring(start + Editor.SelectionLength);
                    _isDirty = true;
                    UpdateTitle();
                }
            }
            catch (Exception ex)
            {
                await ShowErrorAsync(_currentLang == "en" ? "Paste error" : "Erreur collage", ex.Message);
            }
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            Editor.SelectAll();
        }

        private async Task ShowErrorAsync(string title, string message)
        {
            var dlg = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "OK",
                XamlRoot = this.Content.XamlRoot
            };
            await dlg.ShowAsync();
        }

        public async Task OpenFileFromStorageFileAsync(StorageFile file)
        {
            try
            {
                var text = await FileIO.ReadTextAsync(file);
                Editor.Text = text;
                _currentFile = file;
                _isDirty = false;
                UpdateTitle();
            }
            catch (Exception ex)
            {
                await ShowErrorAsync(_currentLang == "en" ? "Unable to read file" : "Impossible de lire le fichier", ex.Message);
            }
        }

        #endregion
    }
}

