namespace EpubMerge.Gui;

public sealed class LanguagePreference(Func<string?> read, Action<string> write, Action<Exception> reportError)
{
    public string Load(string systemCulture)
    {
        try
        {
            var saved = read();
            if (saved is not null)
            {
                var language = LanguageManager.Languages.FirstOrDefault(option =>
                    string.Equals(option.CultureName, saved, StringComparison.OrdinalIgnoreCase));
                if (language is not null) return language.CultureName;
                reportError(new InvalidDataException($"Unsupported saved UI language: {saved}"));
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            reportError(exception);
        }
        return LanguageManager.ResolveSystemLanguage(systemCulture);
    }

    public bool Save(string cultureName)
    {
        var language = LanguageManager.Languages.FirstOrDefault(option => option.CultureName == cultureName)
            ?? throw new ArgumentException("Unsupported UI language.", nameof(cultureName));
        try
        {
            write(language.CultureName);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            reportError(exception);
            return false;
        }
    }
}
