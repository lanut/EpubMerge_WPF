using System.IO;
using EpubMerge.Core.Pure;

namespace EpubMerge.Gui;

internal static class CliRunner
{
    private const string Version = "1.0.0";

    /// <summary>Parses CLI arguments, executes a merge, and returns a process exit code.</summary>
    /// <param name="args">Arguments excluding the explicit <c>--cli</c> switch.</param>
    /// <returns>Zero for success, one for invalid input, and two for cancellation or merge failure.</returns>
    public static async Task<int> RunAsync(string[] args)
    {
        var parsed = Parse(args);
        if (parsed.ShowHelp) { PrintHelp(); return 0; }
        if (parsed.ShowVersion) { Console.WriteLine($"EpubMerge {Version}"); return 0; }
        if (parsed.Error is not null) { Console.Error.WriteLine($"参数错误：{parsed.Error}"); Console.Error.WriteLine("使用 --help 查看帮助。"); return 1; }

        var inputs = EpubInputResolver.Resolve(parsed.Inputs, parsed.Directory, parsed.Recursive, parsed.SortMode);
        if (inputs.Count == 0) { Console.Error.WriteLine("参数错误：没有找到有效的 EPUB 输入文件。"); return 1; }
        if (string.IsNullOrWhiteSpace(parsed.Output)) { Console.Error.WriteLine("参数错误：必须指定 -o/--output。"); return 1; }
        var output = Path.GetFullPath(parsed.Output);
        if (inputs.Any(path => string.Equals(Path.GetFullPath(path), output, StringComparison.OrdinalIgnoreCase)))
        {
            Console.Error.WriteLine("参数错误：输出文件不能覆盖输入文件。");
            return 1;
        }

        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;

        string? coverPath = parsed.Cover;
        string? temporaryCover = null;
        try
        {
            if (parsed.CoverFromIndex is not null)
            {
                if (parsed.CoverFromIndex.Value < 1 || parsed.CoverFromIndex.Value > inputs.Count)
                {
                    Console.Error.WriteLine("参数错误：--cover-from-index 超出输入文件范围。");
                    return 1;
                }
                var cover = new EpubCoverExtractor().ExtractCover(inputs[parsed.CoverFromIndex.Value - 1]);
                if (cover is null) { Console.Error.WriteLine("参数错误：指定的 EPUB 未包含有效封面图片。"); return 1; }
                temporaryCover = Path.Combine(Path.GetTempPath(), "EpubMerge", $"cli-cover-{Guid.NewGuid():N}{cover.SuggestedExtension}");
                Directory.CreateDirectory(Path.GetDirectoryName(temporaryCover)!);
                await File.WriteAllBytesAsync(temporaryCover, cover.ImageData);
                coverPath = temporaryCover;
            }

            var title = string.IsNullOrWhiteSpace(parsed.Title) ? Path.GetFileNameWithoutExtension(output) : parsed.Title.Trim();
            var progress = parsed.Quiet ? null : new Progress<EpubMergeProgress>(value =>
            {
                if (!parsed.Verbose && value.Message.StartsWith("正在读取", StringComparison.Ordinal)) return;
                Console.WriteLine($"[{value.CompletedBooks}/{value.TotalBooks}] {value.Message}");
            });
            var request = new EpubMergeRequest(inputs, output, title, coverPath);
            EpubMergeValidator.Validate(request);
            await new EpubMergeService().MergeAsync(request, progress, cancellation.Token);
            Console.WriteLine($"已生成：{output}");
            Console.WriteLine($"共合并 {inputs.Count} 本 EPUB。 ");
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("操作已取消。");
            return 2;
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine($"参数错误：{exception.Message}");
            return 1;
        }
        catch (FileNotFoundException exception)
        {
            Console.Error.WriteLine($"输入错误：{exception.Message}");
            return 1;
        }
        catch (InvalidDataException exception)
        {
            Console.Error.WriteLine($"输入错误：{exception.Message}");
            return 1;
        }
        catch (NotSupportedException exception)
        {
            Console.Error.WriteLine($"输入错误：{exception.Message}");
            return 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"合并失败：{exception.Message}");
            if (parsed.Verbose) Console.Error.WriteLine(exception);
            return 2;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
            if (temporaryCover is not null) TryDelete(temporaryCover);
        }
    }

    private static CliOptions Parse(string[] args)
    {
        var result = new CliOptions();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg.ToLowerInvariant())
            {
                case "-h": case "--help": result.ShowHelp = true; break;
                case "--version": result.ShowVersion = true; break;
                case "-q": case "--quiet": result.Quiet = true; break;
                case "-v": case "--verbose": result.Verbose = true; break;
                case "-r": case "--recursive": result.Recursive = true; break;
                case "-i": case "--input":
                    while (i + 1 < args.Length && !IsOption(args[i + 1])) result.Inputs.Add(args[++i]);
                    if (result.Inputs.Count == 0) result.Error = "-i/--input 后缺少输入文件。";
                    break;
                case "-d": case "--directory": result.Directory = NextValue(args, ref i, result); break;
                case "-o": case "--output": result.Output = NextValue(args, ref i, result); break;
                case "-t": case "--title": result.Title = NextValue(args, ref i, result); break;
                case "--cover": result.Cover = NextValue(args, ref i, result); break;
                case "--cover-from-index":
                    var value = NextValue(args, ref i, result);
                    if (!int.TryParse(value, out var index)) result.Error ??= "--cover-from-index 必须是正整数。";
                    else result.CoverFromIndex = index;
                    break;
                case "--sort":
                    var mode = NextValue(args, ref i, result)?.ToLowerInvariant();
                    result.SortMode = mode switch { "natural" => EpubSortMode.Natural, "name" => EpubSortMode.Name, "none" => EpubSortMode.None, _ => result.SortMode };
                    if (mode is not ("natural" or "name" or "none")) result.Error ??= "--sort 只支持 natural、name 或 none。";
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
        if (index + 1 >= args.Length || IsOption(args[index + 1])) { result.Error ??= $"参数 {args[index]} 缺少值。"; return null; }
        return args[++index];
    }
    private static bool IsOption(string value) => value.StartsWith("-", StringComparison.Ordinal);
    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }

    private static void PrintHelp() => Console.WriteLine("""
用法：epubmerge.exe --cli -i <file1.epub> <file2.epub> -o <output.epub> [选项]

  -i, --input <paths...>       输入 EPUB 文件、通配符或路径
  -d, --directory <dir>        扫描目录中的 EPUB 文件
  -r, --recursive              递归扫描目录
  -o, --output <path>          输出 EPUB 路径（必需）
  -t, --title <title>          合并书名
      --cover <file>           外部封面图片
      --cover-from-index <n>   使用第 N 本输入书的内置封面
      --sort <mode>            natural、name 或 none
  -q, --quiet                  仅输出错误
  -v, --verbose                输出详细日志
  -h, --help                   显示帮助
      --version                显示版本
""");

    private sealed class CliOptions
    {
        public List<string> Inputs { get; } = [];
        public string? Directory { get; set; }
        public string? Output { get; set; }
        public string? Title { get; set; }
        public string? Cover { get; set; }
        public int? CoverFromIndex { get; set; }
        public EpubSortMode SortMode { get; set; } = EpubSortMode.Natural;
        public bool Recursive { get; set; }
        public bool Quiet { get; set; }
        public bool Verbose { get; set; }
        public bool ShowHelp { get; set; }
        public bool ShowVersion { get; set; }
        public string? Error { get; set; }
    }
}
