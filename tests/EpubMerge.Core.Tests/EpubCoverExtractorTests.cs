using System.IO.Compression;
using System.Text;
using EpubMerge.Core.Pure;
using Xunit;

namespace EpubMerge.Core.Tests;

public sealed class EpubCoverExtractorTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "EpubMergeCoverTests", Guid.NewGuid().ToString("N"));

    public EpubCoverExtractorTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        Directory.Delete(_directory, true);
    }

    [Fact]
    public void ExtractCover_reads_epub3_cover_image_from_nested_opf()
    {
        var epub = CreateEpub("epub3.epub", """
                                            <package xmlns="http://www.idpf.org/2007/opf" version="3.0">
                                              <metadata><dc:title xmlns:dc="http://purl.org/dc/elements/1.1/">Book</dc:title></metadata>
                                              <manifest><item id="cover" href="images/front.png" media-type="image/png" properties="cover-image"/></manifest>
                                              <spine />
                                            </package>
                                            """, "EPUB/images/front.png", [137, 80, 78, 71]);

        var cover = new EpubCoverExtractor().ExtractCover(epub);

        Assert.NotNull(cover);
        Assert.Equal("image/png", cover!.MimeType);
        Assert.Equal(".png", cover.SuggestedExtension);
        Assert.Equal([137, 80, 78, 71], cover.ImageData);
    }

    [Fact]
    public void ExtractCover_reads_epub2_meta_cover_and_resolves_encoded_href()
    {
        var epub = CreateEpub("epub2.epub", """
                                            <package xmlns="http://www.idpf.org/2007/opf" version="2.0">
                                              <metadata><meta name="cover" content="cover-image"/></metadata>
                                              <manifest><item id="cover-image" href="images/front%20cover.jpg" media-type="image/jpeg"/></manifest>
                                              <spine />
                                            </package>
                                            """, "EPUB/images/front cover.jpg", [255, 216, 255]);

        var cover = new EpubCoverExtractor().ExtractCover(epub);

        Assert.NotNull(cover);
        Assert.Equal("image/jpeg", cover!.MimeType);
        Assert.Equal(".jpg", cover.SuggestedExtension);
        Assert.Equal([255, 216, 255], cover.ImageData);
    }

    [Fact]
    public void ExtractCover_uses_supported_image_extension_for_heuristic_cover()
    {
        var epub = CreateEpub("heuristic.epub", """
                                                <package xmlns="http://www.idpf.org/2007/opf" version="3.0">
                                                  <metadata />
                                                  <manifest><item id="front-cover" href="cover.webp" media-type="application/octet-stream"/></manifest>
                                                  <spine />
                                                </package>
                                                """, "EPUB/cover.webp", [82, 73, 70, 70]);

        var cover = new EpubCoverExtractor().ExtractCover(epub);

        Assert.NotNull(cover);
        Assert.Equal("image/webp", cover!.MimeType);
        Assert.Equal(".webp", cover.SuggestedExtension);
    }

    [Fact]
    public void ExtractCover_returns_null_when_no_cover_image_exists()
    {
        var epub = CreateEpub("none.epub", """
                                           <package xmlns="http://www.idpf.org/2007/opf" version="3.0">
                                             <metadata />
                                             <manifest><item id="chapter" href="chapter.xhtml" media-type="application/xhtml+xml"/></manifest>
                                             <spine />
                                           </package>
                                           """, "EPUB/chapter.xhtml", Encoding.UTF8.GetBytes("chapter"));

        Assert.Null(new EpubCoverExtractor().ExtractCover(epub));
    }

    private string CreateEpub(string name, string opf, string imagePath, byte[] image)
    {
        var path = Path.Combine(_directory, name);
        using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        Write(archive, "META-INF/container.xml", """
                                                 <?xml version="1.0" encoding="UTF-8"?>
                                                 <container xmlns="urn:oasis:names:tc:opendocument:xmlns:container" version="1.0">
                                                   <rootfiles><rootfile full-path="EPUB/package.opf" media-type="application/oebps-package+xml"/></rootfiles>
                                                 </container>
                                                 """);
        Write(archive, "EPUB/package.opf", opf);
        WriteBytes(archive, imagePath, image);
        return path;
    }

    private static void Write(ZipArchive archive, string name, string value)
    {
        WriteBytes(archive, name, Encoding.UTF8.GetBytes(value));
    }

    private static void WriteBytes(ZipArchive archive, string name, byte[] value)
    {
        using var stream = archive.CreateEntry(name).Open();
        stream.Write(value);
    }
}
