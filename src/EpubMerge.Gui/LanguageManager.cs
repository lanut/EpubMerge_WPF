using System.Globalization;
using Antelcat.I18N.WPF;
using EpubMerge.Gui.Resources;

namespace EpubMerge.Gui;

public static class LanguageManager
{
    public static readonly IReadOnlyList<LanguageOption> Languages =
    [
        new("zh-CN", "中文"),
        new("en-US", "English")
    ];

    public static CultureInfo CurrentCulture { get; private set; } = CultureInfo.GetCultureInfo("zh-CN");

    public static event EventHandler? CultureChanged;

    static LanguageManager()
    {
        CultureInfo.CurrentCulture = CurrentCulture;
        CultureInfo.CurrentUICulture = CurrentCulture;
        I18NExtension.Culture = CurrentCulture;
    }

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

    public static string Get(string key, params object[] arguments)
    {
        var value = Language.ResourceManager.GetString(key, CurrentCulture) ?? key;
        return arguments.Length == 0 ? value : string.Format(CurrentCulture, value, arguments);
    }
}

public sealed record LanguageOption(string CultureName, string DisplayName);