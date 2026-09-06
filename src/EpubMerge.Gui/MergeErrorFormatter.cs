using System.IO;
using System.Xml;

namespace EpubMerge.Gui;

static class MergeErrorFormatter
{
    /// <summary>Converts an exception into a localized, user-facing error category.</summary>
    public static string Format(Exception exception)
    {
        return exception switch
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
}
