using System.IO;
using System.Xml;
using Xunit;

namespace EpubMerge.Gui.Tests;

[CollectionDefinition("Language")]
public sealed class LanguageCollection;

[Collection("Language")]
public class MergeErrorFormatterTests
{
    [Theory]
    [InlineData(typeof(ArgumentException), "合并参数无效，请检查输入文件、输出位置和书名。", "Invalid merge arguments. Check the input files, output location and title.")]
    [InlineData(typeof(FileNotFoundException), "找不到需要合并的文件，请确认文件仍然存在且可以访问。", "A file required for merging could not be found. Make sure it still exists and is accessible.")]
    [InlineData(typeof(UnauthorizedAccessException), "没有权限读取输入文件或写入输出位置，请选择有权限的文件夹。", "You do not have permission to read the input files or write to the output location. Choose a folder you can access.")]
    [InlineData(typeof(IOException), "文件读写失败，请确认文件没有被其他程序占用，并检查磁盘空间。", "File I/O failed. Make sure the files are not in use by another application and check the available disk space.")]
    [InlineData(typeof(InvalidDataException), "EPUB 文件结构无效或已损坏，请检查输入文件后重试。", "The EPUB structure is invalid or corrupted. Check the input files and try again.")]
    [InlineData(typeof(XmlException), "EPUB 内部目录格式无效，请更换文件后重试。", "The EPUB internal directory is invalid. Choose another file and try again.")]
    [InlineData(typeof(NotSupportedException), "当前 EPUB 包含暂不支持的内容，请更换文件后重试。", "This EPUB contains unsupported content. Choose another file and try again.")]
    public void Format_UsesCurrentLanguageForKnownErrors(Type exceptionType, string chineseText, string englishText)
    {
        var exception = (Exception)Activator.CreateInstance(exceptionType)!;
        var originalCulture = LanguageManager.CurrentCulture.Name;

        try
        {
            LanguageManager.SetCulture("zh-CN");
            Assert.Equal(chineseText, InvokeFormatter(exception));
            LanguageManager.SetCulture("en-US");
            Assert.Equal(englishText, InvokeFormatter(exception));
        }
        finally
        {
            LanguageManager.SetCulture(originalCulture);
        }
    }

    [Fact]
    public void Format_UsesCurrentLanguageAndHidesTechnicalDetailsForUnknownErrors()
    {
        var originalCulture = LanguageManager.CurrentCulture.Name;

        try
        {
            LanguageManager.SetCulture("zh-CN");
            Assert.Equal("合并失败，文件可能已损坏或格式不受支持。请检查输入文件后重试。", InvokeFormatter(new InvalidOperationException("internal detail")));
            LanguageManager.SetCulture("en-US");
            Assert.Equal("The merge failed. The file may be corrupted or unsupported. Check the input files and try again.", InvokeFormatter(new InvalidOperationException("internal detail")));
        }
        finally
        {
            LanguageManager.SetCulture(originalCulture);
        }

        Assert.DoesNotContain("internal detail", InvokeFormatter(new InvalidOperationException("internal detail")));
    }

    private static string InvokeFormatter(Exception exception)
    {
        var formatter = typeof(EpubMerge.Gui.MergeUiResult).Assembly
            .GetType("EpubMerge.Gui.MergeErrorFormatter")!;
        return (string)formatter.GetMethod("Format")!.Invoke(null, [exception])!;
    }
}