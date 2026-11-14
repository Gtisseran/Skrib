using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Windowing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.AccessCache;
using WinRT.Interop;
using Windows.System;
using Windows.ApplicationModel.DataTransfer;

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
        private bool _uiRefreshQueued;
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
        private bool _closeConfirmed;
        private bool _closeDialogOpen;

        public MainWindow()
        {
            this.InitializeComponent();
            ConfigureCustomTitleBar();
            InitializeContextMenu();
            SetMinimumWindowSize();
            SetWindowIcon();
            RegisterWindowClosing();
            RegisterFileDrop();
            ApplySavedTheme();
            ApplySavedBackdrop();
            ApplySavedLanguage();
            UpdateRecentFilesMenu();
            ApplySavedWordWrap();
            ApplySavedEditorFontSize();
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
        private PointerEventHandler? _editorWheelHandler;

        private void Editor_Loaded(object sender, RoutedEventArgs e)
        {
            // Loaded can fire again when the editor re-enters the visual tree: rebind the
            // wheel relay to the current inner ScrollViewer instead of stacking handlers,
            // which would scroll twice per wheel notch.
            var sv = FindDescendant<ScrollViewer>(Editor);
            if (sv is null) return;

            if (_editorScrollViewer != null)
            {
                _editorScrollViewer.ViewChanged -= EditorScrollViewer_ViewChanged;
            }

            if (_editorWheelHandler is null)
            {
                _editorWheelHandler = new PointerEventHandler(Editor_PointerWheelChanged);
            }
            Editor.RemoveHandler(UIElement.PointerWheelChangedEvent, _editorWheelHandler);
            Editor.AddHandler(UIElement.PointerWheelChangedEvent, _editorWheelHandler, true);

            _editorScrollViewer = sv;
            sv.ViewChanged += EditorScrollViewer_ViewChanged;
        }

        // Fraction of the raw wheel delta (±120 per notch) applied per tick,
        // so a single notch moves the editor less than the OS default.
        private const double EditorWheelScale = 0.5;

        private static bool IsShiftDown()
        {
            var state = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift);
            return (state & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
        }

        private void Editor_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            var props = e.GetCurrentPoint(Editor).Properties;
            // Ctrl+wheel zooms the editor text instead of scrolling.
            if (IsControlDown())
            {
                ZoomEditorFont(Editor.FontSize + (props.MouseWheelDelta > 0 ? 1 : -1));
                e.Handled = true;
                return;
            }

            var sv = _editorScrollViewer;
            if (sv is null) return;
            double delta = props.MouseWheelDelta * EditorWheelScale;

            if (IsShiftDown())
            {
                // Shift+wheel: horizontal, forward scrolls left (Windows convention).
                if (sv.ScrollableWidth <= 0) return;
                if (!_smoothHorizontalAnimating) _smoothHorizontalTarget = sv.HorizontalOffset;
                _smoothHorizontalTarget = Math.Clamp(_smoothHorizontalTarget - delta, 0, sv.ScrollableWidth);
                _smoothHorizontalAnimating = true;
                sv.ChangeView(_smoothHorizontalTarget, null, null, false);
            }
            else
            {
                if (sv.ScrollableHeight <= 0) return;
                if (!_smoothVerticalAnimating) _smoothVerticalTarget = sv.VerticalOffset;
                _smoothVerticalTarget = Math.Clamp(_smoothVerticalTarget - delta, 0, sv.ScrollableHeight);
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

        // Intercepts the window close (title bar X, Alt+F4, taskbar close) so unsaved
        // changes get the same save prompt as the File menu Exit/New/Open paths.
        private void RegisterWindowClosing()
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            if (appWindow != null)
            {
                appWindow.Closing += AppWindow_Closing;
            }
        }

        // The Closing event is not awaitable: cancel it synchronously, run the save
        // prompt, then close for real once confirmed. The flags keep a second close
        // event (fires again when Close() runs, or when the user closes during the
        // prompt) from opening a second dialog or cancelling the confirmed close.
        private async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            if (_closeConfirmed || !_isDirty) return;

            args.Cancel = true;
            if (_closeDialogOpen) return;
            _closeDialogOpen = true;
            bool proceed;
            try
            {
                proceed = await AskSaveIfNeededAsync();
            }
            catch (Exception ex)
            {
                // A failed save must never close the window with changes lost.
                System.Diagnostics.Debug.Write($"Error saving on window close: {ex.Message}");
                proceed = false;
            }
            finally
            {
                _closeDialogOpen = false;
            }

            if (!proceed) return;
            _closeConfirmed = true;
            Close();
        }

        private void UpdateAboutVersion()
        {
            AboutDesc.Text = S("about.version") + " " + GetVersionString();
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
            if (e.Key == VirtualKey.Escape && SearchBar.Visibility == Visibility.Visible)
            {
                CloseSearch();
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
                // The 8 px step keeps a drag-resize from relaying out on every pixel.
                aboutWidth = Math.Clamp(Math.Round(width * 0.30 / 8) * 8, 280, 340);
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
            // Same 8 px step: fewer real width changes during a drag-resize.
            double themeWidth = Math.Clamp(Math.Round(mainWidth * 0.38 / 8) * 8, 220, 280);
            double comboWidth = Math.Clamp(Math.Round(mainWidth * 0.32 / 8) * 8, 200, 240);

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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.Write($"Error loading theme: {ex.Message}");
            }
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
                    if (!Enum.TryParse(tag, out ElementTheme theme))
                    {
                        System.Diagnostics.Debug.Write($"Unknown theme tag: {tag}");
                        return;
                    }
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.Write($"Error loading backdrop: {ex.Message}");
            }
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

        // Returns the localized string matching the active language, from the UiStrings
        // table (stable id + one translation per supported language).
        private string S(string id)
        {
            return UiStrings.Get(_currentLang, id);
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

            MenuFile.Title = S("menu.file");
            MenuNew.Text = S("menu.new");
            MenuOpen.Text = S("menu.open");
            MenuRecent.Text = S("recent.title");
            MenuSave.Text = S("menu.save");
            MenuSaveAs.Text = S("menu.saveAs");
            MenuExit.Text = S("menu.exit");

            MenuEdit.Title = S("menu.edit");
            MenuUndo.Text = S("menu.undo");
            MenuRedo.Text = S("menu.redo");
            MenuCut.Text = S("menu.cut");
            MenuCopy.Text = S("menu.copy");
            MenuPaste.Text = S("menu.paste");
            MenuSelectAll.Text = S("menu.selectAll");
            MenuView.Title = S("menu.view");
            MenuZoomIn.Text = S("menu.zoomIn");
            MenuZoomOut.Text = S("menu.zoomOut");
            MenuZoomReset.Text = S("menu.zoomReset");
            MenuPreview.Text = S("menu.preview");
            ToolTipService.SetToolTip(PreviewToggleButton, S("menu.preview"));

            if (_contextUndo != null) _contextUndo.Text = S("menu.undo");
            if (_contextRedo != null) _contextRedo.Text = S("menu.redo");
            if (_contextCut != null) _contextCut.Text = S("menu.cut");
            if (_contextCopy != null) _contextCopy.Text = S("menu.copy");
            if (_contextPaste != null) _contextPaste.Text = S("menu.paste");
            if (_contextSelectAll != null) _contextSelectAll.Text = S("menu.selectAll");

            MenuSearch.Text = S("menu.search");
            MenuReplace.Text = S("menu.replace");
            ReplaceButton.Content = S("search.replaceButton");
            ReplaceAllButton.Content = S("search.replaceAll");
            SearchTextBox.PlaceholderText = S("search.findPlaceholder");
            ReplaceTextBox.PlaceholderText = S("search.replacePlaceholder");
            ToolTipService.SetToolTip(SearchNextButton, S("search.next"));
            ToolTipService.SetToolTip(SearchPreviousButton, S("search.previous"));
            ToolTipService.SetToolTip(SearchCloseButton, S("search.close"));

            ToolTipService.SetToolTip(SettingsButton, S("settings.title"));
            ToolTipService.SetToolTip(BackButton, S("settings.back"));
            SettingsTitleText.Text = S("settings.title");

            SectionAppearanceTitle.Text = S("settings.section.appearance");
            ThemeHeaderTitle.Text = S("settings.theme.title");
            ThemeHeaderDesc.Text = S("settings.theme.desc");
            ThemeLightComboText.Text = S("settings.theme.light");
            ThemeDarkComboText.Text = S("settings.theme.dark");
            ThemeSystemComboText.Text = S("settings.theme.system");
            BackdropHeaderTitle.Text = S("settings.backdrop.title");
            BackdropHeaderDesc.Text = S("settings.backdrop.desc");
            BackdropMicaComboText.Text = "Mica";
            BackdropAcrylicComboText.Text = S("settings.backdrop.acrylic");

            LangHeaderTitle.Text = S("settings.language.title");
            LangHeaderDesc.Text = S("settings.language.desc");
            LangFrComboText.Text = S("settings.lang.fr");
            LangEsComboText.Text = S("settings.lang.es");
            LangSystemComboText.Text = S("settings.lang.system");

            SectionEditorTitle.Text = S("settings.section.editor");
            WordWrapTitle.Text = S("settings.wordWrap.title");
            WordWrapDesc.Text = S("settings.wordWrap.desc");
            EditorBgTitle.Text = S("settings.editorBg.title");
            EditorBgDesc.Text = S("settings.editorBg.desc");

            // The editor welcome message is only visible while the document is empty.
            Editor.PlaceholderText = S("editor.placeholder");

            SectionAboutTitle.Text = S("settings.section.about");
            AboutTitle.Text = "Skrib";
            GitHubButtonText.Text = "GitHub";
            RateButtonText.Text = S("about.rate");

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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.Write($"Error loading word wrap setting: {ex.Message}");
            }
        }

        private void WordWrapToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            Editor.TextWrapping = WordWrapToggle.IsOn ? TextWrapping.Wrap : TextWrapping.NoWrap;
            try { ApplicationData.Current.LocalSettings.Values["WordWrap"] = WordWrapToggle.IsOn; }
            catch (Exception ex) { System.Diagnostics.Debug.Write($"Error saving word wrap setting: {ex.Message}"); }
            UpdateWordWrapState();
        }

        // Keeps the toggle state label in sync (fixed-width label so the toggle never shifts).
        private void UpdateWordWrapState()
        {
            bool isOn = WordWrapToggle != null && WordWrapToggle.IsOn;
            WordWrapStateText.Text = isOn ? S("common.on") : S("common.off");
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.Write($"Error loading editor background setting: {ex.Message}");
            }

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
                try { ApplicationData.Current.LocalSettings.Values["EditorTransparent"] = transparent; }
                catch (Exception ex) { System.Diagnostics.Debug.Write($"Error saving editor background setting: {ex.Message}"); }
            }
            // Force ThemeResource re-resolution so the overridden brushes apply immediately.
            // WinUI has no public API to invalidate theme-resource references on an element,
            // so flipping RequestedTheme and restoring it is the established workaround.
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.Write($"Error resolving editor theme: {ex.Message}");
            }
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.Write($"Error overriding editor theme brush: {ex.Message}");
            }
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.Write($"Error restoring editor theme brush: {ex.Message}");
            }
        }

        // Keeps the toggle state label in sync (fixed-width label so the toggle never shifts).
        private void UpdateEditorBgState()
        {
            bool isOn = EditorBgToggle != null && EditorBgToggle.IsOn;
            EditorBgStateText.Text = isOn ? S("common.on") : S("common.off");
        }

        #region Font Zoom

        // Editor font size zoom bounds; the default matches the XAML FontSize of the TextBox.
        private const double EditorFontSizeDefault = 14;
        private const double EditorFontSizeMin = 10;
        private const double EditorFontSizeMax = 48;

        private void ApplySavedEditorFontSize()
        {
            try
            {
                if (ApplicationData.Current.LocalSettings.Values["EditorFontSize"] is double size)
                {
                    Editor.FontSize = Math.Clamp(size, EditorFontSizeMin, EditorFontSizeMax);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.Write($"Error loading editor font size: {ex.Message}");
            }
        }

        // Zooms the editor text and persists it. The Markdown preview keeps its fixed
        // sizes on purpose: this is an editor-only feature.
        private void ZoomEditorFont(double newSize)
        {
            Editor.FontSize = Math.Clamp(newSize, EditorFontSizeMin, EditorFontSizeMax);
            try { ApplicationData.Current.LocalSettings.Values["EditorFontSize"] = Editor.FontSize; }
            catch (Exception ex) { System.Diagnostics.Debug.Write($"Error saving editor font size: {ex.Message}"); }
        }

        private void ZoomIn_Click(object sender, RoutedEventArgs e)
        {
            ZoomEditorFont(Editor.FontSize + 1);
        }

        private void ZoomOut_Click(object sender, RoutedEventArgs e)
        {
            ZoomEditorFont(Editor.FontSize - 1);
        }

        private void ZoomReset_Click(object sender, RoutedEventArgs e)
        {
            ZoomEditorFont(EditorFontSizeDefault);
        }

        #endregion

        #region Search and Replace

        // 0-based position of the current match among all matches (for the "n of m" counter).
        private int _searchMatchIndex;

        // Shows the bar; withReplace also reveals the replace row.
        private void ShowSearch(bool withReplace)
        {
            SearchBar.Visibility = Visibility.Visible;
            ReplaceRow.Visibility = withReplace ? Visibility.Visible : Visibility.Collapsed;
            SearchTextBox.Focus(FocusState.Programmatic);
            SearchTextBox.SelectAll();
            UpdateSearchCount();
        }

        private void CloseSearch()
        {
            SearchBar.Visibility = Visibility.Collapsed;
            Editor.Focus(FocusState.Programmatic);
        }

        private List<int> CurrentMatches()
        {
            return SearchHelper.MatchStarts(Editor.Text, SearchTextBox.Text);
        }

        // Selects the match at matchIndex and refreshes the counter. With an empty
        // result list it only refreshes the counter (empty state).
        private void SelectMatchAt(List<int> starts, int matchIndex)
        {
            if (starts.Count == 0)
            {
                _searchMatchIndex = 0;
                UpdateSearchCount();
                return;
            }
            matchIndex = Math.Clamp(matchIndex, 0, starts.Count - 1);
            _searchMatchIndex = matchIndex;
            Editor.Select(starts[matchIndex], SearchTextBox.Text.Length);
            UpdateSearchCount();
        }

        private void FindNextMatch()
        {
            var starts = CurrentMatches();
            int pos = Editor.SelectionStart + Editor.SelectionLength;
            int index = -1;
            for (int i = 0; i < starts.Count; i++)
            {
                if (starts[i] >= pos) { index = i; break; }
            }
            // Wrap around to the first match when nothing follows the caret.
            if (index < 0) index = 0;
            SelectMatchAt(starts, index);
        }

        private void FindPreviousMatch()
        {
            var starts = CurrentMatches();
            int index = -1;
            for (int i = starts.Count - 1; i >= 0; i--)
            {
                if (starts[i] < Editor.SelectionStart) { index = i; break; }
            }
            // Wrap around to the last match when nothing precedes the caret.
            if (index < 0) index = starts.Count - 1;
            SelectMatchAt(starts, index);
        }

        private void UpdateSearchCount()
        {
            string query = SearchTextBox.Text;
            if (query.Length == 0)
            {
                SearchCountText.Text = string.Empty;
                return;
            }
            int total = SearchHelper.CountMatches(Editor.Text, query);
            SearchCountText.Text = total == 0
                ? S("search.noMatches")
                : string.Format(S("search.matches"), _searchMatchIndex + 1, total);
        }

        private void SearchTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
            {
                if (IsShiftDown()) FindPreviousMatch(); else FindNextMatch();
                e.Handled = true;
            }
            else if (e.Key == VirtualKey.Escape)
            {
                CloseSearch();
                e.Handled = true;
            }
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // A new query restarts the search from the first match.
            SelectMatchAt(CurrentMatches(), 0);
        }

        private void ReplaceTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
            {
                ReplaceCurrentMatch();
                e.Handled = true;
            }
            else if (e.Key == VirtualKey.Escape)
            {
                CloseSearch();
                e.Handled = true;
            }
        }

        private void MenuSearch_Click(object sender, RoutedEventArgs e)
        {
            ShowSearch(false);
        }

        private void MenuReplace_Click(object sender, RoutedEventArgs e)
        {
            ShowSearch(true);
        }

        private void SearchNext_Click(object sender, RoutedEventArgs e)
        {
            FindNextMatch();
        }

        private void SearchPrevious_Click(object sender, RoutedEventArgs e)
        {
            FindPreviousMatch();
        }

        private void SearchClose_Click(object sender, RoutedEventArgs e)
        {
            CloseSearch();
        }

        private void ReplaceOne_Click(object sender, RoutedEventArgs e)
        {
            ReplaceCurrentMatch();
        }

        private void ReplaceCurrentMatch()
        {
            string query = SearchTextBox.Text;
            if (query.Length == 0) return;

            // If the current selection is a match, replace it before jumping to the next one.
            if (Editor.SelectionLength == query.Length &&
                string.Equals(Editor.SelectedText, query, StringComparison.OrdinalIgnoreCase))
            {
                Editor.SelectedText = ReplaceTextBox.Text;
                _isDirty = true;
            }
            FindNextMatch();
        }

        private void ReplaceAll_Click(object sender, RoutedEventArgs e)
        {
            string query = SearchTextBox.Text;
            if (query.Length == 0) return;
            string updated = SearchHelper.ReplaceAll(Editor.Text, query, ReplaceTextBox.Text);
            if (updated == Editor.Text) return;

            _suppressDirty = true;
            try { Editor.Text = updated; }
            finally { _suppressDirty = false; }
            _isDirty = true;
            QueueUiRefresh();
            UpdateSearchCount();
        }

        #endregion

        #endregion

        private void UpdateTitle()
        {
            var untitled = S("doc.untitled");
            var ready = S("status.ready");
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.Write($"Error reading file path: {ex.Message}");
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

        // One coalesced dispatcher callback for the title / status-bar refresh. The caret
        // info scans the text from the start, and SelectionChanged fires per keystroke and
        // per drag-select movement, so batching keeps large documents responsive.
        private void QueueUiRefresh()
        {
            if (_uiRefreshQueued)
            {
                return;
            }
            _uiRefreshQueued = true;
            _ = DispatcherQueue.TryEnqueue(() =>
            {
                _uiRefreshQueued = false;
                UpdateTitle();
                UpdateCaretInfo();
            });
        }

        private void Editor_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressDirty)
            {
                return;
            }
            _isDirty = true;
            QueueUiRefresh();
            if (SearchBar.Visibility == Visibility.Visible)
            {
                UpdateSearchCount();
            }
        }

        private void Editor_SelectionChanged(object sender, RoutedEventArgs e)
        {
            QueueUiRefresh();
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
            string countLabel = S("status.chars");
            if (_showMarkdownPreview)
                CaretInfoText.Text = S("status.preview") + text.Length + " " + countLabel;
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
                try { InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this)); }
