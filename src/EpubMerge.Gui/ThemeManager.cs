using System.Windows;
using Microsoft.Win32;

namespace EpubMerge.Gui;

/// <summary>Represents the user's persisted visual theme choice.</summary>
public enum AppThemePreference
{
    /// <summary>Follows the Windows application theme.</summary>
    System,
    /// <summary>Always uses the light theme.</summary>
    Light,
    /// <summary>Always uses the dark theme.</summary>
    Dark
}

/// <summary>Applies and persists the app theme, including live system-theme following.</summary>
public static class ThemeManager
{
    private const string RegistryPath = "Software\\EpubMerge.Gui";
    private const string RegistryValue = "ThemePreference";
    private static bool _initialized;

    /// <summary>Gets the user's selected theme preference.</summary>
    public static AppThemePreference Preference { get; private set; } = AppThemePreference.System;

    /// <summary>Loads the saved preference and starts listening for system theme changes.</summary>
    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        Preference = ReadPreference();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        Apply(Preference, false);
    }

    /// <summary>Applies a theme preference and optionally saves it for future launches.</summary>
    /// <param name="preference">The requested system, light, or dark mode.</param>
    /// <param name="persist">Whether to write the choice to the current-user registry.</param>
    public static void Apply(AppThemePreference preference, bool persist = true)
    {
        Preference = preference;
        if (persist) SavePreference(preference);

        var effectiveTheme = preference == AppThemePreference.System ? DetectSystemTheme() : preference;
        Application.Current.ThemeMode = effectiveTheme == AppThemePreference.Dark ? ThemeMode.Dark : ThemeMode.Light;

    }

    /// <summary>Stops listening for system preference changes during application shutdown.</summary>
    public static void Shutdown()
    {
        if (!_initialized) return;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _initialized = false;
    }

    private static void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        // Only re-evaluate while following Windows; explicit light/dark selections must remain stable.
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
