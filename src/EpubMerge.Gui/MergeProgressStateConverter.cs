using System.Globalization;
using System.Windows.Data;
using System.Windows.Shell;

namespace EpubMerge.Gui;

/// <summary>Maps presentation-neutral progress state to the WPF taskbar API.</summary>
public sealed class MergeProgressStateConverter : IValueConverter
{
    /// <summary>Converts an application progress state to the corresponding WPF taskbar state.</summary>
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is MergeProgressState state ? state switch
        {
            MergeProgressState.Normal => TaskbarItemProgressState.Normal,
            MergeProgressState.Paused => TaskbarItemProgressState.Paused,
            MergeProgressState.Error => TaskbarItemProgressState.Error,
            _ => TaskbarItemProgressState.None
        } : TaskbarItemProgressState.None;

    /// <summary>Reverse conversion is unsupported because the binding is one-way.</summary>
    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
