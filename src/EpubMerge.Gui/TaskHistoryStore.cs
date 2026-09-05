using System.IO;
using System.Text.Json;
using EpubMerge.Core.Pure;

namespace EpubMerge.Gui;

public sealed record RecentMergeTask(IReadOnlyList<string> InputPaths, string OutputPath, string Title, string? CoverPath)
{
    public string DisplayName => $"{Title}（{InputPaths.Count} 本）— {Path.GetFileName(OutputPath)}";
}

internal static class TaskHistoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static string DirectoryPath => Path.Combine(AppContext.BaseDirectory, "EpubMerge.Gui");
    private static string FilePath => Path.Combine(DirectoryPath, "history.json");

    public static string? LastOutputDirectory => Load().FirstOrDefault()?.OutputPath is { } path
        ? Path.GetDirectoryName(path)
        : null;

    public static IReadOnlyList<RecentMergeTask> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return [];
            return JsonSerializer.Deserialize<List<RecentMergeTask>>(File.ReadAllText(FilePath), JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static void Add(EpubMergeRequest request)
    {
        var history = Load().Where(item => !string.Equals(item.OutputPath, request.OutputPath, StringComparison.OrdinalIgnoreCase)).ToList();
        history.Insert(0, new RecentMergeTask(request.InputPaths.ToList(), request.OutputPath, request.Title, request.CoverPath));
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(history.Take(10).ToList(), JsonOptions));
        }
        catch
        {
        }
    }
}