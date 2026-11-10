using System.ComponentModel;

namespace EpubMerge.Gui;

/// <summary>Refreshes localized XAML bindings when the culture changes.</summary>
public sealed class LocalizedStrings : INotifyPropertyChanged, IDisposable
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public string this[string key] => LanguageManager.Get(key);
    public string Get(string key) => this[key];

    public LocalizedStrings() => LanguageManager.CultureChanged += OnCultureChanged;

    private void OnCultureChanged(object? sender, EventArgs e)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Get)));
    }

    public void Dispose() => LanguageManager.CultureChanged -= OnCultureChanged;
}
