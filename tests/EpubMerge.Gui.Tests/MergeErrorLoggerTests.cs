using System.IO;
using EpubMerge.Core.Pure;
using Xunit;

namespace EpubMerge.Gui.Tests;

public class MergeErrorLoggerTests
{
    [Fact]
    public void Write_CreatesLogInApplicationDirectoryAndIncludesDiagnosticDetails()
    {
        var directory = Path.Combine(Path.GetTempPath(), "EpubMergeTests", Guid.NewGuid().ToString("N"));
        var output = Path.Combine(directory, "merged.epub");
        var request = new EpubMergeRequest(["input.epub"], output, "合辑");

        var logPath = InvokeLogger(request, new InvalidOperationException("internal detail"));
        Assert.NotNull(logPath);
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "EpubMerge.error.log"), logPath);
        var content = File.ReadAllText(logPath);
        Assert.Contains("input.epub", content);
        Assert.Contains("internal detail", content);
        Assert.Contains(output, content);
        File.Delete(logPath);
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }

    private static string? InvokeLogger(EpubMergeRequest request, Exception exception)
    {
        var logger = typeof(MergeUiResult).Assembly
            .GetType("EpubMerge.Gui.MergeErrorLogger")!;
        return (string?)logger.GetMethod("Write")!.Invoke(null, [request, exception]);
    }
}
