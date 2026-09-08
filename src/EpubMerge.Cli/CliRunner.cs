using System.IO;
using EpubMerge.Core.Pure;
using Spectre.Console;

namespace EpubMerge.Cli;

/// <summary>Runs the EPUB merge command-line application.</summary>
public static class CliRunner
{
    private const string Version = "1.0.0";

    /// <summary>Parses arguments, runs the merge, writes output, and returns a process exit code.</summary>
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter? output = null,
        TextWriter? error = null,
        bool? outputRedirected = null,
        IEpubMergeService? mergeService = null,
        IEpubCoverExtractor? coverExtractor = null)
    {
        var outputWriter = output ?? Console.Out;
        var errorWriter = error ?? Console.Error;
        var parsed = CliOptions.Parse(args);

        if (parsed.ShowHelp)
        {
            PrintHelp(outputWriter);
            return 0;
        }

        if (parsed.ShowVersion)
        {
            await outputWriter.WriteLineAsync($"EpubMerge CLI {Version}");
            return 0;
        }

        if (parsed.Error is not null)
        {
            WriteArgumentError(errorWriter, parsed.Error);
            return 1;
        }

        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;

        string? temporaryCover = null;
        try
        {
            var inputs = EpubInputResolver.Resolve(parsed.Inputs, parsed.Directory, parsed.Recursive, parsed.SortMode);
            if (inputs.Count == 0)
            {
                WriteArgumentError(errorWriter, "没有找到有效的 EPUB 输入文件。");
                return 1;
            }

            if (string.IsNullOrWhiteSpace(parsed.Output))
            {
                WriteArgumentError(errorWriter, "必须指定 -o/--output。");
                return 1;
            }

            var outputPath = Path.GetFullPath(parsed.Output);
            if (inputs.Any(path => string.Equals(Path.GetFullPath(path), outputPath, StringComparison.OrdinalIgnoreCase)))
            {
                WriteArgumentError(errorWriter, "输出文件不能覆盖输入文件。");
                return 1;
            }

            var coverPath = parsed.Cover;
            if (parsed.CoverFromIndex is not null)
            {
                if (parsed.CoverFromIndex.Value < 1 || parsed.CoverFromIndex.Value > inputs.Count)
                {
                    WriteArgumentError(errorWriter, "--cover-from-index 超出输入文件范围。");
                    return 1;
                }

                var cover = (coverExtractor ?? new EpubCoverExtractor()).ExtractCover(inputs[parsed.CoverFromIndex.Value - 1]);
                if (cover is null)
                {
                    WriteArgumentError(errorWriter, "指定的 EPUB 未包含有效封面图片。");
                    return 1;
                }

                temporaryCover = Path.Combine(Path.GetTempPath(), "EpubMerge", $"cli-cover-{Guid.NewGuid():N}{cover.SuggestedExtension}");
                Directory.CreateDirectory(Path.GetDirectoryName(temporaryCover)!);
                await File.WriteAllBytesAsync(temporaryCover, cover.ImageData, cancellation.Token);
                coverPath = temporaryCover;
            }

            var title = string.IsNullOrWhiteSpace(parsed.Title) ? Path.GetFileNameWithoutExtension(outputPath) : parsed.Title.Trim();
            var request = new EpubMergeRequest(inputs, outputPath, title, coverPath);
            EpubMergeValidator.Validate(request);
            var service = mergeService ?? new EpubMergeService();
            var renderer = CreateRenderer(parsed, outputWriter, outputRedirected);

            await MergeWithRendererAsync(service, request, renderer, inputs.Count, cancellation.Token);

            if (!parsed.Quiet)
            {
                if (renderer is SpectreProgressRenderer)
                {
                    await outputWriter.WriteLineAsync($"已生成：{outputPath}");
                    await outputWriter.WriteLineAsync($"共合并 {inputs.Count} 本 EPUB。");
                }
                else if (renderer is not null)
                {
                    renderer.Complete(outputPath, inputs.Count);
                }
            }

            return 0;
        }
        catch (OperationCanceledException)
        {
            await errorWriter.WriteLineAsync("操作已取消。");
            return 2;
        }
        catch (ArgumentException exception)
        {
            WriteArgumentError(errorWriter, exception.Message);
            return 1;
        }
        catch (FileNotFoundException exception)
        {
            await errorWriter.WriteLineAsync($"输入错误：{exception.Message}");
            return 1;
        }
        catch (InvalidDataException exception)
        {
            await errorWriter.WriteLineAsync($"输入错误：{exception.Message}");
            return 1;
        }
        catch (NotSupportedException exception)
        {
            await errorWriter.WriteLineAsync($"输入错误：{exception.Message}");
            return 1;
        }
        catch (Exception exception)
        {
            await errorWriter.WriteLineAsync($"合并失败：{exception.Message}");
            if (parsed.Verbose) errorWriter.WriteLine(exception);
            return 2;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
            if (temporaryCover is not null) TryDelete(temporaryCover);
        }
    }

    private static async Task MergeWithRendererAsync(
        IEpubMergeService service,
        EpubMergeRequest request,
        ICliProgressRenderer? renderer,
        int totalBooks,
        CancellationToken cancellationToken)
    {
        if (renderer is SpectreProgressRenderer spectreRenderer)
        {
            await spectreRenderer.StartAsync(
                () => service.MergeAsync(request, new InlineProgress<EpubMergeProgress>(spectreRenderer.Report), cancellationToken),
                totalBooks);
            return;
        }

        if (renderer is not null)
        {
            await service.MergeAsync(request, new InlineProgress<EpubMergeProgress>(renderer.Report), cancellationToken);
            return;
        }

        await service.MergeAsync(request, null, cancellationToken);
    }

    private static ICliProgressRenderer? CreateRenderer(CliOptions options, TextWriter output, bool? outputRedirected)
    {
        if (options.Quiet) return null;
        if (options.ProgressMode == CliProgressMode.Jsonl) return new JsonlProgressRenderer(output);
        if (options.ProgressMode == CliProgressMode.Plain) return new PlainProgressRenderer(output);

        var redirected = outputRedirected ?? output != Console.Out || Console.IsOutputRedirected;
        return redirected ? new PlainProgressRenderer(output) : new SpectreProgressRenderer();
    }

    private static void WriteArgumentError(TextWriter error, string message)
    {
        error.WriteLine($"参数错误：{message}");
        error.WriteLine("使用 --help 查看帮助。");
    }

    private static void PrintHelp(TextWriter output)
    {
        output.WriteLine("""
                          用法：EpubMerge.Cli.exe -i <file1.epub> <file2.epub> -o <output.epub> [选项]

                            -i, --input <paths...>       输入 EPUB 文件、通配符或路径
                            -d, --directory <dir>        扫描目录中的 EPUB 文件
                            -r, --recursive              递归扫描目录
                            -o, --output <path>          输出 EPUB 路径（必需）
                            -t, --title <title>          合并书名
                                --cover <file>           外部封面图片
                                --cover-from-index <n>   使用第 N 本输入书的内置封面
                                --sort <mode>            natural、name 或 none
                                --progress <mode>        bar、plain 或 jsonl
                            -q, --quiet                  仅输出错误
                            -v, --verbose                输出详细错误
                            -h, --help                   显示帮助
                                --version                显示版本
                          """);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
        }
    }

    private sealed class CliOptions
    {
        public List<string> Inputs { get; } = [];
        public string? Directory { get; set; }
        public string? Output { get; set; }
        public string? Title { get; set; }
        public string? Cover { get; set; }
        public int? CoverFromIndex { get; set; }
        public EpubSortMode SortMode { get; set; } = EpubSortMode.Natural;
        public CliProgressMode ProgressMode { get; set; } = CliProgressMode.Bar;
        public bool Recursive { get; set; }
        public bool Quiet { get; set; }
        public bool Verbose { get; set; }
        public bool ShowHelp { get; set; }
        public bool ShowVersion { get; set; }
        public string? Error { get; set; }

        public static CliOptions Parse(string[] args)
        {
            var result = new CliOptions();
            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                switch (arg.ToLowerInvariant())
                {
                    case "-h":
                    case "--help": result.ShowHelp = true; break;
                    case "--version": result.ShowVersion = true; break;
                    case "-q":
                    case "--quiet": result.Quiet = true; break;
                    case "-v":
                    case "--verbose": result.Verbose = true; break;
                    case "-r":
                    case "--recursive": result.Recursive = true; break;
                    case "-i":
                    case "--input":
                        while (i + 1 < args.Length && !IsOption(args[i + 1])) result.Inputs.Add(args[++i]);
                        if (result.Inputs.Count == 0) result.Error ??= "-i/--input 后缺少输入文件。";
                        break;
                    case "-d":
                    case "--directory": result.Directory = NextValue(args, ref i, result); break;
                    case "-o":
                    case "--output": result.Output = NextValue(args, ref i, result); break;
                    case "-t":
                    case "--title": result.Title = NextValue(args, ref i, result); break;
                    case "--cover": result.Cover = NextValue(args, ref i, result); break;
                    case "--cover-from-index":
                        var value = NextValue(args, ref i, result);
                        if (!int.TryParse(value, out var index)) result.Error ??= "--cover-from-index 必须是正整数。";
                        else result.CoverFromIndex = index;
                        break;
                    case "--sort":
                        var sort = NextValue(args, ref i, result)?.ToLowerInvariant();
                        result.SortMode = sort switch { "natural" => EpubSortMode.Natural, "name" => EpubSortMode.Name, "none" => EpubSortMode.None, _ => result.SortMode };
                        if (sort is not ("natural" or "name" or "none")) result.Error ??= "--sort 只支持 natural、name 或 none。";
                        break;
                    case "--progress":
                        var progress = NextValue(args, ref i, result)?.ToLowerInvariant();
                        result.ProgressMode = progress switch { "bar" => CliProgressMode.Bar, "plain" => CliProgressMode.Plain, "jsonl" => CliProgressMode.Jsonl, _ => result.ProgressMode };
                        if (progress is not ("bar" or "plain" or "jsonl")) result.Error ??= "--progress 只支持 bar、plain 或 jsonl。";
                        break;
                    default: result.Error ??= $"未知参数：{arg}"; break;
                }
            }

            if (result.Quiet && result.Verbose) result.Error ??= "--quiet 与 --verbose 不能同时使用。";
            if (result.CoverFromIndex is not null && !string.IsNullOrWhiteSpace(result.Cover)) result.Error ??= "--cover 与 --cover-from-index 不能同时使用。";
            return result;
        }

        private static string? NextValue(string[] args, ref int index, CliOptions result)
        {
            if (index + 1 >= args.Length || IsOption(args[index + 1]))
            {
                result.Error ??= $"参数 {args[index]} 缺少值。";
                return null;
            }
            return args[++index];
        }

        private static bool IsOption(string value) => value.StartsWith("-", StringComparison.Ordinal);
    }

    private sealed class SpectreProgressRenderer : ICliProgressRenderer
    {
        private ProgressTask? task;
        private readonly Progress progress;

        public SpectreProgressRenderer()
        {
            progress = AnsiConsole.Progress().AutoClear(false);
        }

        public void Report(EpubMergeProgress value)
        {
            if (task is null) return;
            task.MaxValue = value.TotalBooks;
            task.Value = value.Stage == EpubMergeProgressStage.Reading ? 0 : value.CompletedBooks;
            task.Description = value.Message;
        }

        public void Complete(string outputPath, int totalBooks)
        {
        }

        public async Task StartAsync(Func<Task> action, int totalBooks)
        {
            await progress.StartAsync(async context =>
            {
                task = context.AddTask("合并 EPUB", new ProgressTaskSettings { MaxValue = totalBooks });
                await action();
            });
        }
    }

    private sealed class InlineProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
