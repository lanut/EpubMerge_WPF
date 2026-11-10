using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Input;
using EpubMerge.Core.Pure;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Windows.Storage.Pickers;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;

namespace EpubMerge.Gui.WinUI;

public sealed partial class MainWindow : Window
{
    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".gif", ".webp", ".svg"];
    private readonly LocalizedStrings _strings = new();
    private readonly LanguagePreference _languagePreference;
    private readonly Queue<(string Title, string Message)> _messages = new();
    private BookFile[] _draggedFiles = [];
    private TaskbarProgress? _taskbar;
    private bool _changingSelection;
    private bool _dialogOpen;
    private bool _closing;
    private bool _previewFailed;

    public MainViewModel ViewModel { get; }
    public LocalizedStrings Strings => _strings;

    public MainWindow(MainViewModel viewModel, LanguagePreference languagePreference, IReadOnlyList<Exception> startupErrors)
    {
        ViewModel = viewModel;
        _languagePreference = languagePreference;
        InitializeComponent();
        foreach (var language in LanguageManager.Languages)
        {
            var item = new ToggleMenuFlyoutItem { Text = language.DisplayName, Tag = language.CultureName };
            item.Click += Language_Click;
            LanguageMenu.Items.Add(item);
        }
        UpdateLanguageMenu();
        Title = LanguageManager.Get("WindowTitle");
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        AppWindow.Resize(new SizeInt32(1200, 800));
        AppWindow.Changed += Window_Changed;
        Root.Loaded += Root_Loaded;
        Root.SizeChanged += (_, _) => EnforceMinimumSize();
        Root.ActualThemeChanged += (_, _) => UpdateTitleBar();
        Closed += Window_Closed;
        ViewModel.PropertyChanged += ViewModel_Changed;
        ViewModel.Files.CollectionChanged += Files_Changed;
        LanguageManager.CultureChanged += Culture_Changed;
        ApplicationPaths.StorageError += Storage_Error;
        foreach (var exception in startupErrors) Storage_Error(this, exception);
        ThemeManager.Initialize(Root);
        UpdateState();
        UpdateTitleBar();
    }

    private void Root_Loaded(object sender, RoutedEventArgs e)
    {
        EnforceMinimumSize();
        try { _taskbar = new TaskbarProgress(WinRT.Interop.WindowNative.GetWindowHandle(this)); }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException)
        {
            ApplicationPaths.ReportStorageError(exception);
        }
        UpdateState();
        UpdatePreview();
        _ = DrainMessagesAsync();
    }

    private void Window_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidSizeChange) EnforceMinimumSize();
    }

    private void Splitter_DragDelta(object sender, Microsoft.UI.Xaml.Controls.Primitives.DragDeltaEventArgs args)
    {
        var maximum = Math.Max(280, ContentGrid.ActualWidth - 344);
        SettingsColumn.Width = new GridLength(Math.Clamp(SettingsColumn.ActualWidth - args.HorizontalChange, 280, maximum));
    }

    private void UpdateTitleBar()
    {
        var dark = Root.ActualTheme == ElementTheme.Dark;
        var background = dark ? Windows.UI.Color.FromArgb(255, 32, 32, 32) : Microsoft.UI.Colors.WhiteSmoke;
        var foreground = dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
        AppWindow.TitleBar.BackgroundColor = background;
        AppWindow.TitleBar.ForegroundColor = foreground;
        AppWindow.TitleBar.ButtonBackgroundColor = background;
        AppWindow.TitleBar.ButtonForegroundColor = foreground;
        AppWindow.TitleBar.InactiveBackgroundColor = background;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = background;
    }

    private void EnforceMinimumSize()
    {
        if (ContentGrid.ActualWidth > 0)
        {
            var maximum = Math.Max(280, ContentGrid.ActualWidth - 344);
            if (SettingsColumn.Width.Value > maximum) SettingsColumn.Width = new GridLength(maximum);
        }
        if (AppWindow.Presenter is OverlappedPresenter { State: not OverlappedPresenterState.Restored }) return;
        var scale = Root.XamlRoot?.RasterizationScale ?? 1;
        var width = (int)Math.Ceiling(800 * scale);
        var height = (int)Math.Ceiling(600 * scale);
        if (AppWindow.Size.Width < width || AppWindow.Size.Height < height)
            AppWindow.Resize(new SizeInt32(Math.Max(width, AppWindow.Size.Width), Math.Max(height, AppWindow.Size.Height)));
    }

    private void Window_Closed(object sender, WindowEventArgs args)
    {
        _closing = true;
        ViewModel.PropertyChanged -= ViewModel_Changed;
        ViewModel.Files.CollectionChanged -= Files_Changed;
        LanguageManager.CultureChanged -= Culture_Changed;
        ApplicationPaths.StorageError -= Storage_Error;
        ViewModel.Dispose();
        _strings.Dispose();
        _taskbar?.Dispose();
    }

    private void Culture_Changed(object? sender, EventArgs args)
    {
        Title = LanguageManager.Get("WindowTitle");
        UpdateLanguageMenu();
        PreviewStatus.Text = LanguageManager.Get(_previewFailed ? "CoverReadDetails" : "SelectImagePreview");
        if (!_previewFailed) PreviewStatus.Text = ViewModel.CoverPreviewStatus;
    }

    private void UpdateLanguageMenu()
    {
        LanguagePicker.Content = LanguageManager.Languages.First(language =>
            language.CultureName == LanguageManager.CurrentCulture.Name).DisplayName;
        foreach (var item in LanguageMenu.Items.OfType<ToggleMenuFlyoutItem>())
            item.IsChecked = (string)item.Tag == LanguageManager.CurrentCulture.Name;
    }

    private void Language_Click(object sender, RoutedEventArgs args)
    {
        if (sender is not ToggleMenuFlyoutItem { Tag: string cultureName }) return;
        if (cultureName != ViewModel.SelectedLanguage)
        {
            ViewModel.SelectedLanguage = cultureName;
            _languagePreference.Save(cultureName);
        }
        UpdateLanguageMenu();
    }

    private void Storage_Error(object? sender, Exception exception)
    {
        if (!DispatcherQueue.TryEnqueue(() => QueueMessage(LanguageManager.Get("OperationFailed"),
                LanguageManager.Get("StorageFailed"))))
            Trace.TraceError(exception.ToString());
    }

    private void Files_Changed(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs args) =>
        EmptyHint.Visibility = ViewModel.Files.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void ViewModel_Changed(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(MainViewModel.CoverPreviewPath)) UpdatePreview();
        if (args.PropertyName == nameof(MainViewModel.CoverPreviewStatus) && !_previewFailed)
            PreviewStatus.Text = ViewModel.CoverPreviewStatus;
        UpdateState();
    }

    private void UpdateState()
    {
        EmptyHint.Visibility = ViewModel.Files.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        MergeProgress.Visibility = ViewModel.IsBusy ? Visibility.Visible : Visibility.Collapsed;
        try { _taskbar?.Update(ViewModel.ProgressState, ViewModel.ProgressValue); }
        catch (System.Runtime.InteropServices.COMException exception)
        {
            ApplicationPaths.ReportStorageError(exception);
            _taskbar?.Dispose();
            _taskbar = null;
        }
    }

    private void UpdatePreview()
    {
        _previewFailed = false;
        CoverImage.Source = null;
        CoverHint.Visibility = ViewModel.CoverPreviewPath is null ? Visibility.Visible : Visibility.Collapsed;
        PreviewStatus.Text = ViewModel.CoverPreviewStatus;
        if (ViewModel.CoverPreviewPath is not { } path) return;
        try
        {
            var uri = new Uri(path);
            if (string.Equals(Path.GetExtension(path), ".svg", StringComparison.OrdinalIgnoreCase))
            {
                var source = new SvgImageSource();
                source.OpenFailed += (_, _) => PreviewFailed(source);
                CoverImage.Source = source;
                source.UriSource = uri;
            }
            else
            {
                var source = new BitmapImage();
                source.ImageFailed += (_, _) => PreviewFailed(source);
                CoverImage.Source = source;
                source.UriSource = uri;
            }
        }
        catch (Exception exception) when (exception is ArgumentException or System.Runtime.InteropServices.COMException)
        {
            PreviewFailed(CoverImage.Source);
            ApplicationPaths.ReportStorageError(exception);
        }
    }

    private void PreviewFailed(ImageSource? source)
    {
        if (_closing || !ReferenceEquals(CoverImage.Source, source)) return;
        _previewFailed = true;
        CoverImage.Source = null;
        CoverHint.Visibility = Visibility.Visible;
        PreviewStatus.Text = LanguageManager.Get("CoverReadDetails");
    }

    private async Task RunUiAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException
                                             or System.Runtime.InteropServices.COMException or Win32Exception
                                             or NotSupportedException or System.Security.SecurityException)
        {
            ApplicationPaths.ReportStorageError(exception);
            QueueMessage(LanguageManager.Get("OperationFailed"), exception.Message);
        }
    }

    private async void Theme_Click(object sender, RoutedEventArgs args) => await RunUiAsync(() =>
    {
        if (sender is Button { Tag: string value } && Enum.TryParse<AppThemePreference>(value, out var theme))
            ThemeManager.Apply(Root, theme);
        return Task.CompletedTask;
    });

    private async void AddFiles_Click(object sender, RoutedEventArgs args) => await RunUiAsync(async () =>
    {
        if (!ViewModel.CanEdit) return;
        var picker = new FileOpenPicker(AppWindow.Id);
        picker.FileTypeFilter.Add(".epub");
        var files = await picker.PickMultipleFilesAsync();
        ViewModel.AddFiles(files.Select(file => file.Path));
    });

    private async void AddFolder_Click(object sender, RoutedEventArgs args) => await RunUiAsync(async () =>
    {
        if (!ViewModel.CanEdit) return;
        var folder = await new FolderPicker(AppWindow.Id).PickSingleFolderAsync();
        if (folder is null) return;
        var recursive = await ShowDialogAsync(LanguageManager.Get("AddFolder"),
            LanguageManager.Get("RecursivePrompt"), LanguageManager.Get("Yes"), LanguageManager.Get("No"));
        ViewModel.AddFiles([folder.Path], recursive == ContentDialogResult.Primary);
    });

    private async void PickOutput_Click(object sender, RoutedEventArgs args) => await RunUiAsync(async () =>
    {
        if (!ViewModel.CanEdit) return;
        var picker = CreateSavePicker(ViewModel.Title, ".epub");
        var result = await picker.PickSaveFileAsync();
        if (result is null) return;
        ViewModel.OutputPath = Path.ChangeExtension(result.Path, ".epub");
        if (string.IsNullOrWhiteSpace(ViewModel.Title) || ViewModel.Title == "merged")
            ViewModel.Title = Path.GetFileNameWithoutExtension(ViewModel.OutputPath);
    });

    private FileSavePicker CreateSavePicker(string name, string extension)
    {
        var picker = new FileSavePicker(AppWindow.Id)
        {
            SuggestedFileName = Path.GetFileNameWithoutExtension(name),
            SuggestedFolder = TaskHistoryStore.LastOutputDirectory ?? ApplicationPaths.DefaultOutputDirectory,
            DefaultFileExtension = extension
        };
        picker.FileTypeChoices.Add(extension, [extension]);
        return picker;
    }

    private async void PickCover_Click(object sender, RoutedEventArgs args) => await RunUiAsync(async () =>
    {
        if (!ViewModel.CanEdit) return;
        var picker = new FileOpenPicker(AppWindow.Id);
        foreach (var extension in ImageExtensions) picker.FileTypeFilter.Add(extension);
        var file = await picker.PickSingleFileAsync();
        if (file is not null) ViewModel.CoverPath = file.Path;
    });

    private void Files_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!_changingSelection) ViewModel.SetSelectedFiles(FilesList.SelectedItems.Cast<BookFile>().ToArray());
    }

    private void FilesMenu_Opening(object sender, object args)
    {
        if (sender is not MenuFlyout menu) return;
        foreach (var item in menu.Items.OfType<MenuFlyoutItem>())
        {
            item.IsEnabled = ViewModel.CanEditWithSelection;
        }
    }

    private async void ListAction_Click(object sender, RoutedEventArgs args) => await RunUiAsync(async () =>
    {
        if (!ViewModel.CanEditWithSelection) return;
        var tag = sender switch { Button button => button.Tag as string, MenuFlyoutItem item => item.Tag as string, _ => null };
        var file = ViewModel.SelectedFiles.First();
        if (tag == "Export")
        {
            var cover = ViewModel.GetCoverForExport(file);
            if (cover is null) { QueueMessage(LanguageManager.Get("ExportCoverImage"), LanguageManager.Get("CoverMissing", file.Name)); return; }
            var result = await CreateSavePicker(file.Name, cover.SuggestedExtension).PickSaveFileAsync();
            if (result is not null) await File.WriteAllBytesAsync(result.Path, cover.ImageData);
            return;
        }
        if (tag == "Open") { OpenPath(file.Path); return; }
        if (tag == "Locate") { LocatePath(file.Path); return; }
        ICommand? command = tag switch
        {
            "Cover" => ViewModel.SetCoverFromSelectedBookCommand,
            "Up" => ViewModel.MoveSelectedFilesUpCommand,
            "Down" => ViewModel.MoveSelectedFilesDownCommand,
            "Top" => ViewModel.MoveSelectedFilesToTopCommand,
            "Bottom" => ViewModel.MoveSelectedFilesToBottomCommand,
            "Remove" => ViewModel.RemoveSelectedFilesCommand,
            _ => null
        };
        _changingSelection = true;
        try { if (command?.CanExecute(null) == true) command.Execute(null); RestoreSelection(); }
        finally { _changingSelection = false; }
    });

    private void RestoreSelection()
    {
        var selection = ViewModel.SelectedFiles.ToArray();
        FilesList.SelectedItems.Clear();
        foreach (var file in selection) FilesList.SelectedItems.Add(file);
    }

    private async void Files_DoubleTapped(object sender, DoubleTappedRoutedEventArgs args) => await RunUiAsync(() =>
    {
        if (ViewModel.CanEditWithSelection) OpenPath(ViewModel.SelectedFiles.First().Path);
        return Task.CompletedTask;
    });

    private void Files_DragItemsStarting(object sender, DragItemsStartingEventArgs args)
    {
        args.Cancel = ViewModel.IsBusy;
        _draggedFiles = args.Items.Cast<BookFile>().ToArray();
        args.Data.RequestedOperation = DataPackageOperation.Move;
        args.Data.SetText("EpubMerge.InternalReorder");
    }

    private void Files_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args) => _draggedFiles = [];

    private void Files_DragOver(object sender, DragEventArgs args)
    {
        args.AcceptedOperation = ViewModel.IsBusy ? DataPackageOperation.None :
            _draggedFiles.Length > 0 ? DataPackageOperation.Move :
            args.DataView.Contains(StandardDataFormats.StorageItems) ? DataPackageOperation.Copy : DataPackageOperation.None;
        args.Handled = true;
    }

    private async void Files_Drop(object sender, DragEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            await RunUiAsync(async () =>
            {
                if (!ViewModel.CanEdit) return;
                if (_draggedFiles.Length > 0)
                {
                    var target = ViewModel.Files.Count;
                    var point = args.GetPosition(FilesList);
                    for (var index = 0; index < FilesList.Items.Count; index++)
                    {
                        if (FilesList.ContainerFromIndex(index) is not FrameworkElement container) continue;
                        var top = container.TransformToVisual(FilesList).TransformPoint(new Windows.Foundation.Point());
                        if (point.Y < top.Y + container.ActualHeight / 2) { target = index; break; }
                    }
                    _changingSelection = true;
                    try { ViewModel.ReorderFiles(_draggedFiles, target); RestoreSelection(); }
                    finally { _changingSelection = false; _draggedFiles = []; }
                }
                else if (args.DataView.Contains(StandardDataFormats.StorageItems))
                {
                    var items = await args.DataView.GetStorageItemsAsync();
                    ViewModel.AddFiles(items.Where(item => item.IsOfType(Windows.Storage.StorageItemTypes.Folder) ||
                        string.Equals(Path.GetExtension(item.Path), ".epub", StringComparison.OrdinalIgnoreCase)).Select(item => item.Path));
                }
            });
        }
        finally { args.Handled = true; deferral.Complete(); }
    }

    private void Cover_DragOver(object sender, DragEventArgs args)
    {
        args.AcceptedOperation = !ViewModel.IsBusy && args.DataView.Contains(StandardDataFormats.StorageItems)
            ? DataPackageOperation.Copy : DataPackageOperation.None;
        args.Handled = true;
    }

    private async void Cover_Drop(object sender, DragEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            await RunUiAsync(async () =>
            {
                if (!ViewModel.CanEdit || !args.DataView.Contains(StandardDataFormats.StorageItems)) return;
                var items = await args.DataView.GetStorageItemsAsync();
                var path = items.FirstOrDefault()?.Path;
                if (path is null) return;
                if (!ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                    throw new NotSupportedException(LanguageManager.Get("ImageFileFilter"));
                ViewModel.CoverPath = path;
            });
        }
        finally { args.Handled = true; deferral.Complete(); }
    }

    private static void OpenPath(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    private static void LocatePath(string path) => Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });

    private async void Merge_Click(object sender, RoutedEventArgs args) => await RunUiAsync(async () =>
    {
        if (ViewModel.IsBusy) { ViewModel.CancelMergeCommand.Execute(null); return; }
        var result = await ViewModel.MergeAsync();
        if (_closing) return;
        if (result.Succeeded)
        {
            var choice = await ShowDialogAsync(LanguageManager.Get("MergeCompletedTitle"),
                LanguageManager.Get("MergeCompleted", result.OutputPath!) + "\n\n" + LanguageManager.Get("OpenOutputPrompt"),
                LanguageManager.Get("OpenWithDefault"), secondary: LanguageManager.Get("LocateInExplorer"));
            if (choice == ContentDialogResult.Primary) OpenPath(result.OutputPath!);
            else if (choice == ContentDialogResult.Secondary) LocatePath(result.OutputPath!);
        }
        else if (!result.Canceled)
            QueueMessage(LanguageManager.Get("MergeFailed"), result.Error ?? LanguageManager.Get("MergeFailedRetry"));
    });

    private void History_Click(object sender, RoutedEventArgs args)
    {
        var menu = new MenuFlyout();
        if (ViewModel.RecentTasks.Count == 0)
            menu.Items.Add(new MenuFlyoutItem { Text = LanguageManager.Get("NoHistory"), IsEnabled = false });
        foreach (var task in ViewModel.RecentTasks)
        {
            var item = new MenuFlyoutItem { Text = task.DisplayName };
            item.Click += (_, _) =>
            {
                if (!ViewModel.CanEdit) return;
                _changingSelection = true;
                try { ViewModel.RestoreTask(task); RestoreSelection(); }
                finally { _changingSelection = false; }
            };
            menu.Items.Add(item);
        }
        menu.ShowAt((FrameworkElement)sender);
    }

    private void QueueMessage(string title, string message)
    {
        if (_closing) return;
        _messages.Enqueue((title, message));
        _ = DrainMessagesAsync();
    }

    private void Disclaimer_Click(object sender, RoutedEventArgs args) =>
        QueueMessage("Disclaimer",
            "EPUB Merger is provided \"as is\", without warranty of any kind, express or implied. " +
            "To the extent permitted by applicable law, the authors and copyright holders are not liable for claims, damages or other liability arising from its use.\n\n" +
            "Keep backups and verify merged files before relying on them. Compatibility with every EPUB file or reader is not guaranteed.\n\n" +
            "Only process content that you own or are authorized to use. You are responsible for complying with applicable copyright laws and license terms. " +
            "This application does not remove DRM or grant rights to third-party content.\n\n" +
            "See the MIT License in the project repository for the full license terms.");

    private async Task DrainMessagesAsync()
    {
        if (_dialogOpen || Root.XamlRoot is null || _closing) return;
        while (_messages.Count > 0 && !_closing)
        {
            var (title, message) = _messages.Dequeue();
            await ShowDialogAsync(title, message);
        }
    }

    private async Task<ContentDialogResult> ShowDialogAsync(string title, string message, string? primary = null, string? close = null, string? secondary = null)
    {
        if (_closing) return ContentDialogResult.None;
        // All dialogs are owned by this window and serialized with queued notifications.
        if (_dialogOpen) throw new InvalidOperationException("A dialog is already open.");
        _dialogOpen = true;
        try
        {
            return await new ContentDialog
            {
                XamlRoot = Root.XamlRoot,
                RequestedTheme = Root.RequestedTheme,
                Title = title,
                Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true },
                PrimaryButtonText = primary ?? string.Empty,
                SecondaryButtonText = secondary ?? string.Empty,
                CloseButtonText = close ?? LanguageManager.Get("Close")
            }.ShowAsync();
        }
        finally
        {
            _dialogOpen = false;
            if (_messages.Count > 0 && !_closing)
                DispatcherQueue.TryEnqueue(() => _ = DrainMessagesAsync());
        }
    }
}
