using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using CommunityToolkit.Mvvm.ComponentModel;
using EpubMerge.Core.Pure;

namespace EpubMerge.Gui;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IEpubMergeService _mergeService;
    private readonly IEpubCoverExtractor _coverExtractor;
    private CancellationTokenSource? _mergeCancellation;
    private string? _temporaryCoverPath;
    [ObservableProperty]
    public partial string CoverPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial ImageSource? CoverPreview { get; set; }
    [ObservableProperty]
    public partial string CoverPreviewMessage { get; set; } = "未选择封面图片";
    [ObservableProperty]
    public partial string CoverPreviewStatus { get; set; } = "可选择图片进行预览";
    [ObservableProperty]
    public partial string FileCountText { get; set; } = "还未添加文件";

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public bool CanEdit => !IsBusy;

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanEdit));

    [ObservableProperty]
    public partial double ProgressValue { get; set; }

    [ObservableProperty]
    public partial int ProgressCompleted { get; set; }

    [ObservableProperty]
    public partial int ProgressTotal { get; set; }

    [ObservableProperty]
    public partial string ProgressCountText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial TaskbarItemProgressState TaskbarProgressState { get; set; } = TaskbarItemProgressState.None;

    [ObservableProperty]
    public partial string OutputPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "请选择要合并的 EPUB 文件";

    [ObservableProperty]
    public partial string Title { get; set; } = "merged";

    public MainViewModel() : this(new EpubMergeService(), new EpubCoverExtractor()) { }
    public MainViewModel(IEpubMergeService mergeService, IEpubCoverExtractor? coverExtractor = null)
    {
        _mergeService = mergeService;
        _coverExtractor = coverExtractor ?? new EpubCoverExtractor();
        OutputPath = Path.Combine(TaskHistoryStore.LastOutputDirectory ?? Environment.CurrentDirectory, "merged.epub");
        foreach (var task in TaskHistoryStore.Load()) RecentTasks.Add(task);
    }

    public ObservableCollection<BookFile> Files { get; } = [];
    public ObservableCollection<RecentMergeTask> RecentTasks { get; } = [];

    public void RestoreTask(RecentMergeTask task)
    {
        Files.Clear();
        foreach (var path in task.InputPaths.Where(path => !string.IsNullOrWhiteSpace(path))) Files.Add(new BookFile(path));
        OutputPath = task.OutputPath;
        Title = task.Title;
        CoverPath = task.CoverPath ?? string.Empty;
        RefreshSelectionStatus();
        StatusMessage = "已恢复历史任务配置";
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
            CoverPreviewMessage = "未选择封面图片";
            CoverPreviewStatus = "可选择图片进行预览";
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
            CoverPreviewMessage = string.Empty;
            CoverPreviewStatus = $"已加载 {Path.GetExtension(path).TrimStart('.').ToUpperInvariant()} 图片";
        }
        catch (FileNotFoundException)
        {
            CoverPreviewMessage = "封面文件不存在\n请重新选择图片";
            CoverPreviewStatus = "无法读取封面路径";
        }
        catch
        {
            CoverPreviewMessage = "此图片暂时无法展示\n仍可作为封面参与合并";
            CoverPreviewStatus = "当前格式无法使用系统解码器预览";
        }
    }

    public void AddFiles(IEnumerable<string> paths)
    {
        var existing = Files.Select(file => Path.GetFullPath(file.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<string>();

        foreach (var path in paths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            var trimmed = path.Trim().Trim('"');
            if (Directory.Exists(trimmed) || trimmed.IndexOfAny(['*', '?']) >= 0)
                candidates.AddRange(EpubInputResolver.Resolve([trimmed], sort: EpubSortMode.Natural));
            else candidates.Add(Path.GetFullPath(trimmed));
        }

        foreach (var fullPath in candidates)
        {
            if (existing.Add(fullPath)) Files.Add(new BookFile(fullPath));
        }
        SortFilesNatural();
        RefreshSelectionStatus();
    }

    public bool ExtractCover(BookFile file)
    {
        try
        {
            var cover = _coverExtractor.ExtractCover(file.Path);
            if (cover is null)
            {
                StatusMessage = $"《{file.Name}》未包含有效封面图片";
                return false;
            }

            DeleteTemporaryCover();
            var directory = Path.Combine(Path.GetTempPath(), "EpubMerge");
            Directory.CreateDirectory(directory);
            _temporaryCoverPath = Path.Combine(directory, $"source-cover-{Guid.NewGuid():N}{cover.SuggestedExtension}");
            File.WriteAllBytes(_temporaryCoverPath, cover.ImageData);
            CoverPath = _temporaryCoverPath;
            StatusMessage = $"已从《{file.Name}》中提取封面";
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            StatusMessage = $"无法从《{file.Name}》读取封面：{exception.Message}";
            return false;
        }
    }

    public void CancelMerge()
    {
        if (!IsBusy) return;
        StatusMessage = "正在取消合并…";
        TaskbarProgressState = TaskbarItemProgressState.Paused;
        _mergeCancellation?.Cancel();
    }

    public void RemoveFiles(IEnumerable<BookFile> files)
    {
        foreach (var file in files.ToList()) Files.Remove(file);
        RefreshSelectionStatus();
    }

    public void ClearFiles()
    {
        Files.Clear();
        RefreshSelectionStatus();
    }

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

    public async Task<MergeUiResult> MergeAsync()
    {
        if (IsBusy) return new MergeUiResult(false, null, "已有合并任务正在运行");
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
            StatusMessage = "正在合并，请稍候…";
            var progress = new Progress<EpubMergeProgress>(value =>
            {
                var isReading = value.Message.StartsWith("正在读取", StringComparison.Ordinal);
                ProgressTotal = value.TotalBooks;
                ProgressCompleted = isReading
                    ? 0
                    : Math.Clamp(value.CompletedBooks, 0, value.TotalBooks);
                ProgressCountText = $"{ProgressCompleted} / {ProgressTotal}";
                ProgressValue = ProgressTotal <= 0 ? 0 : (double)ProgressCompleted / ProgressTotal;
                StatusMessage = value.Message;
            });
            await _mergeService.MergeAsync(request, progress, cancellation.Token);
            ProgressCompleted = ProgressTotal;
            ProgressValue = 1;
            ProgressCountText = $"{ProgressCompleted} / {ProgressTotal}";
            StatusMessage = $"合并完成：{request.OutputPath}";
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
            StatusMessage = "已取消合并";
            TaskbarProgressState = TaskbarItemProgressState.None;
            return new MergeUiResult(false, null, null, true);
        }
        catch (Exception exception)
        {
            StatusMessage = "合并失败";
            TaskbarProgressState = TaskbarItemProgressState.Error;
            return new MergeUiResult(false, null, exception.ToString());
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
        try { if (File.Exists(_temporaryCoverPath)) File.Delete(_temporaryCoverPath); } catch { }
        _temporaryCoverPath = null;
    }

    private void RefreshSelectionStatus()
    {
        FileCountText = Files.Count == 0 ? "还未添加文件" : $"{Files.Count} 本 EPUB";
        StatusMessage = Files.Count == 0 ? "请选择要合并的 EPUB 文件" : string.Empty;
    }
}

public sealed record BookFile(string Path)
{
    public string Name => System.IO.Path.GetFileName(Path);
}

public sealed record MergeUiResult(bool Succeeded, string? OutputPath, string? Error, bool Canceled = false);
