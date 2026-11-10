using System.Text.Json;
using EpubMerge.Core.Pure;
using EpubMerge.Gui;
using Xunit;

namespace EpubMerge.Presentation.Tests;

public sealed class HistoryFileStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "EpubMergeTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Load_DoesNotImportHistoryFromWpfDirectory()
    {
        Directory.CreateDirectory(_directory);
        var wpfDirectory = Path.Combine(_directory, "EpubMerge.Gui");
        Directory.CreateDirectory(wpfDirectory);
        var legacy = Path.Combine(wpfDirectory, "history.json");
        File.WriteAllText(legacy, """[{"InputPaths":["a.epub"],"OutputPath":"old.epub","Title":"Old","CoverPath":null}]""");
        var store = new HistoryFileStore(Path.Combine(_directory, "data"), exception => Assert.Fail(exception.ToString()));

        Assert.Empty(store.Load());
        Assert.False(File.Exists(Path.Combine(_directory, "data", "history.json")));
        store.Add(new EpubMergeRequest(["b.epub"], "new.epub", "New"));
        Assert.Equal("New", Assert.Single(store.Load()).Title);
        Assert.Contains("Old", File.ReadAllText(legacy));
    }

    [Fact]
    public void Add_RoundTripsUnicodeAndKeepsTenDistinctOutputs()
    {
        var store = new HistoryFileStore(_directory, reportError: exception => Assert.Fail(exception.ToString()));
        for (var index = 0; index < 12; index++)
            store.Add(new EpubMergeRequest([$"输入{index}.epub"], $"output{index}.epub", $"标题{index}", "cover.svg"));
        store.Add(new EpubMergeRequest(["再次.epub"], "output11.epub", "Updated"));

        var tasks = store.Load();
        Assert.Equal(10, tasks.Count);
        Assert.Equal("Updated", tasks[0].Title);
        Assert.Equal("标题10", tasks[1].Title);
        Assert.Equal("cover.svg", tasks[1].CoverPath);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("not json")]
    [InlineData("""[{"InputPaths":null,"Title":"bad","OutputPath":"out.epub"}]""")]
    [InlineData("[null]")]
    public void Load_CorruptDataReportsFailureInsteadOfCrashing(string json)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "history.json"), json);
        var errors = new List<Exception>();
        var store = new HistoryFileStore(_directory, reportError: errors.Add);
        Assert.Empty(store.Load());
        Assert.IsType<JsonException>(Assert.Single(errors));
        store.Add(new EpubMergeRequest(["a.epub"], "out.epub", "New"));
        Assert.Equal(2, errors.Count);
        Assert.Equal(json, File.ReadAllText(Path.Combine(_directory, "history.json")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
