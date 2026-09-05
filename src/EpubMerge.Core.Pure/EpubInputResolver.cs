using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;

namespace EpubMerge.Core.Pure;

public enum EpubSortMode { Natural, Name, None }

public static class EpubInputResolver
{
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
        if (sort == EpubSortMode.Natural) result.Sort((a, b) => NaturalStringComparer.Instance.Compare(Path.GetFileName(a), Path.GetFileName(b)));
        else if (sort == EpubSortMode.Name) result.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(Path.GetFileName(a), Path.GetFileName(b)));
        return result;
    }

    private static IEnumerable<string> ScanDirectory(string path, bool recursive) =>
        Directory.EnumerateFiles(path, "*.epub", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
    private static bool HasWildcard(string value) => value.IndexOfAny(['*', '?']) >= 0;
}

public sealed partial class NaturalStringComparer : IComparer<string>, IComparer
{
    public static NaturalStringComparer Instance { get; } = new();
    private static readonly Regex Tokenizer = MyRegexSource();
    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;
        var left = Tokenizer.Split(x);
        var right = Tokenizer.Split(y);
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
