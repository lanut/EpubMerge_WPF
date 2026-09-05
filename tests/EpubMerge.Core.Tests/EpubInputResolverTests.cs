using EpubMerge.Core.Pure;
using Xunit;

namespace EpubMerge.Core.Tests;

public sealed class EpubInputResolverTests : IDisposable
{
    private static readonly object CurrentDirectoryLock = new();
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "EpubMergeResolverTests", Guid.NewGuid().ToString("N"));

    public EpubInputResolverTests()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "Vol.10.epub"), string.Empty);
        File.WriteAllText(Path.Combine(_directory, "Vol.2.epub"), string.Empty);
        File.WriteAllText(Path.Combine(_directory, "Vol.01.epub"), string.Empty);
        File.WriteAllText(Path.Combine(_directory, "notes.txt"), string.Empty);
        Directory.CreateDirectory(Path.Combine(_directory, "nested"));
        File.WriteAllText(Path.Combine(_directory, "nested", "nested.epub"), string.Empty);
    }

    public void Dispose() => Directory.Delete(_directory, true);

    [Fact]
    public void Resolve_expands_relative_wildcard_and_natural_sorts_by_file_name()
    {
        lock (CurrentDirectoryLock)
        {
            var original = Environment.CurrentDirectory;
            try
            {
                Directory.SetCurrentDirectory(_directory);
                var result = EpubInputResolver.Resolve(["*.epub"]);

                Assert.Equal(["Vol.01.epub", "Vol.2.epub", "Vol.10.epub"], result.Select(Path.GetFileName));
            }
            finally
            {
                Directory.SetCurrentDirectory(original);
            }
        }
    }

    [Fact]
    public void Resolve_scans_directory_recursively_and_name_sort_uses_file_names()
    {
        var result = EpubInputResolver.Resolve([], _directory, recursive: true, sort: EpubSortMode.Name);

        Assert.Equal(["nested.epub", "Vol.01.epub", "Vol.10.epub", "Vol.2.epub"], result.Select(Path.GetFileName));
    }
}