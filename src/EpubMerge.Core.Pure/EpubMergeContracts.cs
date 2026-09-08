namespace EpubMerge.Core.Pure;

/// <summary>Describes the source EPUBs and metadata used for a merge operation.</summary>
/// <param name="InputPaths">The EPUB files to merge, in reading order.</param>
/// <param name="OutputPath">The destination EPUB path.</param>
/// <param name="Title">The title written to the merged package metadata and navigation.</param>
/// <param name="CoverPath">An optional external cover image path.</param>
public sealed record EpubMergeRequest(
    IReadOnlyList<string> InputPaths,
    string OutputPath,
    string Title,
    string? CoverPath = null);

/// <summary>Identifies the current stage of a merge operation.</summary>
public enum EpubMergeProgressStage
{
    /// <summary>The source EPUBs are being read and parsed.</summary>
    Reading,
    /// <summary>The merged EPUB is being written.</summary>
    Merging
}

/// <summary>Reports the number of source books processed by a merge operation.</summary>
/// <param name="CompletedBooks">The number of completed books.</param>
/// <param name="TotalBooks">The total number of books in the operation.</param>
/// <param name="Message">A localized, human-readable progress message.</param>
/// <param name="Stage">The stable, presentation-neutral stage identifier.</param>
public sealed record EpubMergeProgress(
    int CompletedBooks,
    int TotalBooks,
    string Message,
    EpubMergeProgressStage Stage = EpubMergeProgressStage.Merging);

/// <summary>Provides asynchronous EPUB merging with cancellation and progress reporting.</summary>
public interface IEpubMergeService
{
    /// <summary>Merges the requested EPUBs into a new EPUB file.</summary>
    /// <param name="request">The merge input and output configuration.</param>
    /// <param name="progress">Optional progress sink.</param>
    /// <param name="cancellationToken">Token used to cancel reading or writing.</param>
    /// <returns>A task that completes after the output has been written.</returns>
    Task MergeAsync(
        EpubMergeRequest request,
        IProgress<EpubMergeProgress>? progress = null,
        CancellationToken cancellationToken = default(CancellationToken));
}

/// <summary>Validates merge requests before any output is created.</summary>
public static class EpubMergeValidator
{
    private static readonly HashSet<string> supportedCoverExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".svg"
    };

    /// <summary>Checks paths, extensions, collisions, and optional cover files.</summary>
    /// <param name="request">The request to validate.</param>
    /// <exception cref="ArgumentNullException">The request is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">A request value is invalid.</exception>
    /// <exception cref="FileNotFoundException">An input or cover file does not exist.</exception>
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

        // ReSharper disable once InvertIf
        if (!string.IsNullOrWhiteSpace(request.CoverPath))
        {
            if (!File.Exists(request.CoverPath))
            {
                throw new FileNotFoundException($"找不到封面文件：{request.CoverPath}", request.CoverPath);
            }

            if (!supportedCoverExtensions.Contains(Path.GetExtension(request.CoverPath)))
            {
                throw new ArgumentException($"不支持的封面图片类型：{Path.GetExtension(request.CoverPath)}", nameof(request));
            }
        }
    }
}
