using System.IO;
using EpubMerge.Core.Pure;
using EpubMerge.Gui;
using Xunit;

namespace EpubMerge.Presentation.Tests;

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

    [Fact]
    public void Reordering_preserves_display_order_and_busy_state()
    {
        using var viewModel = CreateViewModel();
        viewModel.AddFiles(["a.epub", "b.epub", "c.epub", "d.epub"]);
        viewModel.ReorderFiles([viewModel.Files[2], viewModel.Files[0]], 4);
        Assert.Equal(["b.epub", "d.epub", "a.epub", "c.epub"], viewModel.Files.Select(file => file.Name));
        Assert.Equal(["a.epub", "c.epub"], viewModel.SelectedFiles.Select(file => file.Name));
        viewModel.IsBusy = true;
        viewModel.ReorderFiles([viewModel.Files[3]], 0);
        Assert.Equal("b.epub", viewModel.Files[0].Name);
    }

    [Fact]
    public void Moving_multiple_items_at_boundary_does_not_reverse_selection()
    {
        using var viewModel = CreateViewModel();
        viewModel.AddFiles(["a.epub", "b.epub", "c.epub"]);
        viewModel.SetSelectedFiles([viewModel.Files[1], viewModel.Files[0]]);
        viewModel.MoveSelectedFilesUpCommand.Execute(null);
        Assert.Equal(["a.epub", "b.epub", "c.epub"], viewModel.Files.Select(file => file.Name));
        viewModel.MoveSelectedFilesToBottomCommand.Execute(null);
        Assert.Equal(["c.epub", "a.epub", "b.epub"], viewModel.Files.Select(file => file.Name));
        viewModel.MoveSelectedFilesDownCommand.Execute(null);
        Assert.Equal(["c.epub", "a.epub", "b.epub"], viewModel.Files.Select(file => file.Name));
    }

    [Fact]
    public void Recursive_folder_import_remains_naturally_sorted()
    {
        var directory = Path.Combine(Path.GetTempPath(), "EpubMergeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "nested"));
        try
        {
            File.WriteAllText(Path.Combine(directory, "book10.epub"), "");
            File.WriteAllText(Path.Combine(directory, "nested", "book2.epub"), "");
            using var viewModel = CreateViewModel();
            viewModel.AddFiles([directory], recursive: true);
            Assert.Equal(["book2.epub", "book10.epub"], viewModel.Files.Select(file => file.Name));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Language_switch_preserves_error_category_and_refreshes_indexer()
    {
        LanguageManager.SetCulture("zh-CN");
        using var strings = new LocalizedStrings();
        var notifications = new List<string?>();
        strings.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        using var viewModel = CreateViewModel();
        await viewModel.MergeAsync();
        viewModel.SelectedLanguage = "en-US";
        Assert.Equal(LanguageManager.Get("MergeFailed"), viewModel.StatusMessage);
        Assert.Equal("Close", strings["Close"]);
        Assert.Contains("Item[]", notifications);
        viewModel.SelectedLanguage = "zh-CN";
    }

    private sealed class InMemoryTemporaryCoverStore : ITemporaryCoverStore
    {
        private int _counter;
        public string Save(EpubMerge.Core.Pure.ExtractedCover cover) => $"memory-cover-{++_counter}{cover.SuggestedExtension}";
        public void Delete(string path) { }
    }
}
