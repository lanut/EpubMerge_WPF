using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using CommunityToolkit.Mvvm.ComponentModel;
using EpubMerge.Core.Pure;

namespace EpubMerge.Gui;

/// <summary>Owns the merge screen state and coordinates user actions with the EPUB core services.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly IEpubCoverExtractor _coverExtractor;
    private readonly IEpubMergeService _mergeService;
    private object[] _coverPreviewMessageArguments = [];
    private string? _coverPreviewMessageKey = "NoCoverSelected";
    private object[] _coverPreviewStatusArguments = [];
    private string? _coverPreviewStatusKey = "SelectImagePreview";
    private CancellationTokenSource? _mergeCancellation;
    private object[] _statusArguments = [];
    private string? _statusKey = "SelectFilesToMerge";
    private string? _temporaryCoverPath;

    /// <summary>Creates a view model backed by the production EPUB services.</summary>
    public MainViewModel() : this(new EpubMergeService(), new EpubCoverExtractor()) { }

    /// <summary>Creates a view model with injectable services for UI composition and tests.</summary>
    /// <param name="mergeService">Service that performs the merge.</param>
    /// <param name="coverExtractor">Optional service that reads embedded covers.</param>
    public MainViewModel(IEpubMergeService mergeService, IEpubCoverExtractor? coverExtractor = null)
    {
        _mergeService = mergeService;
        _coverExtractor = coverExtractor ?? new EpubCoverExtractor();
        LanguageManager.CultureChanged += OnCultureChanged;
        OutputPath = Path.Combine(TaskHistoryStore.LastOutputDirectory ?? Environment.CurrentDirectory, "merged.epub");
        foreach (var task in TaskHistoryStore.Load()) RecentTasks.Add(task);
    }

    /// <summary>Gets or sets the selected external or extracted cover path.</summary>
    [ObservableProperty]
    public partial string CoverPath { get; set; } = string.Empty;

    /// <summary>Gets or sets the decoded image shown in the cover preview.</summary>
    [ObservableProperty]
    public partial ImageSource? CoverPreview { get; set; }

    /// <summary>Gets or sets the localized cover preview message.</summary>
    [ObservableProperty]
    public partial string CoverPreviewMessage { get; set; } = LanguageManager.Get("NoCoverSelected");

    /// <summary>Gets or sets the localized cover preview status.</summary>
    [ObservableProperty]
    public partial string CoverPreviewStatus { get; set; } = LanguageManager.Get("SelectImagePreview");

    /// <summary>Gets or sets the localized number-of-files label.</summary>
    [ObservableProperty]
    public partial string FileCountText { get; set; } = LanguageManager.Get("NoFiles");

    /// <summary>Gets or sets the selected UI culture name.</summary>
    [ObservableProperty]
    public partial string SelectedLanguage { get; set; } = LanguageManager.CurrentCulture.Name;

    /// <summary>Gets or sets whether a merge is currently running.</summary>
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>Gets whether input controls should remain editable.</summary>
    public bool CanEdit => !IsBusy;

    /// <summary>Gets or sets the progress percentage represented by the progress bar.</summary>
    [ObservableProperty]
    public partial double ProgressValue { get; set; }

    /// <summary>Gets or sets the number of completed source books.</summary>
    [ObservableProperty]
    public partial int ProgressCompleted { get; set; }

    /// <summary>Gets or sets the total number of source books.</summary>
    [ObservableProperty]
    public partial int ProgressTotal { get; set; }

    /// <summary>Gets or sets the localized progress count label.</summary>
    [ObservableProperty]
    public partial string ProgressCountText { get; set; } = string.Empty;

    /// <summary>Gets or sets the state displayed by the Windows taskbar button.</summary>
    [ObservableProperty]
    public partial TaskbarItemProgressState TaskbarProgressState { get; set; } = TaskbarItemProgressState.None;

    /// <summary>Gets or sets the destination EPUB path.</summary>
    [ObservableProperty]
    public partial string OutputPath { get; set; } = string.Empty;

    /// <summary>Gets or sets the localized operation status.</summary>
    [ObservableProperty]
    public partial string StatusMessage { get; set; } = LanguageManager.Get("SelectFilesToMerge");

    /// <summary>Gets or sets the merged EPUB title.</summary>
    [ObservableProperty]
    public partial string Title { get; set; } = "merged";

    /// <summary>Gets the source EPUB files in their intended merge order.</summary>
    public ObservableCollection<BookFile> Files { get; } = [];

    /// <summary>Gets persisted merge tasks available for restoration.</summary>
    public ObservableCollection<RecentMergeTask> RecentTasks { get; } = [];

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEdit));
    }

    /// <summary>Restores input and output settings from a saved merge task.</summary>
    public void RestoreTask(RecentMergeTask task)
    {
        Files.Clear();
        foreach (var path in task.InputPaths.Where(path => !string.IsNullOrWhiteSpace(path))) Files.Add(new BookFile(path));
        OutputPath = task.OutputPath;
        Title = task.Title;
        CoverPath = task.CoverPath ?? string.Empty;
        RefreshSelectionStatus();
        SetStatus("RestoredTask");
    }

    partial void OnCoverPathChanged(string value)
    {
        if (!string.Equals(_temporaryCoverPath, value, StringComparison.OrdinalIgnoreCase)) DeleteTemporaryCover();
        UpdateCoverPreview(value);
    }

    private void UpdateCoverPreview(string path)
    {
        CoverPreview = null;

        if (string.IsNullOrWhiteSpace(path))
        {
            SetCoverPreview("NoCoverSelected", "SelectImagePreview");
            return;
        }

        try
        {
            if (!File.Exists(path)) throw new FileNotFoundException();
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(Path.GetFullPath(path));
            image.EndInit();
            image.Freeze();
            CoverPreview = image;
            SetCoverPreview(null, "LoadedImage", Path.GetExtension(path).TrimStart('.').ToUpperInvariant());
        }
        catch (FileNotFoundException)
        {
            SetCoverPreview("CoverFileMissing", "CannotReadCoverPath");
        }
        catch
        {
            SetCoverPreview("CoverCannotDisplay", "UnsupportedPreviewFormat");
        }
    }

    /// <summary>Adds files, directories, or wildcard paths and applies natural filename ordering.</summary>
    public void AddFiles(IEnumerable<string> paths)
    {
        var existing = Files.Select(file => Path.GetFullPath(file.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<string>();

        foreach (var path in paths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            var trimmed = path.Trim().Trim('"');

            if (Directory.Exists(trimmed) || trimmed.IndexOfAny(['*', '?']) >= 0)
            {
                candidates.AddRange(EpubInputResolver.Resolve([trimmed], sort: EpubSortMode.Natural));
            }
            else
            {
                candidates.Add(Path.GetFullPath(trimmed));
            }
        }

        foreach (var fullPath in candidates)
        {
            if (existing.Add(fullPath)) Files.Add(new BookFile(fullPath));
        }
        SortFilesNatural();
        RefreshSelectionStatus();
    }

    /// <summary>Extracts a selected book's embedded cover into a temporary image file.</summary>
    /// <returns><see langword="true" /> when a cover was extracted and selected.</returns>
    public bool ExtractCover(BookFile file)
    {
        try
        {
            var cover = _coverExtractor.ExtractCover(file.Path);

            if (cover is null)
            {
                SetStatus("CoverMissing", file.Name);
                return false;
            }

            DeleteTemporaryCover();
            // Core APIs return bytes; a temporary file lets the existing cover pipeline and WPF image preview share one path.
            var directory = Path.Combine(Path.GetTempPath(), "EpubMerge");
            Directory.CreateDirectory(directory);
            _temporaryCoverPath = Path.Combine(directory, $"source-cover-{Guid.NewGuid():N}{cover.SuggestedExtension}");
            File.WriteAllBytes(_temporaryCoverPath, cover.ImageData);
            CoverPath = _temporaryCoverPath;
            SetStatus("CoverExtracted", file.Name);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            SetStatus("CoverReadFailed", file.Name, LanguageManager.Get("CoverReadDetails"));
            return false;
        }
    }

    /// <summary>Requests cancellation of the active merge, if any.</summary>
    public void CancelMerge()
    {
        if (!IsBusy) return;
        SetStatus("Cancelling");
        TaskbarProgressState = TaskbarItemProgressState.Paused;
        _mergeCancellation?.Cancel();
    }

    /// <summary>Removes the specified source files from the merge list.</summary>
    public void RemoveFiles(IEnumerable<BookFile> files)
    {
        foreach (var file in files.ToList()) Files.Remove(file);
        RefreshSelectionStatus();
    }

    /// <summary>Clears all selected source files.</summary>
    public void ClearFiles()
    {
        Files.Clear();
        RefreshSelectionStatus();
    }

    /// <summary>Moves selected files by one position or to an end of the merge list.</summary>
    /// <param name="selected">Files to move.</param>
    /// <param name="direction">Negative for up/top and positive for down/bottom.</param>
    public void MoveFiles(IEnumerable<BookFile> selected, int direction)
    {
        var selectedFiles = selected.Where(Files.Contains).Distinct().ToList();

        if (direction == int.MinValue)
        {
            foreach (var file in selectedFiles.AsEnumerable().Reverse()) Files.Move(Files.IndexOf(file), 0);
            return;
        }

        if (direction == int.MaxValue)
        {
            foreach (var file in selectedFiles) Files.Move(Files.IndexOf(file), Files.Count - 1);
            return;
        }

        var indexes = selectedFiles.Select(Files.IndexOf).Where(index => index >= 0).Order().ToList();
        if (direction > 0) indexes.Reverse();

        foreach (var index in indexes)
        {
            var destination = index + direction;
            if (destination < 0 || destination >= Files.Count) continue;
            Files.Move(index, destination);
        }
    }

    /// <summary>Validates the current state, executes the merge, and converts its outcome to UI data.</summary>
    /// <returns>A result suitable for status messages and dialogs.</returns>
    public async Task<MergeUiResult> MergeAsync()
    {
        if (IsBusy) return new MergeUiResult(false, null, LanguageManager.Get("MergeAlreadyRunning"));
        var request = new EpubMergeRequest(Files.Select(file => file.Path).ToList(), OutputPath.Trim(),
            string.IsNullOrWhiteSpace(Title) ? Path.GetFileNameWithoutExtension(OutputPath) : Title.Trim(),
            string.IsNullOrWhiteSpace(CoverPath) ? null : CoverPath.Trim());

        TaskbarProgressState = TaskbarItemProgressState.None;
        ProgressValue = 0;
        ProgressCompleted = 0;
        ProgressTotal = 0;
        ProgressCountText = string.Empty;

        using var cancellation = new CancellationTokenSource();
        _mergeCancellation = cancellation;

        try
        {
            EpubMergeValidator.Validate(request);
            IsBusy = true;
            ProgressTotal = request.InputPaths.Count;
            ProgressCountText = $"0 / {ProgressTotal}";
            TaskbarProgressState = TaskbarItemProgressState.Normal;
            SetStatus("Merging");
            // Progress<T> posts through the WPF synchronization context, keeping bound properties on the UI thread.
            var progress = new Progress<EpubMergeProgress>(value =>
            {
                var isReading = value.Message.StartsWith("正在读取", StringComparison.Ordinal);
                ProgressTotal = value.TotalBooks;
                ProgressCompleted = isReading
                    ? 0
                    : Math.Clamp(value.CompletedBooks, 0, value.TotalBooks);
                ProgressCountText = $"{ProgressCompleted} / {ProgressTotal}";
                ProgressValue = ProgressTotal <= 0 ? 0 : (double)ProgressCompleted / ProgressTotal;
                SetStatus(isReading ? "ReadingBook" : "MergedBooks",
                    isReading ? value.CompletedBooks + 1 : value.CompletedBooks, value.TotalBooks);
            });
            await _mergeService.MergeAsync(request, progress, cancellation.Token);
            ProgressCompleted = ProgressTotal;
            ProgressValue = 1;
            ProgressCountText = $"{ProgressCompleted} / {ProgressTotal}";
            SetStatus("MergeCompleted", request.OutputPath);
            TaskbarProgressState = TaskbarItemProgressState.None;
            TaskHistoryStore.Add(request);
            RecentTasks.Clear();
            foreach (var task in TaskHistoryStore.Load()) RecentTasks.Add(task);
            return new MergeUiResult(true, request.OutputPath, null);
        }
        catch (OperationCanceledException)
        {
            ProgressValue = 0;
            ProgressCompleted = 0;
            ProgressCountText = string.Empty;
            SetStatus("MergeCanceled");
            TaskbarProgressState = TaskbarItemProgressState.None;
            return new MergeUiResult(false, null, null, true);
        }
        catch (Exception exception)
        {
            SetStatus("MergeFailed");
            TaskbarProgressState = TaskbarItemProgressState.Error;
            var logPath = MergeErrorLogger.Write(request, exception);
            var error = MergeErrorFormatter.Format(exception);
            if (logPath is not null) error += "\n\n" + LanguageManager.Get("ErrorLogSaved", logPath);
            return new MergeUiResult(false, null, error);
        }
        finally
        {
            _mergeCancellation = null;
            IsBusy = false;
        }
    }

    private void SortFilesNatural()
    {
        var sorted = Files.OrderBy(file => file.Name, NaturalStringComparer.Instance).ToList();

        for (var index = 0; index < sorted.Count; index++)
        {
            var currentIndex = Files.IndexOf(sorted[index]);
            if (currentIndex != index) Files.Move(currentIndex, index);
        }
    }

    private void DeleteTemporaryCover()
    {
        if (_temporaryCoverPath is null) return;

        try
        {
            if (File.Exists(_temporaryCoverPath)) File.Delete(_temporaryCoverPath);
        }
        catch { }
        _temporaryCoverPath = null;
    }

    private void RefreshSelectionStatus()
    {
        FileCountText = Files.Count == 0 ? LanguageManager.Get("NoFiles") : LanguageManager.Get("FileCount", Files.Count);
        SetStatus(Files.Count == 0 ? "SelectFilesToMerge" : null);
    }

    partial void OnSelectedLanguageChanged(string value)
    {
        LanguageManager.SetCulture(value);
    }

    private void OnCultureChanged(object? sender, EventArgs e)
    {
        CoverPreviewMessage = _coverPreviewMessageKey is null ? string.Empty : LanguageManager.Get(_coverPreviewMessageKey, _coverPreviewMessageArguments);
        CoverPreviewStatus = _coverPreviewStatusKey is null ? string.Empty : LanguageManager.Get(_coverPreviewStatusKey, _coverPreviewStatusArguments);
        RefreshSelectionStatus();
        StatusMessage = _statusKey is null ? string.Empty : LanguageManager.Get(_statusKey, _statusArguments);
        OnPropertyChanged(nameof(SelectedLanguage));
    }

    private void SetStatus(string? key, params object[] arguments)
    {
        _statusKey = key;
        _statusArguments = arguments;
        StatusMessage = key is null ? string.Empty : LanguageManager.Get(key, arguments);
    }

    private void SetCoverPreview(string? messageKey, string? statusKey, params object[] statusArguments)
    {
        _coverPreviewMessageKey = messageKey;
        _coverPreviewMessageArguments = [];
        _coverPreviewStatusKey = statusKey;
        _coverPreviewStatusArguments = statusArguments;
        CoverPreviewMessage = messageKey is null ? string.Empty : LanguageManager.Get(messageKey);
        CoverPreviewStatus = statusKey is null ? string.Empty : LanguageManager.Get(statusKey, statusArguments);
    }
}

/// <summary>Represents one EPUB selected in the merge list.</summary>
/// <param name="Path">The full path to the EPUB file.</param>
public sealed record BookFile(string Path)
{
    /// <summary>Gets the display file name.</summary>
    public string Name => System.IO.Path.GetFileName(Path);
}

/// <summary>Contains the presentation-level outcome of a merge request.</summary>
/// <param name="Succeeded">Whether the output was created.</param>
/// <param name="OutputPath">The created output path on success.</param>
/// <param name="Error">A user-readable error on failure.</param>
/// <param name="Canceled">Whether cancellation, rather than failure, ended the operation.</param>
public sealed record MergeUiResult(bool Succeeded, string? OutputPath, string? Error, bool Canceled = false);
