using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using EpubMerge.Gui;
using Xunit;

namespace EpubMerge.Presentation.Tests;

[Collection("Language")]
public sealed class LocalizationTests
{
    [Theory]
    [InlineData("zh-HK", "zh-TW")]
    [InlineData("zh-MO", "zh-TW")]
    [InlineData("zh-Hant", "zh-TW")]
    [InlineData("zh-Hant-HK", "zh-TW")]
    [InlineData("zh-SG", "zh-CN")]
    [InlineData("zh-Hans", "zh-CN")]
    [InlineData("zh-Hans-HK", "zh-CN")]
    [InlineData("zh-Hant-CN", "zh-TW")]
    [InlineData("en-GB", "en-US")]
    [InlineData("fr-CA", "fr-FR")]
    [InlineData("de-AT", "de-DE")]
    [InlineData("es-MX", "es-ES")]
    [InlineData("pt-PT", "pt-BR")]
    [InlineData("ja", "ja-JP")]
    [InlineData("ko", "ko-KR")]
    [InlineData("ru", "ru-RU")]
    [InlineData("ar-SA", "en-US")]
    [InlineData("", "en-US")]
    public void System_cultures_resolve_to_supported_languages(string system, string expected) =>
        Assert.Equal(expected, LanguageManager.ResolveSystemLanguage(system));

    [Fact]
    public void All_languages_have_complete_resources_and_matching_store_declarations()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Localization");
        var neutral = ReadResources(Path.Combine(directory, "Language.resx"));
        var files = Directory.GetFiles(directory, "Language*.resx");
        Assert.Equal(10, files.Length);
        Assert.Equal(10, LanguageManager.Languages.Count);
        foreach (var file in files)
        {
            var resources = ReadResources(file);
            Assert.Equal(neutral.Keys.Order(), resources.Keys.Order());
            foreach (var (key, value) in resources)
            {
                Assert.False(string.IsNullOrWhiteSpace(value), $"{file}: {key}");
                Assert.Equal(Arguments(neutral[key]), Arguments(value));
                var format = CompositeFormat.Parse(value);
                _ = string.Format(CultureInfo.InvariantCulture, format,
                    Enumerable.Repeat<object>("test", format.MinimumArgumentCount).ToArray());
                if (neutral[key].Contains('|'))
                {
                    var source = neutral[key].Split('|');
                    var translated = value.Split('|');
                    Assert.Equal(source.Length, translated.Length);
                    for (var index = 1; index < source.Length; index += 2)
                        Assert.Equal(source[index], translated[index]);
                }
            }
        }
        var manifest = XDocument.Load(Path.Combine(directory, "Package.appxmanifest.template"));
        XNamespace ns = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
        Assert.Equal(LanguageManager.Languages.Select(option => option.CultureName).Order(),
            manifest.Descendants(ns + "Resource").Select(element => (string)element.Attribute("Language")!).Order());
        var previous = LanguageManager.CurrentCulture.Name;
        try
        {
            foreach (var language in LanguageManager.Languages)
            {
                Assert.Equal(language.CultureName, LanguageManager.ResolveSystemLanguage(language.CultureName));
                var culture = CultureInfo.GetCultureInfo(language.CultureName);
                var suffix = culture.Name == "zh-CN" ? "" :
                    culture.Name is "zh-TW" or "pt-BR" ? "." + culture.Name : "." + culture.TwoLetterISOLanguageName;
                var ownResources = ReadResources(Path.Combine(directory, $"Language{suffix}.resx"));
                LanguageManager.SetCulture(language.CultureName);
                foreach (var key in new[] { "WindowTitle", "AddFiles", "Close", "MergeErrorUnknown" })
                    Assert.Equal(ownResources[key], LanguageManager.Get(key));
            }
        }
        finally { LanguageManager.SetCulture(previous); }
    }

    [Fact]
    public void Preference_preserves_explicit_choice_without_saving_system_default()
    {
        var writes = new List<string>();
        var errors = new List<Exception>();
        string? stored = null;
        var preference = new LanguagePreference(() => stored, writes.Add, errors.Add);
        Assert.Equal("fr-FR", preference.Load("fr-CA"));
        Assert.Empty(writes);
        stored = "ja-JP";
        Assert.Equal("ja-JP", preference.Load("fr-FR"));
        Assert.True(preference.Save("de-DE"));
        Assert.Equal(["de-DE"], writes);
        stored = "invalid";
        Assert.Equal("ko-KR", preference.Load("ko-KR"));
        Assert.IsType<InvalidDataException>(Assert.Single(errors));
        Assert.Throws<ArgumentException>(() => preference.Save("invalid"));
    }

    [Fact]
    public void Preference_reports_storage_failures_and_does_not_claim_success()
    {
        var errors = new List<Exception>();
        var preference = new LanguagePreference(
            () => throw new UnauthorizedAccessException("read denied"),
            _ => throw new IOException("write failed"), errors.Add);
        Assert.Equal("en-US", preference.Load("it-IT"));
        Assert.False(preference.Save("ja-JP"));
        Assert.IsType<UnauthorizedAccessException>(errors[0]);
        Assert.IsType<IOException>(errors[1]);
        var invalidTypePreference = new LanguagePreference(
            () => throw new InvalidDataException("Registry value is not a string"), _ => { }, errors.Add);
        Assert.Equal("fr-FR", invalidTypePreference.Load("fr-FR"));
        Assert.IsType<InvalidDataException>(errors[2]);
    }

    private static Dictionary<string, string> ReadResources(string path) =>
        XDocument.Load(path).Root!.Elements("data").ToDictionary(
            element => (string)element.Attribute("name")!, element => (string)element.Element("value")!);

    private static string[] Arguments(string value) =>
        Regex.Matches(value, @"\{(\d+)(?:[^{}]*)\}").Select(match => match.Groups[1].Value).Distinct().Order().ToArray();
}
