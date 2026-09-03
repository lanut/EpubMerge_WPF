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

    public MainViewModel() : this(new EpubMergeService()) { }
    public MainViewModel(IEpubMergeService mergeService)
    {
        _mergeService = mergeService;
        OutputPath = Path.Combine(Environment.CurrentDirectory, "merged.epub");
    }

    public ObservableCollection<BookFile> Files { get; } = [];

    partial void OnCoverPathChanged(string value)
    {
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

        foreach (var path in paths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            var fullPath = Path.GetFullPath(path);
            if (existing.Add(fullPath)) Files.Add(new BookFile(fullPath));
        }
        RefreshSelectionStatus();
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
        var indexes = selected.Select(Files.IndexOf).Where(index => index >= 0).Order().ToList();
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
        var request = new EpubMergeRequest(Files.Select(file => file.Path).ToList(), OutputPath.Trim(),
            string.IsNullOrWhiteSpace(Title) ? Path.GetFileNameWithoutExtension(OutputPath) : Title.Trim(),
            string.IsNullOrWhiteSpace(CoverPath) ? null : CoverPath.Trim());

        TaskbarProgressState = TaskbarItemProgressState.None;
        ProgressValue = 0;
        ProgressCompleted = 0;
        ProgressTotal = 0;
        ProgressCountText = string.Empty;

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
            await _mergeService.MergeAsync(request, progress);
            ProgressCompleted = ProgressTotal;
            ProgressValue = 1;
            ProgressCountText = $"{ProgressCompleted} / {ProgressTotal}";
            StatusMessage = $"合并完成：{request.OutputPath}";
            TaskbarProgressState = TaskbarItemProgressState.None;
            return new MergeUiResult(true, request.OutputPath, null);
        }
        catch (Exception exception)
        {
            StatusMessage = "合并失败";
            TaskbarProgressState = TaskbarItemProgressState.Error;
            return new MergeUiResult(false, null, exception.ToString());
        }
        finally { IsBusy = false; }
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

public sealed record MergeUiResult(bool Succeeded, string? OutputPath, string? Error);
