using System.IO;
using System.Xml;

namespace EpubMerge.Gui;

internal static class MergeErrorFormatter
{
    public static string Format(Exception exception) => exception switch
    {
        ArgumentException => "合并参数无效，请检查输入文件、输出位置和书名。",
        FileNotFoundException => "找不到需要合并的文件，请确认文件仍然存在且可以访问。",
        UnauthorizedAccessException => "没有权限读取输入文件或写入输出位置，请选择有权限的文件夹。",
        IOException => "文件读写失败，请确认文件没有被其他程序占用，并检查磁盘空间。",
        InvalidDataException => "EPUB 文件结构无效或已损坏，请检查输入文件后重试。",
        XmlException => "EPUB 内部目录格式无效，请更换文件后重试。",
        NotSupportedException => "当前 EPUB 包含暂不支持的内容，请更换文件后重试。",
        _ => "合并失败，文件可能已损坏或格式不受支持。请检查输入文件后重试。"
    };
}