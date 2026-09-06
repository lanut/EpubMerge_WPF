using System.IO;
using System.Text;
using EpubMerge.Core.Pure;

namespace EpubMerge.Gui;

internal static class MergeErrorLogger
{
    /// <summary>Appends merge request details and the exception stack to the local diagnostic log.</summary>
    /// <returns>The log path, or <see langword="null"/> when logging itself fails.</returns>
    public static string? Write(EpubMergeRequest request, Exception exception)
    {
        try
        {
            var logPath = Path.Combine(AppContext.BaseDirectory, "EpubMerge.error.log");
            var builder = new StringBuilder()
                .AppendLine($"时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}")
                .AppendLine($"输出文件：{request.OutputPath}")
                .AppendLine("输入文件：");
            foreach (var inputPath in request.InputPaths) builder.AppendLine($"- {inputPath}");
            builder.AppendLine().AppendLine("异常详情：").AppendLine(exception.ToString()).AppendLine();
            File.AppendAllText(logPath, builder.ToString(), Encoding.UTF8);
            return logPath;
        }
        catch
        {
            return null;
        }
    }
}
