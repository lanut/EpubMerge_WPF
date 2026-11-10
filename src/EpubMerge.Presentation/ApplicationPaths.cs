using System.Diagnostics;

namespace EpubMerge.Gui;

/// <summary>Writable, per-user locations shared by desktop services.</summary>
public static class ApplicationPaths
{
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EpubMerge");

    public static string DefaultOutputDirectory => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    public static event EventHandler<Exception>? StorageError;

    public static void ReportStorageError(Exception exception)
    {
        Trace.TraceError(exception.ToString());
        try
        {
            Directory.CreateDirectory(DataDirectory);
            File.AppendAllText(Path.Combine(DataDirectory, "storage.error.log"),
                $"{DateTimeOffset.Now:O} {exception}{Environment.NewLine}");
        }
        catch (Exception logException) when (logException is IOException or UnauthorizedAccessException)
        {
            Trace.TraceError(logException.ToString());
        }
        StorageError?.Invoke(null, exception);
    }
}
