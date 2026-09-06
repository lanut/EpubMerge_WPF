using System.IO;
using EpubMerge.Core.Pure;
using Xunit;

namespace EpubMerge.Gui.Tests;

[Collection("Language")]
public sealed class MainViewModelTests
{
    private static MainViewModel CreateViewModel() => new(
        new EpubMergeService(),
        new EpubCoverExtractor(),
        new InMemoryHistoryStore(),
        new InMemoryTemporaryCoverStore());

    [Fact]
    public void File_actions_deduplicate_and_reorder_in_display_order()
    {
        LanguageManager.SetCulture("zh-CN");
        using var viewModel = CreateViewModel();
        viewModel.AddFiles(["a.epub", "b.epub", "a.epub"]);
        Assert.Equal(["a.epub", "b.epub"], viewModel.Files.Select(file => Path.GetFileName(file.Path)));

        viewModel.SetSelectedFiles([viewModel.Files[1]]);
        viewModel.MoveSelectedFilesUpCommand.Execute(null);
        Assert.Equal("b.epub", Path.GetFileName(viewModel.Files[0].Path));
        viewModel.SetSelectedFiles([viewModel.Files[0]]);
        viewModel.RemoveSelectedFilesCommand.Execute(null);
        Assert.Single(viewModel.Files);
        viewModel.ClearFilesCommand.Execute(null);
        Assert.Empty(viewModel.Files);
        Assert.Equal("请选择要合并的 EPUB 文件", viewModel.StatusMessage);
    }

    [Fact]
    public void Changing_language_refreshes_dynamic_view_model_text()
    {
        LanguageManager.SetCulture("zh-CN");
        using var viewModel = CreateViewModel();
        viewModel.AddFiles(["book.epub"]);

        Assert.Equal("1 本 EPUB", viewModel.FileCountText);
        viewModel.ClearFilesCommand.Execute(null);
        Assert.Equal("请选择要合并的 EPUB 文件", viewModel.StatusMessage);

        viewModel.SelectedLanguage = "en-US";

        Assert.Equal("No files added", viewModel.FileCountText);
        Assert.Equal("Select EPUB files to merge", viewModel.StatusMessage);
        Assert.Equal("No cover image selected", viewModel.CoverPreviewMessage);

        viewModel.SelectedLanguage = "zh-CN";
        Assert.Equal("还未添加文件", viewModel.FileCountText);
    }

    [Fact]
    public void Selection_commands_follow_busy_and_selection_state()
    {
        using var viewModel = CreateViewModel();
        viewModel.AddFiles(["a.epub"]);

        Assert.False(viewModel.RemoveSelectedFilesCommand.CanExecute(null));
        viewModel.SetSelectedFiles([viewModel.Files[0]]);
        Assert.True(viewModel.RemoveSelectedFilesCommand.CanExecute(null));

        viewModel.IsBusy = true;
        Assert.False(viewModel.RemoveSelectedFilesCommand.CanExecute(null));
        Assert.False(viewModel.ClearFilesCommand.CanExecute(null));
        Assert.True(viewModel.CancelMergeCommand.CanExecute(null));
    }

    [Fact]
    public void Cover_path_updates_preview_path_without_loading_WPF_image_types()
    {
        using var viewModel = CreateViewModel();
        var path = Path.Combine(Path.GetTempPath(), $"epub-merge-cover-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(path, [137, 80, 78, 71]);

        try
        {
            viewModel.CoverPath = path;
            Assert.Equal(Path.GetFullPath(path), viewModel.CoverPreviewPath);

            viewModel.CoverPath = Path.Combine(Path.GetTempPath(), "missing-cover.png");
            Assert.Null(viewModel.CoverPreviewPath);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Merge_validation_failure_does_not_enter_busy_state()
    {
        using var viewModel = CreateViewModel();
        var result = await viewModel.MergeAsync();

        Assert.False(result.Succeeded);
        Assert.False(result.Canceled);
        Assert.False(viewModel.IsBusy);
        Assert.Equal(MergeProgressState.Error, viewModel.ProgressState);
    }

    private sealed class InMemoryHistoryStore : IMergeHistoryStore
    {
        public string? LastOutputDirectory => null;
        public IReadOnlyList<RecentMergeTask> Load() => [];
        public void Add(EpubMerge.Core.Pure.EpubMergeRequest request) { }
    }

    private sealed class InMemoryTemporaryCoverStore : ITemporaryCoverStore
    {
        private int _counter;
        public string Save(EpubMerge.Core.Pure.ExtractedCover cover) => $"memory-cover-{++_counter}{cover.SuggestedExtension}";
        public void Delete(string path) { }
    }
}
