using System.IO;
using Xunit;

namespace EpubMerge.Gui.Tests;

[Collection("Language")]
public sealed class MainViewModelTests
{
    [Fact]
    public void File_actions_deduplicate_and_reorder_in_display_order()
    {
        LanguageManager.SetCulture("zh-CN");
        var viewModel = new MainViewModel();
        viewModel.AddFiles(["a.epub", "b.epub", "a.epub"]);
        Assert.Equal(["a.epub", "b.epub"], viewModel.Files.Select(file => Path.GetFileName(file.Path)));

        viewModel.MoveFiles([viewModel.Files[1]], -1);
        Assert.Equal("b.epub", Path.GetFileName(viewModel.Files[0].Path));
        viewModel.RemoveFiles([viewModel.Files[0]]);
        Assert.Single(viewModel.Files);
        viewModel.ClearFiles();
        Assert.Empty(viewModel.Files);
        Assert.Equal("请选择要合并的 EPUB 文件", viewModel.StatusMessage);
    }

    [Fact]
    public void Changing_language_refreshes_dynamic_view_model_text()
    {
        LanguageManager.SetCulture("zh-CN");
        var viewModel = new MainViewModel();
        viewModel.AddFiles(["book.epub"]);

        Assert.Equal("1 本 EPUB", viewModel.FileCountText);
        viewModel.ClearFiles();
        Assert.Equal("请选择要合并的 EPUB 文件", viewModel.StatusMessage);

        viewModel.SelectedLanguage = "en-US";

        Assert.Equal("No files added", viewModel.FileCountText);
        Assert.Equal("Select EPUB files to merge", viewModel.StatusMessage);
        Assert.Equal("No cover image selected", viewModel.CoverPreviewMessage);

        viewModel.SelectedLanguage = "zh-CN";
        Assert.Equal("还未添加文件", viewModel.FileCountText);
    }
}
