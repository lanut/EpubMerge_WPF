using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using EpubMerge.Core.Pure;
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
        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.IsBusy)) UpdateSelectionActions();
        };
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

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择 EPUB 文件夹" };
        if (dialog.ShowDialog(this) == true) ViewModel.AddFiles([dialog.FolderName]);
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

    private void MoveTop_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.MoveFiles(FilesList.SelectedItems.Cast<BookFile>(), int.MinValue);
    }

    private void MoveBottom_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.MoveFiles(FilesList.SelectedItems.Cast<BookFile>(), int.MaxValue);
    }

    private void FilesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSelectionActions();
    }

    private void UpdateSelectionActions()
    {
        var hasSelection = !ViewModel.IsBusy && FilesList.SelectedItems.Count > 0;
        RemoveButton.IsEnabled = hasSelection;
        MoveUpButton.IsEnabled = hasSelection;
        MoveDownButton.IsEnabled = hasSelection;
    }

    private void PickOutput_Click(object sender, RoutedEventArgs e)
    {
        var outputPath = Path.Combine(TaskHistoryStore.LastOutputDirectory ?? Environment.CurrentDirectory, ViewModel.Title.Trim() + ".epub");
        var dialog = new SaveFileDialog { Title = "保存合并后的 EPUB", DefaultExt = ".epub", Filter = "EPUB 文件|*.epub|所有文件|*.*", FileName = Path.GetFileName(outputPath) };

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
        e.Effects = !ViewModel.IsBusy && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void CoverPreview_Drop(object sender, DragEventArgs e)
    {
        if (!ViewModel.IsBusy && e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
        {
            ViewModel.CoverPath = paths[0];
        }
        e.Handled = true;
    }

    private async void Merge_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsBusy)
        {
            ViewModel.CancelMerge();
            return;
        }

        MergeButton.Content = "取消";

        try
        {
            var result = await ViewModel.MergeAsync();
            if (result.Succeeded)
            {
                var open = MessageBox.Show(this, $"已生成：\n{result.OutputPath}\n\n点击“是”打开文件，点击“否”打开所在文件夹。", "合并完成",
                    MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (open == MessageBoxResult.Yes) OpenPath(result.OutputPath!);
                else LocatePath(result.OutputPath!);
            }
            else if (!result.Canceled)
            {
                MessageBox.Show(this, result.Error, "合并失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        finally
        {
            MergeButton.Content = "开始合并";
        }
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

    private void SetCoverFromBook_Click(object sender, RoutedEventArgs e)
    {
        if (FilesList.SelectedItem is BookFile file) ViewModel.ExtractCover(file);
    }

    private void ExportBookCover_Click(object sender, RoutedEventArgs e)
    {
        if (FilesList.SelectedItem is not BookFile file) return;
        var cover = new EpubCoverExtractor().ExtractCover(file.Path);
        if (cover is null)
        {
            MessageBox.Show(this, $"《{file.Name}》未包含有效封面图片", "导出封面", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dialog = new SaveFileDialog { Title = "导出封面图片", DefaultExt = cover.SuggestedExtension, Filter = "图片文件|*" + cover.SuggestedExtension + "|所有文件|*.*", FileName = Path.GetFileNameWithoutExtension(file.Name) + cover.SuggestedExtension };
        if (dialog.ShowDialog(this) == true) File.WriteAllBytes(dialog.FileName, cover.ImageData.ToArray());
    }

    private void LocateBook_Click(object sender, RoutedEventArgs e)
    {
        if (FilesList.SelectedItem is BookFile file) LocatePath(file.Path);
    }

    private void OpenBook_Click(object sender, RoutedEventArgs e)
    {
        if (FilesList.SelectedItem is BookFile file) OpenPath(file.Path);
    }

    private void FilesList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => OpenBook_Click(sender, e);

    private void OpenPath(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(this, "系统没有找到可以打开 EPUB 文件的应用程序。请先安装或关联 EPUB 阅读器。", "打开文件失败",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static void LocatePath(string path) => Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });

    private void History_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        if (ViewModel.RecentTasks.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = "暂无历史任务", IsEnabled = false });
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
