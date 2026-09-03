namespace EpubMerge.Core.Pure;

public sealed record EpubMergeRequest(
    IReadOnlyList<string> InputPaths,
    string OutputPath,
    string Title,
    string? CoverPath = null);

public sealed record EpubMergeProgress(int CompletedBooks, int TotalBooks, string Message);

public interface IEpubMergeService
{
    Task MergeAsync(
        EpubMergeRequest request,
        IProgress<EpubMergeProgress>? progress = null,
        CancellationToken cancellationToken = default(CancellationToken));
}

public static class EpubMergeValidator
{
    private static readonly HashSet<string> SupportedCoverExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".svg"
    };

    public static void Validate(EpubMergeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.InputPaths.Count == 0)
        {
            throw new ArgumentException("至少需要选择一个 EPUB 文件。", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.OutputPath))
        {
            throw new ArgumentException("请选择输出 EPUB 文件。", nameof(request));
        }

        if (!string.Equals(Path.GetExtension(request.OutputPath), ".epub", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("输出文件必须以 .epub 结尾。", nameof(request));
        }

        var outputPath = Path.GetFullPath(request.OutputPath);
        var pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        foreach (var path in request.InputPaths)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"找不到输入文件：{path}", path);
            }

            if (!string.Equals(Path.GetExtension(path), ".epub", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"不是 EPUB 文件：{path}", nameof(request));
            }

            if (string.Equals(Path.GetFullPath(path), outputPath, pathComparison))
            {
                throw new ArgumentException("输出 EPUB 不能覆盖输入 EPUB 文件。", nameof(request));
            }
        }

        if (!string.IsNullOrWhiteSpace(request.CoverPath))
        {
            if (!File.Exists(request.CoverPath))
            {
                throw new FileNotFoundException($"找不到封面文件：{request.CoverPath}", request.CoverPath);
            }

            if (!SupportedCoverExtensions.Contains(Path.GetExtension(request.CoverPath)))
            {
                throw new ArgumentException($"不支持的封面图片类型：{Path.GetExtension(request.CoverPath)}", nameof(request));
            }
        }
    }
}
