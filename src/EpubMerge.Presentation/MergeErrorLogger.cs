using System.IO;
using System.Text;
using EpubMerge.Core.Pure;

namespace EpubMerge.Gui;

static class MergeErrorLogger
{
    /// <summary>Appends merge request details and the exception stack to the local diagnostic log.</summary>
    /// <returns>The log path, or <see langword="null" /> when logging itself fails.</returns>
    public static string? Write(EpubMergeRequest request, Exception exception)
        => WriteToDirectory(request, exception, ApplicationPaths.DataDirectory);

    internal static string? WriteToDirectory(EpubMergeRequest request, Exception exception, string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var logPath = Path.Combine(directory, "EpubMerge.error.log");
            var builder = new StringBuilder()
                .AppendLine($"时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}")
                .AppendLine($"输出文件：{request.OutputPath}")
                .AppendLine("输入文件：");
            foreach (var inputPath in request.InputPaths) builder.AppendLine($"- {inputPath}");
            builder.AppendLine().AppendLine("异常详情：").AppendLine(exception.ToString()).AppendLine();
            File.AppendAllText(logPath, builder.ToString(), Encoding.UTF8);
            return logPath;
        }
        catch (Exception logException) when (logException is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.TraceError(logException.ToString());
            return null;
        }
    }
}
