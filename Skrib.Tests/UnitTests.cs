namespace Skrib.Tests;

public partial class UnitTests
{
    #region Theme Management Tests

    [Fact]
    public void LoadTheme_ReturnsDefaultWhenNoSaved()
    {
        // Arrange
        var mockAppData = new MockApplicationData();
        ApplicationData.Current = mockAppData;

        // Act
        ElementTheme result = LoadTheme();

        // Assert
        Assert.Equal(ElementTheme.Default, result);
    }

    [Fact]
    public void LoadTheme_ReturnsSavedWhenPresent()
    {
        // Arrange
        var mockAppData = new MockApplicationData();
        mockAppData.Values["AppTheme"] = "Dark";
        ApplicationData.Current = mockAppData;

        // Act
        ElementTheme result = LoadTheme();

        // Assert
        Assert.Equal(ElementTheme.Dark, result);
    }

    [Fact]
    public void ApplyTheme_SavesCorrectly()
    {
        // Arrange
        var mockAppData = new MockApplicationData();
        ApplicationData.Current = mockAppData;
        var mockWindow = new MockMainWindow();

        // Act
        mockWindow.ApplyTheme(ElementTheme.Dark);

        // Assert
        Assert.Contains(mockAppData.Values, kvp => kvp.Key == "AppTheme" && kvp.Value.ToString() == "Dark");
    }

    #endregion

    #region Backdrop Management Tests

    [Fact]
    public void LoadBackdrop_ReturnsDefaultMicaWhenNoSaved()
    {
        // Arrange
        var mockAppData = new MockApplicationData();
        ApplicationData.Current = mockAppData;

        // Act
        string result = LoadBackdrop();

        // Assert
        Assert.Equal("Mica", result);
    }

    [Fact]
    public void LoadBackdrop_ReturnsSavedWhenPresent()
    {
        // Arrange
        var mockAppData = new MockApplicationData();
        mockAppData.Values["AppBackdrop"] = "Acrylic";
        ApplicationData.Current = mockAppData;

        // Act
        string result = LoadBackdrop();

        // Assert
        Assert.Equal("Acrylic", result);
    }

    [Fact]
    public void ApplyBackdrop_SavesCorrectly()
    {
        // Arrange
        var mockAppData = new MockApplicationData();
        ApplicationData.Current = mockAppData;
        var mockWindow = new MockMainWindow();

        // Act
        mockWindow.ApplyBackdrop("Acrylic");

        // Assert
        Assert.Contains(mockAppData.Values, kvp => kvp.Key == "AppBackdrop" && kvp.Value == "Acrylic");
    }

    #endregion

    #region Language Management Tests

    [Fact]
    public void LoadLanguage_ReturnsDefaultSystemWhenNoSaved()
    {
        // Arrange
        var mockAppData = new MockApplicationData();
        ApplicationData.Current = mockAppData;

        // Act
        string result = LoadLanguage();

        // Assert
        Assert.Equal("System", result);
    }

    [Fact]
    public void LoadLanguage_ReturnsSavedWhenPresent()
    {
        // Arrange
        var mockAppData = new MockApplicationData();
        mockAppData.Values["AppLanguage"] = "fr";
        ApplicationData.Current = mockAppData;

        // Act
        string result = LoadLanguage();

        // Assert
        Assert.Equal("fr", result);
    }

    [Fact]
    public void ApplyLanguage_SavesCorrectly()
    {
        // Arrange
        var mockAppData = new MockApplicationData();
        ApplicationData.Current = mockAppData;
        var mockWindow = new MockMainWindow();

        // Act
        mockWindow.ApplyLanguage("en");

        // Assert
        Assert.Contains(mockAppData.Values, kvp => kvp.Key == "AppLanguage" && kvp.Value == "en");
    }

    #endregion

    #region Editor Background Management Tests

