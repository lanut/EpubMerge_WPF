using System.IO;
using EpubMerge.Core.Pure;

namespace EpubMerge.Gui;

/// <summary>Stores and retrieves recent merge tasks.</summary>
public interface IMergeHistoryStore
{
    /// <summary>Gets the directory used for the most recently saved output.</summary>
    string? LastOutputDirectory { get; }
    /// <summary>Loads persisted merge tasks.</summary>
    IReadOnlyList<RecentMergeTask> Load();
    /// <summary>Adds a merge request to persisted history.</summary>
    void Add(EpubMergeRequest request);
}

/// <summary>Creates and removes temporary files used by the cover preview.</summary>
public interface ITemporaryCoverStore
{
    /// <summary>Saves extracted cover bytes and returns the resulting path.</summary>
    string Save(ExtractedCover cover);
    /// <summary>Deletes a previously saved temporary cover.</summary>
    void Delete(string path);
}

/// <summary>Production adapter around the existing history persistence implementation.</summary>
public sealed class FileMergeHistoryStore : IMergeHistoryStore
{
    /// <inheritdoc />
    public string? LastOutputDirectory => TaskHistoryStore.LastOutputDirectory;
    /// <inheritdoc />
    public IReadOnlyList<RecentMergeTask> Load() => TaskHistoryStore.Load();
    /// <inheritdoc />
    public void Add(EpubMergeRequest request) => TaskHistoryStore.Add(request);
}

/// <summary>Production temporary-cover store.</summary>
public sealed class FileTemporaryCoverStore : ITemporaryCoverStore
{
    /// <inheritdoc />
    public string Save(ExtractedCover cover)
    {
        var directory = Path.Combine(Path.GetTempPath(), "EpubMerge");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"source-cover-{Guid.NewGuid():N}{cover.SuggestedExtension}");
        File.WriteAllBytes(path, cover.ImageData);
        return path;
    }

    /// <inheritdoc />
    public void Delete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
