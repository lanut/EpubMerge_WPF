using System.IO;
using Xunit;

namespace EpubMerge.Gui.Tests;

public sealed class MainViewModelTests
{
    [Fact]
    public void File_actions_deduplicate_and_reorder_in_display_order()
    {
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
}
