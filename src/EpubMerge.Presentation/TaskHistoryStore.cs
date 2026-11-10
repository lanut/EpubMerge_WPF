using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    [JsonIgnore]
    public string DisplayName => $"{Title} ({LanguageManager.Get("FileCount", InputPaths.Count)}) - {Path.GetFileName(OutputPath)}";
}

/// <summary>Persists recent merge tasks for the desktop application.</summary>
public static class TaskHistoryStore
{
    private static readonly HistoryFileStore Store = new(ApplicationPaths.DataDirectory);

    /// <summary>Gets the directory used by the last saved task, when available.</summary>
    public static string? LastOutputDirectory => Load().FirstOrDefault()?.OutputPath is { } path
        ? Path.GetDirectoryName(path)
        : null;

    /// <summary>Loads recent tasks, treating missing or corrupt history as an empty list.</summary>
    public static IReadOnlyList<RecentMergeTask> Load() => Store.Load();

    /// <summary>Saves a recent task.</summary>
    public static void Add(EpubMergeRequest request) => Store.Add(request);
}

internal sealed class HistoryFileStore(string directory, Action<Exception>? reportError = null)
{
    private string FilePath => Path.Combine(directory, "history.json");

    public IReadOnlyList<RecentMergeTask> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return [];
            return Read(FilePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            (reportError ?? ApplicationPaths.ReportStorageError)(exception);
            return [];
        }
    }

    public void Add(EpubMergeRequest request)
    {
        try
        {
            var history = (File.Exists(FilePath) ? Read(FilePath) : Load())
                .Where(item => !string.Equals(item.OutputPath, request.OutputPath, StringComparison.OrdinalIgnoreCase)).ToList();
            history.Insert(0, new RecentMergeTask(request.InputPaths.ToList(), request.OutputPath, request.Title, request.CoverPath));
            Save(history.Take(10).ToList());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            (reportError ?? ApplicationPaths.ReportStorageError)(exception);
        }
    }

    private static List<RecentMergeTask> Read(string path)
    {
        var result = JsonSerializer.Deserialize(File.ReadAllText(path), HistoryJsonContext.Default.ListRecentMergeTask)
            ?? throw new JsonException("History must be a JSON array.");
        if (result.Any(item => item is null || item.InputPaths is null ||
                               string.IsNullOrWhiteSpace(item.OutputPath) || item.Title is null))
            throw new JsonException("Invalid merge history entry.");
        return result;
    }

    private void Save(List<RecentMergeTask> history)
    {
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $"history-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(history, HistoryJsonContext.Default.ListRecentMergeTask));
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    if (File.Exists(FilePath)) File.Replace(temporaryPath, FilePath, null);
                    else File.Move(temporaryPath, FilePath);
                    break;
                }
                catch (Exception exception) when (attempt < 4 &&
                    (exception is IOException or UnauthorizedAccessException) &&
                    (exception.HResult & 0xffff) is 5 or 32 or 33)
                {
                    // Antivirus/indexers can briefly hold the destination without delete sharing.
                    Thread.Sleep(50);
                }
            }
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(List<RecentMergeTask>))]
internal partial class HistoryJsonContext : JsonSerializerContext;
