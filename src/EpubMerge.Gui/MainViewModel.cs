using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EpubMerge.Core.Pure;

namespace EpubMerge.Gui;

/// <summary>Owns the merge screen state and coordinates user actions with the EPUB core services.</summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IEpubCoverExtractor _coverExtractor;
    private readonly IMergeHistoryStore _historyStore;
    private readonly IEpubMergeService _mergeService;
    private readonly ITemporaryCoverStore _temporaryCoverStore;
    private object[] _coverPreviewMessageArguments = [];
    private string? _coverPreviewMessageKey = "NoCoverSelected";
    private object[] _coverPreviewStatusArguments = [];
    private string? _coverPreviewStatusKey = "SelectImagePreview";
    private CancellationTokenSource? _mergeCancellation;
    private object[] _statusArguments = [];
    private string? _statusKey = "SelectFilesToMerge";
    private string? _temporaryCoverPath;

    /// <summary>Creates a view model with application services supplied by the composition root.</summary>
    /// <param name="mergeService">Service that performs the merge.</param>
    /// <param name="coverExtractor">Service that reads embedded covers.</param>
    /// <param name="historyStore">Persistence adapter for recent merge tasks.</param>
    /// <param name="temporaryCoverStore">Storage adapter for extracted cover previews.</param>
    public MainViewModel(
        IEpubMergeService mergeService,
        IEpubCoverExtractor coverExtractor,
        IMergeHistoryStore historyStore,
        ITemporaryCoverStore temporaryCoverStore)
    {
        _mergeService = mergeService;
        _coverExtractor = coverExtractor;
        _historyStore = historyStore;
        _temporaryCoverStore = temporaryCoverStore;
        LanguageManager.CultureChanged += OnCultureChanged;
        OutputPath = Path.Combine(_historyStore.LastOutputDirectory ?? Environment.CurrentDirectory, "merged.epub");
        foreach (var task in _historyStore.Load()) RecentTasks.Add(task);
    }

#if DEBUG
    /// <summary>
    /// Only for debugging design
    /// </summary>
    public MainViewModel() : this(new EpubMergeService(),
        new EpubCoverExtractor(),
        new FileMergeHistoryStore(),
        new FileTemporaryCoverStore())
    { }
