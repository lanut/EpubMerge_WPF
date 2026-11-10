using System.Globalization;
using EpubMerge.Gui.Resources;

namespace EpubMerge.Gui;

/// <summary>Centralizes the UI culture and localized resource lookup for the application.</summary>
public static class LanguageManager
{
    /// <summary>Gets the cultures users can choose in the UI.</summary>
    public static readonly IReadOnlyList<LanguageOption> Languages =
    [
        new("zh-CN", "简体中文"),
        new("en-US", "English"),
        new("zh-TW", "繁體中文"),
        new("ja-JP", "日本語"),
        new("ko-KR", "한국어"),
        new("fr-FR", "Français"),
        new("de-DE", "Deutsch"),
        new("es-ES", "Español"),
        new("pt-BR", "Português (Brasil)"),
        new("ru-RU", "Русский")
    ];

    static LanguageManager()
    {
        CultureInfo.CurrentCulture = CurrentCulture;
        CultureInfo.CurrentUICulture = CurrentCulture;
    }

    /// <summary>Gets the culture currently used for formatting and resources.</summary>
    public static CultureInfo CurrentCulture { get; private set; } = CultureInfo.GetCultureInfo("zh-CN");

    /// <summary>Occurs after the active UI culture changes.</summary>
    public static event EventHandler? CultureChanged;

    public static string ResolveSystemLanguage(string cultureName)
    {
        var exact = Languages.FirstOrDefault(language =>
            string.Equals(language.CultureName, cultureName, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact.CultureName;

        var culture = CultureInfo.GetCultureInfo(cultureName);
        if (culture.TwoLetterISOLanguageName == "zh")
        {
            var parts = culture.Name.Split('-');
            if (parts.Contains("Hans")) return "zh-CN";
            if (parts.Contains("Hant")) return "zh-TW";
            return parts.Any(part => part is "TW" or "HK" or "MO") ? "zh-TW" : "zh-CN";
        }
        return Languages.FirstOrDefault(language =>
            CultureInfo.GetCultureInfo(language.CultureName).TwoLetterISOLanguageName == culture.TwoLetterISOLanguageName)
            ?.CultureName ?? "en-US";
    }

    /// <summary>Changes the UI culture and refreshes the localization extension.</summary>
    /// <param name="cultureName">A valid .NET culture name from <see cref="Languages" />.</param>
    public static void SetCulture(string cultureName)
    {
        var language = Languages.FirstOrDefault(option =>
            string.Equals(option.CultureName, cultureName, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException("Unsupported UI language.", nameof(cultureName));
        var culture = CultureInfo.GetCultureInfo(language.CultureName);
        if (string.Equals(CurrentCulture.Name, culture.Name, StringComparison.OrdinalIgnoreCase)) return;

        CurrentCulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Gets a localized string and optionally formats it with the active culture.</summary>
    /// <param name="key">The resource key.</param>
    /// <param name="arguments">Format arguments for the resource value.</param>
    /// <returns>The localized value, or <paramref name="key" /> when no resource exists.</returns>
    public static string Get(string key, params object[] arguments)
    {
        var value = Language.ResourceManager.GetString(key, CurrentCulture) ?? key;
        return arguments.Length == 0 ? value : string.Format(CurrentCulture, value, arguments);
    }
}

/// <summary>Describes a culture available in the language picker.</summary>
/// <param name="CultureName">The .NET culture identifier.</param>
/// <param name="DisplayName">The label displayed to the user.</param>
public sealed record LanguageOption(string CultureName, string DisplayName);