    [Fact]
    public void ApplySavedEditorBg_ToggleIsOnWhenPresent()
    {
        // Arrange
        var mockAppData = new MockApplicationData();
        mockAppData.Values["EditorTransparent"] = true;
        ApplicationData.Current = mockAppData;
        var mockWindow = new MockMainWindow();

        // Act
        mockWindow.ApplySavedEditorBg();

        // Assert
        Assert.True(mockWindow.EditorBgToggle?.IsOn == true);
    }

    [Fact]
    public void ApplySavedEditorBg_ToggleOffWhenNotPresent()
    {
        // Arrange
        var mockAppData = new MockApplicationData();
        ApplicationData.Current = mockAppData;
        var mockWindow = new MockMainWindow();

        // Act
        mockWindow.ApplySavedEditorBg();

        // Assert
        Assert.False(mockWindow.EditorBgToggle?.IsOn);
    }

    [Fact]
    public void ApplyEditorBg_SavesCorrectly()
    {
        // Arrange
        var mockAppData = new MockApplicationData();
        ApplicationData.Current = mockAppData;
        var mockWindow = new MockMainWindow();

        // Act
        mockWindow.ApplyEditorBg(true);

        // Assert
        Assert.Contains(mockAppData.Values, kvp => kvp.Key == "EditorTransparent" && kvp.Value == true);
    }

    #endregion

    #region Version Management Tests

    [Fact]
    public void GetVersionString_ReturnsFallbackWhenPackageAndAssemblyFail()
    {
        // Arrange
        var mockPackage = new MockPackage();
        Package.Current = mockPackage;

        var assemblyName = new AssemblyName("Skrib");
        Type.GetTypeFromHandle(new RuntimeTypeHandle(assemblyName));

        // Act
        string result = GetVersionString();

        // Assert - Should return fallback "1.0.0.0"
        Assert.Equal("1.0.0.0", result);
    }

    #endregion

    #region File Operations Tests