#endif

    /// <summary>Gets or sets the selected external or extracted cover path.</summary>
    [ObservableProperty]
    public partial string CoverPath { get; set; } = string.Empty;

    /// <summary>Gets or sets the image path shown by the cover preview.</summary>
    [ObservableProperty]
    public partial string? CoverPreviewPath { get; set; }

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

    /// <summary>Gets or sets the localized merge/cancel button text.</summary>
    [ObservableProperty]
    public partial string MergeButtonText { get; set; } = LanguageManager.Get("StartMerge");

    /// <summary>Gets or sets whether a merge is currently running.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyPropertyChangedFor(nameof(CanEditWithSelection))]
    [NotifyCanExecuteChangedFor(nameof(ClearFilesCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearCoverCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveSelectedFilesCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveSelectedFilesUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveSelectedFilesDownCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveSelectedFilesToTopCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveSelectedFilesToBottomCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetCoverFromSelectedBookCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelMergeCommand))]
    public partial bool IsBusy { get; set; }

    /// <summary>Gets whether input controls should remain editable.</summary>
    public bool CanEdit => !IsBusy;

    /// <summary>Gets whether commands that operate on selected books can run.</summary>
    public bool CanEditWithSelection => CanEdit && SelectedFiles.Count > 0;

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

    /// <summary>Gets or sets the presentation-neutral merge progress state.</summary>
    [ObservableProperty]
    public partial MergeProgressState ProgressState { get; set; } = MergeProgressState.None;

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
    /// <summary>Gets the current view-provided selection, independent of any WPF control.</summary>
    public ObservableCollection<BookFile> SelectedFiles { get; } = [];
    /// <summary>Gets persisted merge tasks available for restoration.</summary>
    public ObservableCollection<RecentMergeTask> RecentTasks { get; } = [];

    partial void OnIsBusyChanged(bool value)
    {
        MergeButtonText = LanguageManager.Get(value ? "Cancel" : "StartMerge");
    }

    /// <summary>Replaces the current selection with items extracted by the view.</summary>
    public void SetSelectedFiles(IEnumerable<BookFile> files)
    {
        SelectedFiles.Clear();
        foreach (var file in files.Where(Files.Contains).Distinct()) SelectedFiles.Add(file);
        OnPropertyChanged(nameof(CanEditWithSelection));
        NotifySelectionCommandsCanExecuteChanged();
    }

    /// <summary>Restores input and output settings from a saved merge task.</summary>
    /// <param name="task">The saved merge task to restore.</param>
    public void RestoreTask(RecentMergeTask task)
    {
        Files.Clear();
        foreach (var path in task.InputPaths.Where(path => !string.IsNullOrWhiteSpace(path))) Files.Add(new BookFile(path));
        SetSelectedFiles([]);
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
        CoverPreviewPath = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            SetCoverPreview("NoCoverSelected", "SelectImagePreview");
            return;
        }
        if (!File.Exists(path))
        {
            SetCoverPreview("CoverFileMissing", "CannotReadCoverPath");
            return;
        }
        CoverPreviewPath = Path.GetFullPath(path);
        SetCoverPreview(null, "LoadedImage", Path.GetExtension(path).TrimStart('.').ToUpperInvariant());
    }

    /// <summary>Adds files, directories, or wildcard paths and applies natural filename ordering.</summary>
    public void AddFiles(IEnumerable<string> paths)
    {
        if (!CanEdit) return;
        var existing = Files.Select(file => Path.GetFullPath(file.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<string>();
        foreach (var path in paths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            var trimmed = path.Trim().Trim('"');
            candidates.AddRange(Directory.Exists(trimmed) || trimmed.IndexOfAny(['*', '?']) >= 0
                ? EpubInputResolver.Resolve([trimmed], sort: EpubSortMode.Natural)
                : [Path.GetFullPath(trimmed)]);
        }
        foreach (var fullPath in candidates)
        {
            if (existing.Add(fullPath)) Files.Add(new BookFile(fullPath));
        }
        SortFilesNatural();
        RefreshSelectionStatus();
    }

    /// <summary>Returns a selected book's embedded cover for a view-managed export operation.</summary>
    /// <param name="file">The source EPUB whose cover should be read.</param>
    /// <returns>The extracted cover, or <see langword="null" /> when it is unavailable.</returns>
    public ExtractedCover? GetCoverForExport(BookFile file)
    {
        try { return _coverExtractor.ExtractCover(file.Path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            SetStatus("CoverReadFailed", file.Name, LanguageManager.Get("CoverReadDetails"));
            return null;
        }
    }

    /// <summary>Clears all selected source files.</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void ClearFiles()
    {
        Files.Clear();
        SetSelectedFiles([]);
        RefreshSelectionStatus();
    }

    /// <summary>Clears the selected cover path and preview.</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void ClearCover() => CoverPath = string.Empty;

    /// <summary>Removes the currently selected source files from the merge list.</summary>
    [RelayCommand(CanExecute = nameof(CanEditWithSelection))]
    private void RemoveSelectedFiles()
    {
        foreach (var file in SelectedFiles.ToList()) Files.Remove(file);
        SetSelectedFiles([]);
        RefreshSelectionStatus();
    }

    /// <summary>Moves the selected source files one position toward the beginning.</summary>
    [RelayCommand(CanExecute = nameof(CanEditWithSelection))]
    private void MoveSelectedFilesUp() => MoveSelectedFiles(-1);

    /// <summary>Moves the selected source files one position toward the end.</summary>
    [RelayCommand(CanExecute = nameof(CanEditWithSelection))]
    private void MoveSelectedFilesDown() => MoveSelectedFiles(1);

    /// <summary>Moves the selected source files to the beginning of the merge list.</summary>
    [RelayCommand(CanExecute = nameof(CanEditWithSelection))]
    private void MoveSelectedFilesToTop() => MoveSelectedFiles(int.MinValue);

    /// <summary>Moves the selected source files to the end of the merge list.</summary>
    [RelayCommand(CanExecute = nameof(CanEditWithSelection))]
    private void MoveSelectedFilesToBottom() => MoveSelectedFiles(int.MaxValue);

    /// <summary>Extracts the selected book's cover and uses it as the merge cover.</summary>
    [RelayCommand(CanExecute = nameof(CanEditWithSelection))]
    private void SetCoverFromSelectedBook()
    {
        var file = SelectedFiles.FirstOrDefault();
        if (file is null) return;
        var cover = GetCoverForExport(file);
        if (cover is null)
        {
            SetStatus("CoverMissing", file.Name);
            return;
        }
        DeleteTemporaryCover();
        _temporaryCoverPath = _temporaryCoverStore.Save(cover);
        CoverPath = _temporaryCoverPath;
        SetStatus("CoverExtracted", file.Name);
    }

    /// <summary>Requests cancellation of the active merge.</summary>
    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void CancelMerge()
    {
        SetStatus("Cancelling");
        ProgressState = MergeProgressState.Paused;
        _mergeCancellation?.Cancel();
    }

    /// <summary>Validates the current state, executes the merge, and converts its outcome to UI data.</summary>
    /// <returns>A result suitable for status messages and view-managed dialogs.</returns>
    public async Task<MergeUiResult> MergeAsync()
    {
        if (IsBusy) return new MergeUiResult(false, null, LanguageManager.Get("MergeAlreadyRunning"));
        var request = new EpubMergeRequest(Files.Select(file => file.Path).ToList(), OutputPath.Trim(),
            string.IsNullOrWhiteSpace(Title) ? Path.GetFileNameWithoutExtension(OutputPath) : Title.Trim(),
            string.IsNullOrWhiteSpace(CoverPath) ? null : CoverPath.Trim());
        ProgressState = MergeProgressState.None;
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
            ProgressState = MergeProgressState.Normal;
            SetStatus("Merging");
            await _mergeService.MergeAsync(request, new Progress<EpubMergeProgress>(UpdateProgress), cancellation.Token);
            ProgressCompleted = ProgressTotal;
            ProgressValue = 1;
            ProgressCountText = $"{ProgressCompleted} / {ProgressTotal}";
            SetStatus("MergeCompleted", request.OutputPath);
            ProgressState = MergeProgressState.None;
            _historyStore.Add(request);
            ReloadRecentTasks();
            return new MergeUiResult(true, request.OutputPath, null);
        }
        catch (OperationCanceledException)
        {
            ProgressValue = 0;
            ProgressCompleted = 0;
            ProgressCountText = string.Empty;
            SetStatus("MergeCanceled");
            ProgressState = MergeProgressState.None;
            return new MergeUiResult(false, null, null, true);
        }
        catch (Exception exception)
        {
            SetStatus("MergeFailed");
            ProgressState = MergeProgressState.Error;
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

    private void UpdateProgress(EpubMergeProgress value)
    {
        var isReading = value.Stage == EpubMergeProgressStage.Reading;
        ProgressTotal = value.TotalBooks;
        ProgressCompleted = isReading ? 0 : Math.Clamp(value.CompletedBooks, 0, value.TotalBooks);
        ProgressCountText = $"{ProgressCompleted} / {ProgressTotal}";
        ProgressValue = ProgressTotal <= 0 ? 0 : (double)ProgressCompleted / ProgressTotal;
        SetStatus(isReading ? "ReadingBook" : "MergedBooks", isReading ? value.CompletedBooks + 1 : value.CompletedBooks, value.TotalBooks);
    }

    private void MoveSelectedFiles(int direction)
    {
        var selectedFiles = SelectedFiles.Where(Files.Contains).Distinct().ToList();
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
            if (destination >= 0 && destination < Files.Count) Files.Move(index, destination);
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
        _temporaryCoverStore.Delete(_temporaryCoverPath);
        _temporaryCoverPath = null;
    }

    private void ReloadRecentTasks()
    {
        RecentTasks.Clear();
        foreach (var task in _historyStore.Load()) RecentTasks.Add(task);
    }

    private void RefreshSelectionStatus()
    {
        FileCountText = Files.Count == 0 ? LanguageManager.Get("NoFiles") : LanguageManager.Get("FileCount", Files.Count);
        SetStatus(Files.Count == 0 ? "SelectFilesToMerge" : null);
    }

    private void NotifySelectionCommandsCanExecuteChanged()
    {
        RemoveSelectedFilesCommand.NotifyCanExecuteChanged();
        MoveSelectedFilesUpCommand.NotifyCanExecuteChanged();
        MoveSelectedFilesDownCommand.NotifyCanExecuteChanged();
        MoveSelectedFilesToTopCommand.NotifyCanExecuteChanged();
        MoveSelectedFilesToBottomCommand.NotifyCanExecuteChanged();
        SetCoverFromSelectedBookCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedLanguageChanged(string value) => LanguageManager.SetCulture(value);

    private void OnCultureChanged(object? sender, EventArgs e)
    {
        CoverPreviewMessage = _coverPreviewMessageKey is null ? string.Empty : LanguageManager.Get(_coverPreviewMessageKey, _coverPreviewMessageArguments);
        CoverPreviewStatus = _coverPreviewStatusKey is null ? string.Empty : LanguageManager.Get(_coverPreviewStatusKey, _coverPreviewStatusArguments);
        RefreshSelectionStatus();
        StatusMessage = _statusKey is null ? string.Empty : LanguageManager.Get(_statusKey, _statusArguments);
        MergeButtonText = LanguageManager.Get(IsBusy ? "Cancel" : "StartMerge");
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

    /// <summary>Releases language and temporary-resource subscriptions.</summary>
    public void Dispose()
    {
        LanguageManager.CultureChanged -= OnCultureChanged;
        _mergeCancellation?.Cancel();
        DeleteTemporaryCover();
    }
}

/// <summary>Represents the display-neutral state of a merge operation.</summary>
public enum MergeProgressState
{
    /// <summary>No active merge progress.</summary>
    None,
    /// <summary>A merge is running.</summary>
    Normal,
    /// <summary>A merge cancellation has been requested.</summary>
    Paused,
    /// <summary>The merge failed.</summary>
    Error
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
