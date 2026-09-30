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
                var desc = _currentLang == "en"
                    ? $"Version {versionStr} • Modern Windows 11 text editor"
                    : $"Version {versionStr} • Éditeur de texte moderne Windows 11";
                AboutDesc.Text = desc;
            }
            catch
            {
                AboutDesc.Text = _currentLang == "en"
                    ? "Version 1.0.4 • Modern Windows 11 text editor"
                    : "Version 1.0.4 • Éditeur de texte moderne Windows 11";
            }
        }

        #region Navigation Page Paramètres

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

        #region Mises à jour GitHub

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
                        UpdateInfoBar.Severity = InfoBarSeverity.Success;
                        UpdateInfoBar.Title = isEn ? "You're up to date" : "Vous êtes à jour";
                        UpdateInfoBar.Message = isEn
                            ? $"Skrib {result.LocalVersion} is the latest release."
                            : $"Skrib {result.LocalVersion} est la dernière version.";
                        UpdateInfoBar.IsOpen = true;
                        break;

                    case UpdateCheckStatus.UpdateAvailable:
                        await ShowUpdateAvailableAsync(result);
                        break;

                    case UpdateCheckStatus.NoRelease:
                        UpdateInfoBar.Severity = InfoBarSeverity.Warning;
                        UpdateInfoBar.Title = isEn ? "No release found" : "Aucune release trouvée";
                        UpdateInfoBar.Message = isEn
                            ? $"No GitHub release on {UpdateConfig.GitHubOwner}/{UpdateConfig.GitHubRepo}. Open the tutorial to publish one."
                            : $"Aucune release GitHub sur {UpdateConfig.GitHubOwner}/{UpdateConfig.GitHubRepo}. Ouvrez le tutoriel pour en publier une.";
                        UpdateInfoBar.IsOpen = true;
                        break;

                    default:
                        UpdateInfoBar.Severity = InfoBarSeverity.Error;
                        UpdateInfoBar.Title = isEn ? "Update check failed" : "Vérification impossible";
                        UpdateInfoBar.Message = string.IsNullOrWhiteSpace(result.Message)
                            ? (isEn ? "Check your connection and the GitHub repository name." : "Vérifiez la connexion et le nom du dépôt GitHub.")
                            : result.Message;
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
                    : (isEn ? "Open GitHub" : "Ouvrir GitHub"),
                CloseButtonText = isEn ? "Later" : "Plus tard",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.Content.XamlRoot
            };

            if (result.Asset != null)
            {
                dialog.SecondaryButtonText = isEn ? "Open GitHub" : "Ouvrir GitHub";
            }

            var choice = await dialog.ShowAsync();
            if (choice == ContentDialogResult.Primary)
            {
                if (result.Asset != null)
                {
                    await DownloadAndInstallAsync(result.Asset);
                }
                else
                {
                    await OpenReleasePageAsync(result);
                }
            }
            else if (choice == ContentDialogResult.Secondary)
            {
                await OpenReleasePageAsync(result);
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

        private static async Task OpenReleasePageAsync(UpdateCheckResult result)
        {
            var url = result.Release?.HtmlUrl;
            if (string.IsNullOrWhiteSpace(url))
            {
                url = UpdateConfig.ReleasesPageUrl;
            }
            await Launcher.LaunchUriAsync(new Uri(url));
        }

        private async void GitHubGuideLink_Click(object sender, RoutedEventArgs e)
        {
            bool isEn = _currentLang == "en";
            var body = isEn
                ? "1. Install Git for Windows (git-scm.com).\n" +
                  "2. Create a GitHub repository (public recommended).\n" +
                  "3. In UpdateConfig.cs, set GitHubOwner and GitHubRepo.\n" +
                  "4. git init, commit, then git push origin main.\n" +
                  "5. Bump Identity Version in Package.appxmanifest.\n" +
                  "6. Create a GitHub Release tagged like v1.0.6 and attach the .msix or .zip.\n" +
                  "7. Use Check in Settings — Skrib reads /releases/latest.\n\n" +
                  "Full steps: TUTO-GITHUB.md in the project folder."
                : "1. Installez Git pour Windows (git-scm.com).\n" +
                  "2. Créez un dépôt GitHub (public de préférence).\n" +
                  "3. Dans UpdateConfig.cs, renseignez GitHubOwner et GitHubRepo.\n" +
                  "4. git init, commit, puis git push origin main.\n" +
                  "5. Augmentez Identity Version dans Package.appxmanifest.\n" +
                  "6. Créez une Release GitHub (tag v1.0.6) et joignez le .msix ou .zip.\n" +
                  "7. Utilisez Vérifier dans les paramètres — Skrib lit /releases/latest.\n\n" +
                  "Détail : fichier TUTO-GITHUB.md à la racine du projet.";

            var dialog = new ContentDialog
            {
                Title = isEn ? "Git and GitHub tutorial" : "Tutoriel Git et GitHub",
                Content = new ScrollViewer
                {
                    MaxHeight = 360,
                    Content = new TextBlock
                    {
                        Text = body,
                        TextWrapping = TextWrapping.Wrap
                    }
                },
                PrimaryButtonText = isEn ? "Open GitHub" : "Ouvrir GitHub",
                SecondaryButtonText = isEn ? "Download Git" : "Télécharger Git",
                CloseButtonText = isEn ? "Close" : "Fermer",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.Content.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                await Launcher.LaunchUriAsync(new Uri("https://github.com/new"));
            }
            else if (result == ContentDialogResult.Secondary)
            {
                await Launcher.LaunchUriAsync(new Uri("https://git-scm.com/download/win"));
            }
        }

        #endregion

        #region Gestion du Thème

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

        #region Gestion de la Langue

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
                UpdateHeaderTitle.Text = "GitHub updates";
                UpdateHeaderDesc.Text = "Check whether a newer version is available";
                CheckUpdateButtonText.Text = "Check";
                GitHubGuideLink.Content = "Git and GitHub tutorial";

                SectionAboutTitle.Text = "About";
                AboutTitle.Text = "Skrib";
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
                UpdateHeaderTitle.Text = "Mises à jour GitHub";
                UpdateHeaderDesc.Text = "Vérifiez si une nouvelle version est disponible";
                CheckUpdateButtonText.Text = "Vérifier";
                GitHubGuideLink.Content = "Tutoriel Git et GitHub";

                SectionAboutTitle.Text = "À propos";
                AboutTitle.Text = "Skrib";
            }

            UpdateAboutVersion();
            UpdateTitle();
        }

        #endregion

        #region Éditeur et Word Wrap

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

        private void Editor_TextChanged(object sender, TextChangedEventArgs e)
        {
            _isDirty = true;
            UpdateTitle();
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