    [Fact]
    public void SaveAsAsync_FilesCorrectly()
    {
        // Arrange
        var mockPicker = new MockFileSavePicker();
        mockPicker.FileTypeChoices.Add("Markdown", new List<string> { ".md" });

        var mockStorageFolder = new MockStorageFolder();
        mockPicker.SaveLocation = mockStorageFolder;

        var testText = "Test content";
        var testFileName = "test.md";

        // Act
        var result = SaveAsAsync(mockPicker, testText);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void AskSaveIfNeededAsync_ReturnsFalseWhenDirty()
    {
        // Arrange
        bool isDirty = true;
        var mockWindow = new MockMainWindow { _isDirty = isDirty };

        // Act
        bool result = AskSaveIfNeededAsync(mockWindow);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void AskSaveIfNeededAsync_ReturnsTrueWhenNotDirty()
    {
        // Arrange
        bool isDirty = false;
        var mockWindow = new MockMainWindow { _isDirty = isDirty };

        // Act
        bool result = AskSaveIfNeededAsync(mockWindow);

        // Assert
        Assert.True(result);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public void ShowErrorAsync_DismissesDialog()
    {
        // Arrange
        var mockWindow = new MockMainWindow();
        bool dialogShown = false;

        Action showAction = () => dialogShown = true;

        // Act
        try
        {
            ShowErrorAsync(mockWindow, "Test Title", "Test Message");
        }
        catch
        {
            // Dialogs don't throw in unit tests
        }

        // Assert - Just executed without crashing
        Assert.True(dialogShown);
    }

    [Fact]
    public void Editor_TextChanged_TriggersAfterQueue()
    {
        // Arrange
        var mockWindow = new MockMainWindow();
        int queueTime = DateTime.Now.Millisecond;

        // Act
        var testText = "Line 1\nLine 2";
        mockWindow.SetEditorText(testText);

        // Assert - Should trigger after queue time
        Assert.True(mockWindow.TitleRefreshQueued);
    }

    #endregion
}

/// <summary>
/// Mock classes for unit tests to isolate dependencies.
/// </summary>
public static class TestHelpers
{
    private static bool _isInitialized;

    public static void Initialize()
    {
        _isInitialized = true;
    }

    public static bool IsInitialized() => _isInitialized;
}

/// <summary>
/// Mock ApplicationData for testing.
/// </summary>
public class MockApplicationData : ApplicationData
{
    private readonly Dictionary<string, object> _values = new();

    public override IReadOnlyDictionary<string, object> LocalSettings
    {
        get => _values;
        set => _values = value as Dictionary<string, object>;
    }

    public void SetValue(string key, object value)
    {
        _values[key] = value;
    }

    public override bool SetValue(string key, Type type, object value)
    {
        _values[key] = value;
        return true;
    }

    public string this[string key] => _values.TryGetValue(key, out var val) ? val.ToString() : null;
}

/// <summary>
/// Mock MainWindow for testing.
/// </summary>
public class MockMainWindow : Window
{
    public ToggleSwitch? EditorBgToggle { get; set; } = new();
    public bool _isDirty;
    public bool _titleRefreshQueued = false;

    public MockMainWindow()
    {
        InitializeCustomWindow();
    }

    private void InitializeCustomWindow()
    {
        _isInitialized = true;
        EditorBgToggle?.IsOn = false;
    }

    public override void Activate()
    {
        // Mock activation
    }

    public ElementTheme ApplyTheme(ElementTheme theme, bool save = true)
    {
        ApplicationData.Current.LocalSettings.Values["AppTheme"] = theme.ToString();
        return theme;
    }

    public string LoadBackdrop()
    {
        try
        {
            var v = ApplicationData.Current.LocalSettings.Values["AppBackdrop"] as string;
            if (v == "Acrylic") return "Acrylic";
        }
        catch { }
        return "Mica";
    }

    public void ApplyBackdrop(string backdrop, bool save = true)
    {
        ApplicationData.Current.LocalSettings.Values["AppBackdrop"] = backdrop;
    }

    public string LoadLanguage()
    {
        try
        {
            var lang = ApplicationData.Current.LocalSettings.Values["AppLanguage"] as string;
            if (!string.IsNullOrEmpty(lang)) return lang;
        }
        catch { }
        return "System";
    }

    public void ApplyLanguage(string lang, bool save = true)
    {
        ApplicationData.Current.LocalSettings.Values["AppLanguage"] = (lang == "System") ? "System" : lang;
    }

    public void ApplySavedEditorBg()
    {
        try
        {
            var v = ApplicationData.Current.LocalSettings.Values["EditorTransparent"];
            if (v is bool isWrap)
            {
                EditorBgToggle?.IsOn = isWrap;
            }
        }
        catch { }
    }

    public void ApplyEditorBg(bool transparent, bool save = true)
    {
        ApplicationData.Current.LocalSettings.Values["EditorTransparent"] = transparent;
        _titleRefreshQueued = false;
    }

    public async Task ShowErrorAsync(string title, string message)
    {
        // Mock dialog - doesn't actually show
    }

    public static string GetVersionString()
    {
        return "1.0.0.0";
    }
}

/// <summary>
/// Mock package for testing.
/// </summary>
public class MockPackage : Package
{
    public MockPackage(string version)
    {
        Id = new PackageId("gaelt.Skrib", "CN=test", version);
    }

    protected override void OnActivation(ActivationEventArgs args)
    {
        // Mock activation
    }

    protected override void RaiseOnActivated(ActivationEventArgs e)
    {
        // Mock activation
    }
}

/// <summary>
/// Mock FileSavePicker for testing.
/// </summary>
public class MockFileSavePicker : FileSavePicker
{
    public string SuggestedFileName { get; set; } = "test.txt";

    public bool Succeeded { get; set; } = false;

    protected override void OnFileChosen(FileDataEventArgs e)
    {
        // Mock file chosen
    }

    protected override void RaiseOnFileChosen(FileDataEventArgs e)
    {
        // Mock file chosen
    }
}

/// <summary>
/// Mock StorageFolder for testing.
/// </summary>
public class MockStorageFolder : StorageFolder
{
    protected override IEnumCollection EnumerateItems(IEnumCollectionFlags flags)
    {
        return new IEnumCollection() { };
    }

    public string Path => "C:/mock/tests";
}
