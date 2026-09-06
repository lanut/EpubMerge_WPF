using System.IO;
using System.Text.Json;
using EpubMerge.Core.Pure;

namespace EpubMerge.Gui;

/// <summary>Captures the settings required to restore a previous merge.</summary>
/// <param name="InputPaths">Source EPUB paths in merge order.</param>
/// <param name="OutputPath">The saved output path.</param>
/// <param name="Title">The saved merged-book title.</param>
/// <param name="CoverPath">The optional external cover path.</param>
public sealed record RecentMergeTask(IReadOnlyList<string> InputPaths, string OutputPath, string Title, string? CoverPath)
{
    /// <summary>Gets a compact label for history menus.</summary>
    public string DisplayName => $"{Title}（{InputPaths.Count} 本）— {Path.GetFileName(OutputPath)}";
}

internal static class TaskHistoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static string DirectoryPath => Path.Combine(AppContext.BaseDirectory, "EpubMerge.Gui");
    private static string FilePath => Path.Combine(DirectoryPath, "history.json");

    /// <summary>Gets the directory used by the last saved task, when available.</summary>
    public static string? LastOutputDirectory => Load().FirstOrDefault()?.OutputPath is { } path
        ? Path.GetDirectoryName(path)
        : null;

    /// <summary>Loads recent tasks, treating missing or corrupt history as an empty list.</summary>
    public static IReadOnlyList<RecentMergeTask> Load()
    {
        try
        {
            // History is convenience data: a corrupt or inaccessible file must never prevent the app from starting.
            if (!File.Exists(FilePath)) return [];
            return JsonSerializer.Deserialize<List<RecentMergeTask>>(File.ReadAllText(FilePath), JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>Adds a task to the front of history and keeps only the ten newest entries.</summary>
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
