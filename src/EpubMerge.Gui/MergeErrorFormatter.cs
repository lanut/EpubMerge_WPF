using System.IO;
using System.Xml;

namespace EpubMerge.Gui;

internal static class MergeErrorFormatter
{
    public static string Format(Exception exception) => exception switch
    {
        ArgumentException => LanguageManager.Get("MergeErrorInvalidArguments"),
        FileNotFoundException => LanguageManager.Get("MergeErrorFileNotFound"),
        UnauthorizedAccessException => LanguageManager.Get("MergeErrorUnauthorized"),
        IOException => LanguageManager.Get("MergeErrorIo"),
        InvalidDataException => LanguageManager.Get("MergeErrorInvalidData"),
        XmlException => LanguageManager.Get("MergeErrorXml"),
        NotSupportedException => LanguageManager.Get("MergeErrorNotSupported"),
        _ => LanguageManager.Get("MergeErrorUnknown")
    };
}