using System.ComponentModel;

namespace EpubMerge.Gui;

/// <summary>Refreshes localized XAML indexer bindings when the culture changes.</summary>
public sealed class LocalizedStrings : INotifyPropertyChanged, IDisposable
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public string this[string key] => LanguageManager.Get(key);

    public LocalizedStrings() => LanguageManager.CultureChanged += OnCultureChanged;

    private void OnCultureChanged(object? sender, EventArgs e) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));

    public void Dispose() => LanguageManager.CultureChanged -= OnCultureChanged;
}
