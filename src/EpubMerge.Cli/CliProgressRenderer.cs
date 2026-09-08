using System.Text.Json;
using EpubMerge.Core.Pure;

namespace EpubMerge.Cli;

/// <summary>Controls how the CLI renders merge progress.</summary>
public enum CliProgressMode
{
    /// <summary>Renders an interactive terminal progress bar.</summary>
    Bar,
    /// <summary>Renders stable, line-oriented key/value records.</summary>
    Plain,
    /// <summary>Renders one JSON object per output line.</summary>
    Jsonl
}

internal interface ICliProgressRenderer
{
    void Report(EpubMergeProgress value);
    void Complete(string outputPath, int totalBooks);
}

internal sealed class PlainProgressRenderer(TextWriter output) : ICliProgressRenderer
{
    public void Report(EpubMergeProgress value)
    {
        output.WriteLine($"PROGRESS phase={GetPhase(value.Stage)} completed={value.CompletedBooks} total={value.TotalBooks}");
    }

    public void Complete(string outputPath, int totalBooks)
    {
        output.WriteLine($"RESULT status=success output={JsonSerializer.Serialize(outputPath)} books={totalBooks}");
    }

    private static string GetPhase(EpubMergeProgressStage stage) => stage switch
    {
        EpubMergeProgressStage.Reading => "reading",
        EpubMergeProgressStage.Merging => "merging",
        _ => "unknown"
    };
}

internal sealed class JsonlProgressRenderer(TextWriter output) : ICliProgressRenderer
{
    public void Report(EpubMergeProgress value)
    {
        Write(new CliProgressEvent(
            "progress",
            GetPhase(value.Stage),
            value.CompletedBooks,
            value.TotalBooks,
            value.Message));
    }

    public void Complete(string outputPath, int totalBooks)
    {
        Write(new CliResultEvent("result", "success", outputPath, totalBooks));
    }

    private void Write(CliProgressEvent value) => output.WriteLine(JsonSerializer.Serialize(value, CliJsonContext.Default.CliProgressEvent));
    private void Write(CliResultEvent value) => output.WriteLine(JsonSerializer.Serialize(value, CliJsonContext.Default.CliResultEvent));

    private static string GetPhase(EpubMergeProgressStage stage) => stage switch
    {
        EpubMergeProgressStage.Reading => "reading",
        EpubMergeProgressStage.Merging => "merging",
        _ => "unknown"
    };
}
