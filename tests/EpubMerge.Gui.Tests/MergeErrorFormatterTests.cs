using System.IO;
using System.Xml;
using Xunit;

namespace EpubMerge.Gui.Tests;

public class MergeErrorFormatterTests
{
    [Theory]
    [InlineData(typeof(FileNotFoundException), "找不到需要合并的文件")]
    [InlineData(typeof(UnauthorizedAccessException), "没有权限读取输入文件")]
    [InlineData(typeof(InvalidDataException), "EPUB 文件结构无效")]
    [InlineData(typeof(XmlException), "EPUB 内部目录格式无效")]
    public void Format_ReturnsActionableMessageForKnownErrors(Type exceptionType, string expectedText)
    {
        var exception = (Exception)Activator.CreateInstance(exceptionType)!;

        var message = InvokeFormatter(exception);

        Assert.Contains(expectedText, message);
        Assert.DoesNotContain(exception.GetType().FullName!, message);
    }

    [Fact]
    public void Format_HidesTechnicalDetailsForUnknownErrors()
    {
        var message = InvokeFormatter(new InvalidOperationException("internal detail"));

        Assert.Equal("合并失败，文件可能已损坏或格式不受支持。请检查输入文件后重试。", message);
        Assert.DoesNotContain("internal detail", message);
    }

    private static string InvokeFormatter(Exception exception)
    {
        var formatter = typeof(EpubMerge.Gui.MergeUiResult).Assembly
            .GetType("EpubMerge.Gui.MergeErrorFormatter")!;
        return (string)formatter.GetMethod("Format")!.Invoke(null, [exception])!;
    }
}