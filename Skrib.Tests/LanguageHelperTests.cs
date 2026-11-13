using Skrib;

namespace Skrib.Tests;

/// <summary>
/// Covers the language resolution rules shared by the UI and the settings screen.
/// </summary>
public class LanguageHelperTests
{
    [Theory]
    [InlineData("en")]
    [InlineData("fr")]
    [InlineData("es")]
    public void NormalizeMode_KeepsSupportedTag(string tag)
    {
        Assert.Equal(tag, LanguageHelper.NormalizeMode(tag));
    }

    [Fact]
    public void NormalizeMode_KeepsSystemTag()
    {
        Assert.Equal(LanguageHelper.SystemTag, LanguageHelper.NormalizeMode(LanguageHelper.SystemTag));
    }

    [Theory]
    [InlineData("de")]
    [InlineData("zh")]
    [InlineData("System ")]
    public void NormalizeMode_FallsBackToFrenchForUnknownTag(string tag)
    {
        Assert.Equal(LanguageHelper.French, LanguageHelper.NormalizeMode(tag));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void NormalizeMode_FallsBackToSystemForEmptyTag(string? tag)
    {
        Assert.Equal(LanguageHelper.SystemTag, LanguageHelper.NormalizeMode(tag!));
    }

    [Theory]
    [InlineData("en", true)]
    [InlineData("fr", true)]
    [InlineData("es", true)]
    [InlineData("de", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsSupported_OnlyAcceptsKnownTags(string? tag, bool expected)
    {
        Assert.Equal(expected, LanguageHelper.IsSupported(tag!));
    }

    [Theory]
    [InlineData("es")]
    [InlineData("es-ES")]
    [InlineData("es-MX")]
    [InlineData("es-419")]
    public void ResolveSystemLanguage_DetectsSpanish(string cultureName)
    {
        Assert.Equal(LanguageHelper.Spanish, LanguageHelper.ResolveSystemLanguage(cultureName));
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("fr-FR")]
    [InlineData("fr-CA")]
    public void ResolveSystemLanguage_DetectsFrench(string cultureName)
    {
        Assert.Equal(LanguageHelper.French, LanguageHelper.ResolveSystemLanguage(cultureName));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("en-US")]
    [InlineData("en-GB")]
    public void ResolveSystemLanguage_DetectsEnglish(string cultureName)
    {
        Assert.Equal(LanguageHelper.English, LanguageHelper.ResolveSystemLanguage(cultureName));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("zh-CN")]
    [InlineData("ja-JP")]
    [InlineData("pt-BR")]
    public void ResolveSystemLanguage_FallsBackToEnglish(string cultureName)
    {
        Assert.Equal(LanguageHelper.English, LanguageHelper.ResolveSystemLanguage(cultureName));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void ResolveSystemLanguage_FallsBackToEnglishForEmptyCulture(string? cultureName)
    {
        Assert.Equal(LanguageHelper.English, LanguageHelper.ResolveSystemLanguage(cultureName!));
    }

    [Fact]
    public void Supported_ExposesExactlyTheShippedLanguages()
    {
        Assert.Equal(
            new[] { LanguageHelper.English, LanguageHelper.French, LanguageHelper.Spanish },
            LanguageHelper.Supported);
    }

    [Fact]
    public void Supported_AreAllResolvableByTheSystemLookup()
    {
        foreach (var tag in LanguageHelper.Supported)
        {
            Assert.Equal(tag, LanguageHelper.ResolveSystemLanguage(tag));
        }
    }
}
