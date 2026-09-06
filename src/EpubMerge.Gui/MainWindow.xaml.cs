using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Navigation;
using EpubMerge.Core.Pure;
using Microsoft.Win32;

namespace EpubMerge.Gui;

/// <summary>Hosts the merge view and adapts WPF-only events and dialogs to view-model commands.</summary>
public partial class MainWindow : Window
{
    /// <summary>Initializes the window with its composed view model.</summary>
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        if (!IsFluentBackdropSupported()) SetResourceReference(BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
        DataContext = viewModel;
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
        if (sender is Button { Tag: string value } && Enum.TryParse<AppThemePreference>(value, out var preference) && preference != ThemeManager.Preference)
            ThemeManager.Apply(preference);
    }

    private void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = LanguageManager.Get("SelectEpubFile"), Filter = LanguageManager.Get("EpubFileFilter"), Multiselect = true };
        if (dialog.ShowDialog(this) == true) ViewModel.AddFiles(dialog.FileNames);
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = LanguageManager.Get("SelectEpubFolder") };
        if (dialog.ShowDialog(this) == true) ViewModel.AddFiles([dialog.FolderName]);
    }

    private void FilesList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ViewModel.SetSelectedFiles(FilesList.SelectedItems.Cast<BookFile>());

    private void PickOutput_Click(object sender, RoutedEventArgs e)
    {
        var outputPath = Path.Combine(TaskHistoryStore.LastOutputDirectory ?? Environment.CurrentDirectory, ViewModel.Title.Trim() + ".epub");
        var dialog = new SaveFileDialog { Title = LanguageManager.Get("SaveMergedEpub"), DefaultExt = ".epub", Filter = LanguageManager.Get("EpubFileFilter"), FileName = Path.GetFileName(outputPath) };
        if (dialog.ShowDialog(this) == true)
        {
            ViewModel.OutputPath = Path.ChangeExtension(dialog.FileName, ".epub");
            if (string.IsNullOrWhiteSpace(ViewModel.Title) || ViewModel.Title == "merged") ViewModel.Title = Path.GetFileNameWithoutExtension(ViewModel.OutputPath);
        }
    }

    private void PickCover_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = LanguageManager.Get("SelectCoverImage"), Filter = LanguageManager.Get("ImageFileFilter") };
        if (dialog.ShowDialog(this) == true) ViewModel.CoverPath = dialog.FileName;
    }

    private void CoverPreview_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = !ViewModel.IsBusy && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void CoverPreview_Drop(object sender, DragEventArgs e)
    {
        if (!ViewModel.IsBusy && e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0) ViewModel.CoverPath = paths[0];
        e.Handled = true;
    }

    private void FilesList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) && !ViewModel.IsBusy ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void FilesList_Drop(object sender, DragEventArgs e)
    {
        if (!ViewModel.IsBusy && e.Data.GetData(DataFormats.FileDrop) is string[] paths) ViewModel.AddFiles(paths);
        e.Handled = true;
    }

    private void ExportBookCover_Click(object sender, RoutedEventArgs e)
    {
        if (FilesList.SelectedItem is not BookFile file) return;
        var cover = ViewModel.GetCoverForExport(file);
        if (cover is null)
        {
            MessageBox.Show(this, LanguageManager.Get("CoverMissing", file.Name), LanguageManager.Get("ExportCoverImage"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dialog = new SaveFileDialog { Title = LanguageManager.Get("ExportCoverImage"), DefaultExt = cover.SuggestedExtension, Filter = LanguageManager.Get("ImageFilter", cover.SuggestedExtension), FileName = Path.GetFileNameWithoutExtension(file.Name) + cover.SuggestedExtension };
        if (dialog.ShowDialog(this) == true) File.WriteAllBytes(dialog.FileName, cover.ImageData);
    }

    private void LocateBook_Click(object sender, RoutedEventArgs e)
    {
        if (FilesList.SelectedItem is BookFile file) LocatePath(file.Path);
    }

    private void OpenBook_Click(object sender, RoutedEventArgs e)
    {
        if (FilesList.SelectedItem is BookFile file) OpenPath(file.Path);
    }

    private void FilesList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenBook_Click(sender, e);

    private void OpenPath(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Win32Exception)
        {
            MessageBox.Show(this, LanguageManager.Get("OpenEpubFailed"), LanguageManager.Get("OpenFileFailed"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static void LocatePath(string path) => Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });

    private async void Merge_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsBusy)
        {
            ViewModel.CancelMergeCommand.Execute(null);
            return;
        }

        var result = await ViewModel.MergeAsync();
        if (result.Succeeded)
        {
            var open = MessageBox.Show(this, $"{LanguageManager.Get("MergeCompleted", result.OutputPath!)}\n\n{LanguageManager.Get("OpenOutputPrompt")}", LanguageManager.Get("MergeCompletedTitle"), MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (open == MessageBoxResult.Yes) OpenPath(result.OutputPath!);
            else LocatePath(result.OutputPath!);
        }
        else if (!result.Canceled)
        {
            MessageBox.Show(this, result.Error ?? LanguageManager.Get("MergeFailedRetry"), LanguageManager.Get("MergeFailed"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void History_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        if (ViewModel.RecentTasks.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = LanguageManager.Get("NoHistory"), IsEnabled = false });
        }
        else
        {
            foreach (var task in ViewModel.RecentTasks)
            {
                var item = new MenuItem { Header = task.DisplayName, ToolTip = task.OutputPath };
                item.Click += (_, _) => ViewModel.RestoreTask(task);
                menu.Items.Add(item);
            }
        }
        menu.PlacementTarget = (UIElement)sender;
        menu.IsOpen = true;
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
