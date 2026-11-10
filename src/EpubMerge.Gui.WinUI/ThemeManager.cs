using Microsoft.UI.Xaml;
using Microsoft.Win32;

namespace EpubMerge.Gui.WinUI;

public enum AppThemePreference { System, Light, Dark }

public static class ThemeManager
{
    private const string RegistryPath = "Software\\EpubMerge.Gui.WinUI";
    public static AppThemePreference Preference { get; private set; }

    public static void Initialize(FrameworkElement root)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
            var value = key?.GetValue("ThemePreference") as string;
            Preference = Enum.TryParse<AppThemePreference>(value, out var theme) ? theme : AppThemePreference.System;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            ApplicationPaths.ReportStorageError(exception);
        }
        SetTheme(root);
    }

    public static void Apply(FrameworkElement root, AppThemePreference preference)
    {
        Preference = preference;
        SetTheme(root);
        using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
        key.SetValue("ThemePreference", preference.ToString());
    }

    private static void SetTheme(FrameworkElement root) => root.RequestedTheme = Preference switch
    {
        AppThemePreference.Light => ElementTheme.Light,
        AppThemePreference.Dark => ElementTheme.Dark,
        _ => ElementTheme.Default
    };
}
