using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;

namespace EpubMerge.Core.Pure;

/// <summary>Controls how resolved EPUB paths are ordered.</summary>
public enum EpubSortMode
{
    /// <summary>Sorts file names by text and numeric segments.</summary>
    Natural,
    /// <summary>Sorts file names using case-insensitive ordinal comparison.</summary>
    Name,
    /// <summary>Preserves the order in which paths were discovered.</summary>
    None
}

/// <summary>Resolves EPUB files from explicit paths, directories, and wildcard patterns.</summary>
public static class EpubInputResolver
{
    /// <summary>Collects existing EPUB files and removes duplicate paths.</summary>
    /// <param name="paths">Files, directories, or wildcard paths supplied by the caller.</param>
    /// <param name="directory">An optional additional directory to scan.</param>
    /// <param name="recursive">Whether directory scans include subdirectories.</param>
    /// <param name="sort">The ordering applied to the resulting file names.</param>
    /// <returns>A distinct list of normalized EPUB paths.</returns>
    public static IReadOnlyList<string> Resolve(IEnumerable<string> paths, string? directory = null, bool recursive = false, EpubSortMode sort = EpubSortMode.Natural)
    {
        var candidates = new List<string>();
        foreach (var raw in paths ?? [])
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var path = raw.Trim().Trim('"');
            if (File.Exists(path)) candidates.Add(Path.GetFullPath(path));
            else if (Directory.Exists(path)) candidates.AddRange(ScanDirectory(path, recursive));
            else if (HasWildcard(path))
            {
                var parent = Path.GetDirectoryName(path);
                if (string.IsNullOrWhiteSpace(parent)) parent = Environment.CurrentDirectory;
                var pattern = Path.GetFileName(path);
                if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent))
                    candidates.AddRange(Directory.EnumerateFiles(parent, pattern, SearchOption.TopDirectoryOnly));
            }
        }
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory)) candidates.AddRange(ScanDirectory(directory, recursive));

        var result = candidates.Where(path => string.Equals(Path.GetExtension(path), ".epub", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        switch (sort)
        {
            case EpubSortMode.Natural:
                result.Sort((a, b) => NaturalStringComparer.Instance.Compare(Path.GetFileName(a), Path.GetFileName(b)));
                break;
            case EpubSortMode.Name:
                result.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(Path.GetFileName(a), Path.GetFileName(b)));
                break;
            case EpubSortMode.None:
            default:
                break;
        }
        return result;
    }

    private static IEnumerable<string> ScanDirectory(string path, bool recursive) =>
        Directory.EnumerateFiles(path, "*.epub", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
    private static bool HasWildcard(string value) => value.IndexOfAny(['*', '?']) >= 0;
}

/// <summary>Compares strings by their text and numeric segments, so <c>book2</c> precedes <c>book10</c>.</summary>
public sealed partial class NaturalStringComparer : IComparer<string>, IComparer
{
    /// <summary>Gets the shared comparer instance.</summary>
    public static NaturalStringComparer Instance { get; } = new();
    private static readonly Regex tokenizer = MyRegexSource();
    /// <summary>Compares two strings using case-insensitive natural ordering.</summary>
    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;
        var left = tokenizer.Split(x);
        var right = tokenizer.Split(y);
        for (var i = 0; i < Math.Min(left.Length, right.Length); i++)
        {
            var aIsNumber = long.TryParse(left[i], NumberStyles.None, CultureInfo.InvariantCulture, out var a);
            var bIsNumber = long.TryParse(right[i], NumberStyles.None, CultureInfo.InvariantCulture, out var b);
            var result = aIsNumber && bIsNumber ? a.CompareTo(b) : StringComparer.OrdinalIgnoreCase.Compare(left[i], right[i]);
            if (result != 0) return result;
        }
        return left.Length.CompareTo(right.Length);
    }
    int IComparer.Compare(object? x, object? y) => Compare(x as string, y as string);
    [GeneratedRegex(@"(\d+)", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex MyRegexSource();
}