catch (Exception ex) { System.Diagnostics.Debug.Write($"Error initializing file picker: {ex.Message}"); }
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
                        AddToRecentFiles(file);
                        SetEditorText(text);
                    }
                    catch (Exception ex)
                    {
                        await ShowErrorAsync(S("error.readFile"), ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                await ShowErrorAsync(S("error.open"), ex.Message);
            }
        }

        private async void SaveFile_Click(object sender, RoutedEventArgs e)
        {
            try { await SaveAsync(); } catch (Exception ex) { await ShowErrorAsync(S("error.save"), ex.Message); }
        }

        private async void SaveAsFile_Click(object sender, RoutedEventArgs e)
        {
            try { await SaveAsAsync(); } catch (Exception ex) { await ShowErrorAsync(S("error.save"), ex.Message); }
        }

        // HResult for E_ACCESSDENIED (0x80070005): the save target is locked or access denied.
        private const int FileAccessDenied = unchecked((int)0x80070005);

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
            catch (Exception ex) when (ex.HResult == FileAccessDenied)
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
            try { InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this)); }
catch (Exception ex) { System.Diagnostics.Debug.Write($"Error initializing file picker: {ex.Message}"); }
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.DefaultFileExtension = ".txt";
            var untitled = S("doc.untitled");
            var txtLabel = S("saveAs.txtType");
            var mdLabel = S("saveAs.mdType");
            picker.SuggestedFileName = _currentFile != null ? Path.GetFileNameWithoutExtension(_currentFile.Name) : untitled;
            picker.FileTypeChoices.Clear();
            picker.FileTypeChoices.Add(txtLabel, new System.Collections.Generic.List<string>() { ".txt" });
            picker.FileTypeChoices.Add(mdLabel, new System.Collections.Generic.List<string>() { ".md" });
            var file = await picker.PickSaveFileAsync();
            if (file == null) return false;
            await FileIO.WriteTextAsync(file, Editor.Text);
            _currentFile = file;
            AddToRecentFiles(file);
            _isDirty = false;
            UpdateTitle();
            return true;
        }

        private async Task<bool> AskSaveIfNeededAsync()
        {
            if (!_isDirty) return true;
            var dialog = new ContentDialog
            {
                Title = S("dialog.saveChanges.title"),
                Content = S("dialog.saveChanges.body"),
                PrimaryButtonText = S("menu.save"),
                SecondaryButtonText = S("dialog.dontSave"),
                CloseButtonText = S("dialog.cancel"),
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.Write($"Error opening GitHub page: {ex.Message}");
            }
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.Write($"Error opening Store review page: {ex.Message}");
            }
        }

        private static string GetVersionString()
        {
            try
            {
                var v = Windows.ApplicationModel.Package.Current.Id.Version;
                return v.Major + "." + v.Minor + "." + v.Build + "." + v.Revision;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.Write($"Error reading package version: {ex.Message}");
            }
            try
            {
                var asm = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                if (asm != null) return asm.Major + "." + asm.Minor + "." + asm.Build + "." + asm.Revision;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.Write($"Error reading assembly version: {ex.Message}");
            }
            return "1.0.0.0";
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            _ = ExitAsync();
        }

        private async Task ExitAsync()
        {
            if (!await AskSaveIfNeededAsync()) return;
            // The user already answered the save prompt: don't let the exit re-run it.
            _closeConfirmed = true;
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.Write($"Error checking markdown file type: {ex.Message}");
                return false;
            }
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.Write($"Error toggling markdown preview: {ex.Message}");
            }
            UpdateCaretInfo();
        }

        // Threshold above which a synchronous preview render is logged (ms). The render
        // runs on the UI thread on purpose — instant for note-sized documents — but a
        // very large document must stay visible instead of silently freezing the app.
        private const int SlowPreviewRenderMs = 200;

        // Builds the native preview content from the current document. Synchronous on
        // purpose: parsing and building paragraphs is instant for note-sized documents,
        // and there is no browser process to keep alive anymore.
        private void RenderMarkdownPreview()
        {
            try
            {
                bool light = (this.Content as FrameworkElement)?.ActualTheme != ElementTheme.Dark;
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var blocks = MarkdownPreviewModel.Parse(Editor.Text ?? string.Empty);
                MarkdownPreviewBuilder.Fill(MarkdownPreview, blocks, light);
                clock.Stop();
                if (clock.ElapsedMilliseconds > SlowPreviewRenderMs)
                {
                    System.Diagnostics.Debug.Write(
                        $"Markdown preview slow: {clock.ElapsedMilliseconds} ms for {Editor.Text?.Length ?? 0} chars");
                }
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

        #region Recent Files

        // Files opened in earlier sessions, reachable after relaunch. The FutureAccessList
        // token is what grants access to a file picked before; the path is display-only.
        private void AddToRecentFiles(StorageFile file)
        {
            try
            {
                var entries = LoadRecentFiles();
                string token = StorageApplicationPermissions.FutureAccessList.Add(file);
                entries = RecentFilesList.Push(entries, token, file.Path, out var evicted);
                FreeRecentFileTokens(evicted);
                ApplicationData.Current.LocalSettings.Values["RecentFiles"] = RecentFilesList.ToJson(entries);
                UpdateRecentFilesMenu(entries);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.Write($"Error updating recent files: {ex.Message}");
            }
        }

        private void RemoveRecentFile(string token)
        {
            try
            {
                var entries = RecentFilesList.RemoveByToken(LoadRecentFiles(), token);
                try { StorageApplicationPermissions.FutureAccessList.Remove(token); }
                catch (Exception ex) { System.Diagnostics.Debug.Write($"Error freeing recent file token: {ex.Message}"); }
                ApplicationData.Current.LocalSettings.Values["RecentFiles"] = RecentFilesList.ToJson(entries);
                UpdateRecentFilesMenu(entries);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.Write($"Error removing recent file: {ex.Message}");
            }
        }

        private void ClearRecentFiles_Click(object sender, RoutedEventArgs e)
        {
            var entries = LoadRecentFiles();
            FreeRecentFileTokens(entries);
            ApplicationData.Current.LocalSettings.Values["RecentFiles"] = RecentFilesList.ToJson(new List<RecentFilesList.Entry>());
            UpdateRecentFilesMenu(new List<RecentFilesList.Entry>());
        }

        private static List<RecentFilesList.Entry> LoadRecentFiles()
        {
            try
            {
                return RecentFilesList.Parse(ApplicationData.Current.LocalSettings.Values["RecentFiles"] as string);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.Write($"Error loading recent files: {ex.Message}");
                return new List<RecentFilesList.Entry>();
            }
        }

        private static void FreeRecentFileTokens(List<RecentFilesList.Entry> entries)
        {
            foreach (var entry in entries)
            {
                try { StorageApplicationPermissions.FutureAccessList.Remove(entry.Token); }
                catch (Exception ex) { System.Diagnostics.Debug.Write($"Error freeing recent file token: {ex.Message}"); }
            }
        }

        // Rebuilds the "Recent files" submenu (hidden while the list is empty).
        private void UpdateRecentFilesMenu()
        {
            UpdateRecentFilesMenu(LoadRecentFiles());
        }

        private void UpdateRecentFilesMenu(List<RecentFilesList.Entry> entries)
        {
            MenuRecent.Items.Clear();
            foreach (var entry in entries)
            {
                var item = new MenuFlyoutItem { Text = Path.GetFileName(entry.Path), Tag = entry.Token };
                ToolTipService.SetToolTip(item, entry.Path);
                item.Click += RecentFile_Click;
                MenuRecent.Items.Add(item);
            }
            if (entries.Count > 0)
            {
                MenuRecent.Items.Add(new MenuFlyoutSeparator());
                var clear = new MenuFlyoutItem { Text = S("recent.clear") };
                clear.Click += ClearRecentFiles_Click;
                MenuRecent.Items.Add(clear);
            }
            MenuRecent.Visibility = entries.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void RecentFile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem item || item.Tag is not string token) return;
            StorageFile? file;
            try
            {
                file = await StorageApplicationPermissions.FutureAccessList.GetFileAsync(token);
            }
            catch (Exception ex)
            {
                // A moved or deleted file invalidates its entry: drop it from the menu.
                System.Diagnostics.Debug.Write($"Error opening recent file: {ex.Message}");
                RemoveRecentFile(token);
                return;
            }

            if (!await AskSaveIfNeededAsync()) return;
            await OpenFileFromStorageFileAsync(file);
        }

        #endregion

        #region File Activation

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
                AddToRecentFiles(file);
                SetEditorText(text);
            }
            catch (Exception ex)
            {
                await ShowErrorAsync(S("error.readFile"), ex.Message);
            }
        }

        // Accepts files dragged from Explorer (and other shell sources) onto the window.
        private void RegisterFileDrop()
        {
            RootGrid.DragOver += RootGrid_DragOver;
            RootGrid.Drop += RootGrid_Drop;
        }

        private void RootGrid_DragOver(object sender, DragEventArgs e)
        {
            bool accepted = false;
            try
            {
                accepted = e.DataView.Contains(StandardDataFormats.StorageItems);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.Write($"Error inspecting drag payload: {ex.Message}");
            }
            e.AcceptedOperation = accepted ? DataPackageOperation.Copy : DataPackageOperation.None;
            if (accepted)
            {
                e.Handled = true;
            }
        }

        private async void RootGrid_Drop(object sender, DragEventArgs e)
        {
            try
            {
                if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
                var items = await e.DataView.GetStorageItemsAsync();

                StorageFile? file = null;
                foreach (var item in items)
                {
                    if (item is StorageFile candidate && IsSupportedTextFile(candidate))
                    {
                        file = candidate;
                        break;
                    }
                }
                if (file == null) return;

                if (!await AskSaveIfNeededAsync()) return;
                await OpenFileFromStorageFileAsync(file);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.Write($"Error opening dropped file: {ex.Message}");
            }
        }

        // Only the text documents the editor can safely open: .txt and .md.
        private static bool IsSupportedTextFile(StorageFile file)
        {
            return string.Equals(file.FileType, ".txt", StringComparison.OrdinalIgnoreCase)
                || string.Equals(file.FileType, ".md", StringComparison.OrdinalIgnoreCase);
        }

        #endregion
    }
}
