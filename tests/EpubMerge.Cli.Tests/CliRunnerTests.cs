using System.Text.Json;
using EpubMerge.Cli;
using EpubMerge.Core.Pure;
using Xunit;

namespace EpubMerge.Cli.Tests;

public sealed class CliRunnerTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "EpubMergeCliTests", Guid.NewGuid().ToString("N"));

    public CliRunnerTests()
    {
        Directory.CreateDirectory(directory);
    }

    public void Dispose()
    {
        Directory.Delete(directory, true);
    }

    [Fact]
    public async Task Help_is_written_to_stdout()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await CliRunner.RunAsync(["--help"], output, error, true);

        Assert.Equal(0, exitCode);
        Assert.Contains("--progress <mode>", output.ToString());
        Assert.Empty(error.ToString());
    }

    [Fact]
    public async Task Invalid_progress_mode_returns_argument_error()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await CliRunner.RunAsync(["--progress", "invalid"], output, error, true);

        Assert.Equal(1, exitCode);
        Assert.Contains("--progress 只支持", error.ToString());
        Assert.Empty(output.ToString());
    }

    [Fact]
    public async Task Quiet_suppresses_success_and_progress_output()
    {
        var input = CreateInput("quiet.epub");
        var outputPath = Path.Combine(directory, "quiet-output.epub");
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await CliRunner.RunAsync(
            ["--quiet", "-i", input, "-o", outputPath],
            output,
            error,
            true,
            new FakeMergeService());

        Assert.Equal(0, exitCode);
        Assert.Empty(output.ToString());
        Assert.Empty(error.ToString());
    }

    [Fact]
    public async Task Plain_progress_is_line_oriented_and_stable()
    {
        var input = CreateInput("plain.epub");
        var outputPath = Path.Combine(directory, "plain-output.epub");
        var output = new StringWriter();

        var exitCode = await CliRunner.RunAsync(
            ["--progress", "plain", "-i", input, "-o", outputPath],
            output,
            new StringWriter(),
            true,
            new FakeMergeService());

        var lines = output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(0, exitCode);
        Assert.Contains("PROGRESS phase=reading completed=0 total=1", lines);
        Assert.Contains("PROGRESS phase=merging completed=1 total=1", lines);
        Assert.Contains(lines, line => line.StartsWith("RESULT status=success", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Jsonl_progress_contains_parseable_progress_and_result_events()
    {
        var input = CreateInput("jsonl.epub");
        var outputPath = Path.Combine(directory, "jsonl-output.epub");
        var output = new StringWriter();

        var exitCode = await CliRunner.RunAsync(
            ["--progress", "jsonl", "-i", input, "-o", outputPath],
            output,
            new StringWriter(),
            true,
            new FakeMergeService());

        var documents = output.ToString()
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonDocument.Parse(line))
            .ToList();

        Assert.Equal(0, exitCode);
        Assert.Contains(documents, document => document.RootElement.GetProperty("type").GetString() == "progress" && document.RootElement.GetProperty("phase").GetString() == "reading");
        Assert.Contains(documents, document => document.RootElement.GetProperty("type").GetString() == "progress" && document.RootElement.GetProperty("phase").GetString() == "merging");
        Assert.Contains(documents, document => document.RootElement.GetProperty("type").GetString() == "result");
    }

    [Fact]
    public async Task Default_progress_uses_plain_output_when_stdout_is_redirected()
    {
        var input = CreateInput("redirected.epub");
        var outputPath = Path.Combine(directory, "redirected-output.epub");
        var output = new StringWriter();

        var exitCode = await CliRunner.RunAsync(
            ["-i", input, "-o", outputPath],
            output,
            new StringWriter(),
            true,
            new FakeMergeService());

        Assert.Equal(0, exitCode);
        Assert.Contains("PROGRESS", output.ToString());
        Assert.DoesNotContain("\u001b[", output.ToString());
    }

    private string CreateInput(string fileName)
    {
        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, string.Empty);
        return path;
    }

    private sealed class FakeMergeService : IEpubMergeService
    {
        public Task MergeAsync(EpubMergeRequest request, IProgress<EpubMergeProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            progress?.Report(new EpubMergeProgress(0, request.InputPaths.Count, "reading", EpubMergeProgressStage.Reading));
            progress?.Report(new EpubMergeProgress(request.InputPaths.Count, request.InputPaths.Count, "merging", EpubMergeProgressStage.Merging));
            File.WriteAllText(request.OutputPath, "test output");
            return Task.CompletedTask;
        }
    }
}
