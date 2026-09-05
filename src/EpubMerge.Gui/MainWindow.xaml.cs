using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using Microsoft.Win32;

namespace EpubMerge.Gui;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        if (!IsFluentBackdropSupported())
            SetResourceReference(BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
        DataContext = new MainViewModel();
        UpdateSelectionActions();
    }
    private MainViewModel ViewModel => (MainViewModel)DataContext;

    private static bool IsFluentBackdropSupported()
    {
        var version = Environment.OSVersion.Version;
        var backdropSwitch = AppContext.GetData("Switch.System.Windows.Appearance.DisableFluentThemeWindowBackdrop");
        var disabled = backdropSwitch is not null && bool.TryParse(Convert.ToString(backdropSwitch), out var isDisabled) && isDisabled;
        return !disabled && version.Major >= 10 && version.Build >= 22621;
    }

    private void ThemeModeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string value ||
            !Enum.TryParse<AppThemePreference>(value, out var preference)) return;
        if (preference != ThemeManager.Preference) ThemeManager.Apply(preference);
    }

    private void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "选择 EPUB 文件", Filter = "EPUB 文件|*.epub|所有文件|*.*", Multiselect = true };
        if (dialog.ShowDialog(this) == true) ViewModel.AddFiles(dialog.FileNames);
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.RemoveFiles(FilesList.SelectedItems.Cast<BookFile>());
    }
    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ClearFiles();
    }
    private void MoveUp_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.MoveFiles(FilesList.SelectedItems.Cast<BookFile>(), -1);
    }
    private void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.MoveFiles(FilesList.SelectedItems.Cast<BookFile>(), 1);
    }

    private void FilesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSelectionActions();
    }

    private void UpdateSelectionActions()
    {
        var hasSelection = FilesList.SelectedItems.Count > 0;
        RemoveButton.IsEnabled = hasSelection;
        MoveUpButton.IsEnabled = hasSelection;
        MoveDownButton.IsEnabled = hasSelection;
    }

    private void PickOutput_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Title = "保存合并后的 EPUB", DefaultExt = ".epub", Filter = "EPUB 文件|*.epub|所有文件|*.*", FileName = Path.GetFileName(ViewModel.OutputPath) };

        if (dialog.ShowDialog(this) == true)
        {
            ViewModel.OutputPath = Path.ChangeExtension(dialog.FileName, ".epub");
            if (string.IsNullOrWhiteSpace(ViewModel.Title) || ViewModel.Title == "merged") ViewModel.Title = Path.GetFileNameWithoutExtension(ViewModel.OutputPath);
        }
    }

    private void PickCover_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "选择封面图片", Filter = "图片文件|*.jpg;*.jpeg;*.png;*.gif;*.webp;*.svg|所有文件|*.*" };
        if (dialog.ShowDialog(this) == true) ViewModel.CoverPath = dialog.FileName;
    }

    private void ClearCover_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.CoverPath = string.Empty;
    }

    private void CoverPreview_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void CoverPreview_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
        {
            ViewModel.CoverPath = paths[0];
        }
        e.Handled = true;
    }

    private async void Merge_Click(object sender, RoutedEventArgs e)
    {
        MergeButton.IsEnabled = false;

        try
        {
            var result = await ViewModel.MergeAsync();
            MessageBox.Show(this, result.Succeeded ? $"已生成：\n{result.OutputPath}" : result.Error, result.Succeeded ? "合并完成" : "合并失败",
                MessageBoxButton.OK, result.Succeeded ? MessageBoxImage.Information : MessageBoxImage.Error);
        }
        finally { MergeButton.IsEnabled = true; }
    }

    private void FilesList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
    }
    private void FilesList_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) ViewModel.AddFiles(paths.Where(path => string.Equals(Path.GetExtension(path), ".epub", StringComparison.OrdinalIgnoreCase)));
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
