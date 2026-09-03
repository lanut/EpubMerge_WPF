using System.Windows;
using Microsoft.Win32;

namespace EpubMerge.Gui;

public enum AppThemePreference
{
    System,
    Light,
    Dark
}

public static class ThemeManager
{
    private const string RegistryPath = "Software\\EpubMerge.Gui";
    private const string RegistryValue = "ThemePreference";
    private static bool _initialized;

    public static AppThemePreference Preference { get; private set; } = AppThemePreference.System;

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        Preference = ReadPreference();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        Apply(Preference, false);
    }

    public static void Apply(AppThemePreference preference, bool persist = true)
    {
        Preference = preference;
        if (persist) SavePreference(preference);

        var effectiveTheme = preference == AppThemePreference.System ? DetectSystemTheme() : preference;
        Application.Current.ThemeMode = effectiveTheme == AppThemePreference.Dark ? ThemeMode.Dark : ThemeMode.Light;

    }

    public static void Shutdown()
    {
        if (!_initialized) return;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _initialized = false;
    }

    private static void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (Preference != AppThemePreference.System || e.Category != UserPreferenceCategory.General) return;
        Application.Current.Dispatcher.Invoke(() => Apply(AppThemePreference.System, false));
    }

    private static AppThemePreference DetectSystemTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int value && value != 0
            ? AppThemePreference.Light
            : AppThemePreference.Dark;
    }

    private static AppThemePreference ReadPreference()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
        return Enum.TryParse(key?.GetValue(RegistryValue) as string, true, out AppThemePreference value)
            ? value
            : AppThemePreference.System;
    }

    private static void SavePreference(AppThemePreference preference)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
        key?.SetValue(RegistryValue, preference.ToString());
    }
}
