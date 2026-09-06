using System.Globalization;
using Antelcat.I18N.WPF;
using EpubMerge.Gui.Resources;

namespace EpubMerge.Gui;

/// <summary>Centralizes the UI culture and localized resource lookup for the application.</summary>
public static class LanguageManager
{
    /// <summary>Gets the cultures users can choose in the UI.</summary>
    public static readonly IReadOnlyList<LanguageOption> Languages =
    [
        new("zh-CN", "中文"),
        new("en-US", "English")
    ];

    /// <summary>Gets the culture currently used for formatting and resources.</summary>
    public static CultureInfo CurrentCulture { get; private set; } = CultureInfo.GetCultureInfo("zh-CN");

    /// <summary>Occurs after the active UI culture changes.</summary>
    public static event EventHandler? CultureChanged;

    static LanguageManager()
    {
        CultureInfo.CurrentCulture = CurrentCulture;
        CultureInfo.CurrentUICulture = CurrentCulture;
        I18NExtension.Culture = CurrentCulture;
    }

    /// <summary>Changes the UI culture and refreshes the localization extension.</summary>
    /// <param name="cultureName">A valid .NET culture name from <see cref="Languages"/>.</param>
    public static void SetCulture(string cultureName)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);
        if (string.Equals(CurrentCulture.Name, culture.Name, StringComparison.OrdinalIgnoreCase)) return;

        CurrentCulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        I18NExtension.Culture = culture;
        CultureChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Gets a localized string and optionally formats it with the active culture.</summary>
    /// <param name="key">The resource key.</param>
    /// <param name="arguments">Format arguments for the resource value.</param>
    /// <returns>The localized value, or <paramref name="key"/> when no resource exists.</returns>
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
