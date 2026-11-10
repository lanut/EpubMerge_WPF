using Microsoft.Win32;

namespace EpubMerge.Gui.WinUI;

internal static class LanguagePreferenceStore
{
    private const string RegistryPath = @"Software\EpubMerge.Gui.WinUI";
    private const string ValueName = "LanguagePreference";

    public static string? Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
        return key?.GetValue(ValueName) switch
        {
            null => null,
            string value => value,
            _ => throw new InvalidDataException("Saved UI language must be a string.")
        };
    }

    public static void Write(string cultureName)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
        key.SetValue(ValueName, cultureName);
    }
}
