using System.IO;
using EpubMerge.Core.Pure;
using EpubMerge.Gui;
using Xunit;

namespace EpubMerge.Presentation.Tests;

public class MergeErrorLoggerTests
{
    [Fact]
    public void Write_CreatesLogInUserDataDirectoryAndIncludesDiagnosticDetails()
    {
        var directory = Path.Combine(Path.GetTempPath(), "EpubMergeTests", Guid.NewGuid().ToString("N"));
        var output = Path.Combine(directory, "merged.epub");
        var request = new EpubMergeRequest(["input.epub"], output, "合辑");

        var logPath = MergeErrorLogger.WriteToDirectory(request, new InvalidOperationException("internal detail"), directory);
        Assert.NotNull(logPath);
        Assert.Equal(Path.Combine(directory, "EpubMerge.error.log"), logPath);
        var content = File.ReadAllText(logPath);
        Assert.Contains("input.epub", content);
        Assert.Contains("internal detail", content);
        Assert.Contains(output, content);
        File.Delete(logPath);
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
