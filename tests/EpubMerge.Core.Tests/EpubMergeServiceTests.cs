using System.IO.Compression;
using System.Text;
using EpubMerge.Core.Pure;
using Xunit;

namespace EpubMerge.Core.Tests;

public sealed class EpubMergeServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "EpubMergeTests", Guid.NewGuid().ToString("N"));

    public EpubMergeServiceTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        Directory.Delete(_directory, true);
    }

    [Fact]
    public async Task MergeAsync_creates_epub3_nav_and_required_zip_layout()
    {
        var first = CreateEpub("one.epub", "第一册", TocType.Nav, "第一章");
        var second = CreateEpub("two.epub", "第二册", TocType.Nav, "第二章");
        var output = Path.Combine(_directory, "merged.epub");
        File.WriteAllText(output, "previous output");

        await new EpubMergeService().MergeAsync(new EpubMergeRequest([first, second], output, "合辑"));

        using var archive = ZipFile.OpenRead(output);
        Assert.Equal("mimetype", archive.Entries[0].FullName);
        Assert.Equal(archive.Entries[0].Length, archive.Entries[0].CompressedLength);
        var raw = File.ReadAllBytes(output);
        Assert.Equal(0, raw[8]); // ZIP local-header compression method, low byte.
        Assert.Equal(0, raw[9]); // ZIP local-header compression method, high byte.
        Assert.NotNull(archive.GetEntry("META-INF/container.xml"));
        Assert.NotNull(archive.GetEntry("EPUB/book_001/OEBPS/chapters/ch1.xhtml"));
        Assert.NotNull(archive.GetEntry("EPUB/book_002/OEBPS/chapters/ch1.xhtml"));
        var nav = ReadEntry(archive, "EPUB/nav.xhtml");
        Assert.Contains("第二章", nav);
        Assert.Contains("book_002/OEBPS/chapters/ch1.xhtml#start", nav);
        var opf = ReadEntry(archive, "EPUB/package.opf");
        Assert.Contains("b002-ch1", opf);
        Assert.Matches("<meta property=\"dcterms:modified\">20[0-9]{2}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z</meta>", opf);
    }

    [Fact]
    public async Task MergeAsync_reports_structured_progress_stages()
    {
        var first = CreateEpub("progress-one.epub", "进度一", TocType.Nav, "第一章");
        var output = Path.Combine(_directory, "progress-output.epub");
        var reports = new List<EpubMergeProgress>();

        await new EpubMergeService().MergeAsync(
            new EpubMergeRequest([first], output, "进度"),
            new Progress<EpubMergeProgress>(reports.Add));

        Assert.Contains(reports, report => report.Stage == EpubMergeProgressStage.Reading);
        Assert.Contains(reports, report => report.Stage == EpubMergeProgressStage.Merging);
    }

    [Fact]
    public async Task MergeAsync_reads_ncx_and_adds_manual_cover()
    {
        var input = CreateEpub("ncx.epub", "NCX 册", TocType.Ncx, "NCX 章节");
        var cover = Path.Combine(_directory, "cover.png");
        await File.WriteAllBytesAsync(cover, [137, 80, 78, 71]);
        var output = Path.Combine(_directory, "with-cover.epub");

        await new EpubMergeService().MergeAsync(new EpubMergeRequest([input], output, "带封面", cover));

        using var archive = ZipFile.OpenRead(output);
        Assert.NotNull(archive.GetEntry("EPUB/cover.png"));
        Assert.Contains("manual-cover-image", ReadEntry(archive, "EPUB/package.opf"));
        Assert.Contains("NCX 章节", ReadEntry(archive, "EPUB/nav.xhtml"));
    }

    [Fact]
    public async Task MergeAsync_uses_spine_heading_when_no_toc_exists()
    {
        var input = CreateEpub("fallback.epub", "回退册", TocType.None, "回退章节");
        var output = Path.Combine(_directory, "fallback-output.epub");

        await new EpubMergeService().MergeAsync(new EpubMergeRequest([input], output, "回退合辑"));

        using var archive = ZipFile.OpenRead(output);
        Assert.Contains("回退章节", ReadEntry(archive, "EPUB/nav.xhtml"));
    }

    [Fact]
    public async Task MergeAsync_handles_duplicate_manifest_ids()
    {
        var input = CreateEpubWithDuplicateManifestIds("duplicate-ids.epub");
        var output = Path.Combine(_directory, "duplicate-ids-output.epub");

        await new EpubMergeService().MergeAsync(new EpubMergeRequest([input], output, "重复 ID"));

        using var archive = ZipFile.OpenRead(output);
        var opf = ReadEntry(archive, "EPUB/package.opf");
        Assert.Contains("id=\"b001-Section0001.xhtml\"", opf);
        Assert.Contains("id=\"b001-Section0001.xhtml-2\"", opf);
    }

    [Fact]
    public async Task MergeAsync_resolves_toc_links_relative_to_nav_document()
    {
        var first = CreateEpubWithNestedNav("nested-one.epub", "嵌套一", "第一章");
        var second = CreateEpubWithNestedNav("nested-two.epub", "嵌套二", "第二章");
        var output = Path.Combine(_directory, "nested-output.epub");

        await new EpubMergeService().MergeAsync(new EpubMergeRequest([first, second], output, "嵌套目录"));

        using var archive = ZipFile.OpenRead(output);
        var nav = ReadEntry(archive, "EPUB/nav.xhtml");
        Assert.Contains("book_001/OEBPS/text/ch1.xhtml#start", nav);
        Assert.Contains("book_002/OEBPS/text/ch1.xhtml#start", nav);
    }

    [Fact]
    public async Task MergeAsync_resolves_ncx_links_relative_to_ncx_document()
    {
        var first = CreateEpubWithNestedNcx("nested-ncx-one.epub", "NCX 嵌套一", "第一章");
        var second = CreateEpubWithNestedNcx("nested-ncx-two.epub", "NCX 嵌套二", "第二章");
        var output = Path.Combine(_directory, "nested-ncx-output.epub");

        await new EpubMergeService().MergeAsync(new EpubMergeRequest([first, second], output, "嵌套 NCX"));

        using var archive = ZipFile.OpenRead(output);
        var nav = ReadEntry(archive, "EPUB/nav.xhtml");
        Assert.Contains("book_001/OEBPS/text/ch1.xhtml#start", nav);
        Assert.Contains("book_002/OEBPS/text/ch1.xhtml#start", nav);
    }

    [Fact]
    public async Task MergeAsync_preserves_external_toc_links()
    {
        var input = CreateEpubWithExternalTocLink("external-link.epub");
        var output = Path.Combine(_directory, "external-link-output.epub");

        await new EpubMergeService().MergeAsync(new EpubMergeRequest([input], output, "外部链接"));

        using var archive = ZipFile.OpenRead(output);
        var nav = ReadEntry(archive, "EPUB/nav.xhtml");
        Assert.Contains("https://example.test/chapter#start", nav);
        Assert.DoesNotContain("book_001/OEBPS/https:", nav);
    }

    [Fact]
    public async Task MergeAsync_falls_back_to_spine_when_navigation_file_is_malformed()
    {
        var input = CreateEpubWithMalformedNavigation("malformed-nav.epub");
        var output = Path.Combine(_directory, "malformed-nav-output.epub");

        await new EpubMergeService().MergeAsync(new EpubMergeRequest([input], output, "损坏目录"));

        using var archive = ZipFile.OpenRead(output);
        var nav = ReadEntry(archive, "EPUB/nav.xhtml");
        Assert.Contains("回退章节", nav);
        Assert.Contains("book_001/OEBPS/text/ch1.xhtml", nav);
    }

    [Fact]
    public async Task MergeAsync_accepts_xml11_package_declaration()
    {
        var input = CreateEpubWithXml11Package("xml11.epub");
        var output = Path.Combine(_directory, "xml11-output.epub");

        await new EpubMergeService().MergeAsync(new EpubMergeRequest([input], output, "XML 1.1"));

        using var archive = ZipFile.OpenRead(output);
        Assert.Contains("XML 1.1", ReadEntry(archive, "EPUB/nav.xhtml"));
    }

    [Fact]
    public async Task MergeAsync_accepts_utf16_xml11_package_declaration()
    {
        var input = CreateEpubWithUtf16Xml11Package("xml11-utf16.epub");
        var output = Path.Combine(_directory, "xml11-utf16-output.epub");

        await new EpubMergeService().MergeAsync(new EpubMergeRequest([input], output, "XML 1.1 UTF-16"));

        using var archive = ZipFile.OpenRead(output);
        Assert.Contains("UTF-16 标题", ReadEntry(archive, "EPUB/nav.xhtml"));
    }

    [Fact]
    public async Task MergeAsync_rejects_encrypted_epub()
    {
        var input = CreateEpub("encrypted.epub", "加密册", TocType.Nav, "章节");

        using (var archive = ZipFile.Open(input, ZipArchiveMode.Update))
        {
            Write(archive, "META-INF/encryption.xml", "<encryption/>");
        }

        var output = Path.Combine(_directory, "encrypted-output.epub");
        var exception = await Assert.ThrowsAsync<NotSupportedException>(() => new EpubMergeService().MergeAsync(new EpubMergeRequest([input], output, "加密合辑")));

        Assert.Contains("encryption.xml", exception.Message);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task MergeAsync_reports_the_path_for_a_truncated_epub()
    {
        var input = Path.Combine(_directory, "truncated.epub");
        await File.WriteAllBytesAsync(input, [80, 75, 3, 4]);
        var output = Path.Combine(_directory, "truncated-output.epub");

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new EpubMergeService().MergeAsync(new EpubMergeRequest([input], output, "损坏合辑")));

        Assert.Contains(input, exception.Message);
        Assert.Contains("仍在写入或已损坏", exception.Message);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task MergeAsync_omits_navigation_document_from_spine()
    {
        var input = CreateEpubWithNavInSpine("nav-in-spine.epub");
        var output = Path.Combine(_directory, "nav-in-spine-output.epub");

        await new EpubMergeService().MergeAsync(new EpubMergeRequest([input], output, "导航 spine"));

        using var archive = ZipFile.OpenRead(output);
        var opf = ReadEntry(archive, "EPUB/package.opf");
        Assert.DoesNotContain("idref=\"b001-nav\"", opf);
        Assert.Contains("idref=\"b001-ch1\"", opf);
    }

    [Fact]
    public async Task MergeAsync_omits_fallback_when_target_is_excluded_from_manifest()
    {
        var input = CreateEpubWithFallbackToNav("fallback-to-nav.epub");
        var output = Path.Combine(_directory, "fallback-to-nav-output.epub");

        await new EpubMergeService().MergeAsync(new EpubMergeRequest([input], output, "Fallback"));

        using var archive = ZipFile.OpenRead(output);
        Assert.DoesNotContain("fallback=\"b001-nav\"", ReadEntry(archive, "EPUB/package.opf"));
    }

    [Fact]
    public void Validator_rejects_missing_input_and_unsupported_cover()
    {
        Assert.Throws<FileNotFoundException>(() => EpubMergeValidator.Validate(new EpubMergeRequest([Path.Combine(_directory, "missing.epub")], Path.Combine(_directory, "out.epub"), "x")));
        var input = CreateEpub("valid.epub", "有效", TocType.Nav, "章节");
        var cover = Path.Combine(_directory, "cover.bmp");
        File.WriteAllText(cover, "x");
        Assert.Throws<ArgumentException>(() => EpubMergeValidator.Validate(new EpubMergeRequest([input], Path.Combine(_directory, "out.epub"), "x", cover)));
    }

    [Fact]
    public void Validator_rejects_output_path_matching_input()
    {
        var input = CreateEpub("same-path.epub", "有效", TocType.Nav, "章节");

        Assert.Throws<ArgumentException>(() => EpubMergeValidator.Validate(new EpubMergeRequest([input], input, "x")));
    }

    private string CreateEpub(string fileName, string title, TocType tocType, string chapter)
    {
        var path = Path.Combine(_directory, fileName);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        Write(archive, "mimetype", "application/epub+zip", CompressionLevel.NoCompression);
        Write(archive, "META-INF/container.xml", "<container><rootfiles><rootfile full-path=\"OEBPS/content.opf\"/></rootfiles></container>");
        var tocItem = tocType switch
        {
            TocType.Nav => "<item id=\"nav\" href=\"nav.xhtml\" media-type=\"application/xhtml+xml\" properties=\"nav\"/>",
            TocType.Ncx => "<item id=\"ncx\" href=\"toc.ncx\" media-type=\"application/x-dtbncx+xml\"/>",
            _ => string.Empty
        };
        var tocAttribute = tocType == TocType.Ncx ? " toc=\"ncx\"" : string.Empty;
        Write(archive, "OEBPS/content.opf", $"<package><metadata><title>{title}</title></metadata><manifest><item id=\"ch1\" href=\"chapters/ch1.xhtml\" media-type=\"application/xhtml+xml\"/>{tocItem}</manifest><spine{tocAttribute}><itemref idref=\"ch1\"/></spine></package>");
        Write(archive, "OEBPS/chapters/ch1.xhtml", $"<html><head><title>{chapter}</title></head><body><h1>{chapter}</h1></body></html>");
        if (tocType == TocType.Nav) Write(archive, "OEBPS/nav.xhtml", $"<html xmlns:epub=\"http://www.idpf.org/2007/ops\"><body><nav epub:type=\"toc\"><ol><li><a href=\"chapters/ch1.xhtml#start\">{chapter}</a></li></ol></nav></body></html>");
        if (tocType == TocType.Ncx) Write(archive, "OEBPS/toc.ncx", $"<ncx><navMap><navPoint><navLabel><text>{chapter}</text></navLabel><content src=\"chapters/ch1.xhtml#start\"/></navPoint></navMap></ncx>");
        return path;
    }

    private string CreateEpubWithDuplicateManifestIds(string fileName)
    {
        var path = Path.Combine(_directory, fileName);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        Write(archive, "mimetype", "application/epub+zip", CompressionLevel.NoCompression);
        Write(archive, "META-INF/container.xml", "<container><rootfiles><rootfile full-path=\"OEBPS/content.opf\"/></rootfiles></container>");
        Write(archive, "OEBPS/content.opf",
            "<package><metadata><title>重复 ID</title></metadata><manifest><item id=\"Section0001.xhtml\" href=\"Section0001.xhtml\" media-type=\"application/xhtml+xml\"/><item id=\"Section0001.xhtml\" href=\"Section0002.xhtml\" media-type=\"application/xhtml+xml\"/></manifest><spine><itemref idref=\"Section0001.xhtml\"/></spine></package>");
        Write(archive, "OEBPS/Section0001.xhtml", "<html><body><h1>第一节</h1></body></html>");
        Write(archive, "OEBPS/Section0002.xhtml", "<html><body><h1>第二节</h1></body></html>");
        return path;
    }

    private string CreateEpubWithNestedNav(string fileName, string title, string chapter)
    {
        var path = Path.Combine(_directory, fileName);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        Write(archive, "mimetype", "application/epub+zip", CompressionLevel.NoCompression);
        Write(archive, "META-INF/container.xml", "<container><rootfiles><rootfile full-path=\"OEBPS/content.opf\"/></rootfiles></container>");
        Write(archive, "OEBPS/content.opf",
            "<package><metadata><title>" + title + "</title></metadata><manifest><item id=\"ch1\" href=\"text/ch1.xhtml\" media-type=\"application/xhtml+xml\"/><item id=\"nav\" href=\"nav/toc.xhtml\" media-type=\"application/xhtml+xml\" properties=\"nav\"/></manifest><spine><itemref idref=\"ch1\"/></spine></package>");
        Write(archive, "OEBPS/text/ch1.xhtml", "<html><body><h1>" + chapter + "</h1></body></html>");
        Write(archive, "OEBPS/nav/toc.xhtml", "<html xmlns:epub=\"http://www.idpf.org/2007/ops\"><body><nav epub:type=\"toc\"><ol><li><a href=\"../text/ch1.xhtml#start\">" + chapter + "</a></li></ol></nav></body></html>");
        return path;
    }

    private string CreateEpubWithNestedNcx(string fileName, string title, string chapter)
    {
        var path = Path.Combine(_directory, fileName);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        Write(archive, "mimetype", "application/epub+zip", CompressionLevel.NoCompression);
        Write(archive, "META-INF/container.xml", "<container><rootfiles><rootfile full-path=\"OEBPS/content.opf\"/></rootfiles></container>");
        Write(archive, "OEBPS/content.opf",
            "<package><metadata><title>" + title + "</title></metadata><manifest><item id=\"ch1\" href=\"text/ch1.xhtml\" media-type=\"application/xhtml+xml\"/><item id=\"ncx\" href=\"toc/toc.ncx\" media-type=\"application/x-dtbncx+xml\"/></manifest><spine toc=\"ncx\"><itemref idref=\"ch1\"/></spine></package>");
        Write(archive, "OEBPS/text/ch1.xhtml", "<html><body><h1>" + chapter + "</h1></body></html>");
        Write(archive, "OEBPS/toc/toc.ncx", "<ncx><navMap><navPoint><navLabel><text>" + chapter + "</text></navLabel><content src=\"../text/ch1.xhtml#start\"/></navPoint></navMap></ncx>");
        return path;
    }

    private string CreateEpubWithExternalTocLink(string fileName)
    {
        var path = Path.Combine(_directory, fileName);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        Write(archive, "mimetype", "application/epub+zip", CompressionLevel.NoCompression);
        Write(archive, "META-INF/container.xml", "<container><rootfiles><rootfile full-path=\"OEBPS/content.opf\"/></rootfiles></container>");
        Write(archive, "OEBPS/content.opf",
            "<package><metadata><title>外部链接</title></metadata><manifest><item id=\"ch1\" href=\"text/ch1.xhtml\" media-type=\"application/xhtml+xml\"/><item id=\"nav\" href=\"nav.xhtml\" media-type=\"application/xhtml+xml\" properties=\"nav\"/></manifest><spine><itemref idref=\"ch1\"/></spine></package>");
        Write(archive, "OEBPS/text/ch1.xhtml", "<html><body><h1>本地章节</h1></body></html>");
        Write(archive, "OEBPS/nav.xhtml", "<html xmlns:epub=\"http://www.idpf.org/2007/ops\"><body><nav epub:type=\"toc\"><ol><li><a href=\"https://example.test/chapter#start\">外部章节</a></li></ol></nav></body></html>");
        return path;
    }

    private string CreateEpubWithMalformedNavigation(string fileName)
    {
        var path = Path.Combine(_directory, fileName);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        Write(archive, "mimetype", "application/epub+zip", CompressionLevel.NoCompression);
        Write(archive, "META-INF/container.xml", "<container><rootfiles><rootfile full-path=\"OEBPS/content.opf\"/></rootfiles></container>");
        Write(archive, "OEBPS/content.opf",
            "<package><metadata><title>损坏目录</title></metadata><manifest><item id=\"ch1\" href=\"text/ch1.xhtml\" media-type=\"application/xhtml+xml\"/><item id=\"nav\" href=\"nav.xhtml\" media-type=\"application/xhtml+xml\" properties=\"nav\"/></manifest><spine><itemref idref=\"ch1\"/></spine></package>");
        Write(archive, "OEBPS/text/ch1.xhtml", "<html><body><h1>回退章节</h1></body></html>");
        Write(archive, "OEBPS/nav.xhtml", "<html><body><nav><ol><li><a href=\"text/ch1.xhtml\">损坏");
        return path;
    }

    private string CreateEpubWithXml11Package(string fileName)
    {
        var path = Path.Combine(_directory, fileName);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        Write(archive, "mimetype", "application/epub+zip", CompressionLevel.NoCompression);
        Write(archive, "META-INF/container.xml", "<container><rootfiles><rootfile full-path=\"OEBPS/content.opf\"/></rootfiles></container>");
        Write(archive, "OEBPS/content.opf", "<?xml version=\"1.1\" encoding=\"UTF-8\"?><package><metadata><title>XML 1.1</title></metadata><manifest><item id=\"ch1\" href=\"text/ch1.xhtml\" media-type=\"application/xhtml+xml\"/></manifest><spine><itemref idref=\"ch1\"/></spine></package>");
        Write(archive, "OEBPS/text/ch1.xhtml", "<html><body><h1>XML 1.1</h1></body></html>");
        return path;
    }

    private string CreateEpubWithUtf16Xml11Package(string fileName)
    {
        var path = Path.Combine(_directory, fileName);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        Write(archive, "mimetype", "application/epub+zip", CompressionLevel.NoCompression);
        Write(archive, "META-INF/container.xml", "<container><rootfiles><rootfile full-path=\"OEBPS/content.opf\"/></rootfiles></container>");
        var opf = "<?xml version=\"1.1\" encoding=\"UTF-16\"?><package><metadata><title>UTF-16 标题</title></metadata><manifest><item id=\"ch1\" href=\"text/ch1.xhtml\" media-type=\"application/xhtml+xml\"/></manifest><spine><itemref idref=\"ch1\"/></spine></package>";
        WriteBytes(archive, "OEBPS/content.opf", Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(opf)).ToArray());
        Write(archive, "OEBPS/text/ch1.xhtml", "<html><body><h1>UTF-16 标题</h1></body></html>");
        return path;
    }

    private string CreateEpubWithNavInSpine(string fileName)
    {
        var path = Path.Combine(_directory, fileName);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        Write(archive, "mimetype", "application/epub+zip", CompressionLevel.NoCompression);
        Write(archive, "META-INF/container.xml", "<container><rootfiles><rootfile full-path=\"OEBPS/content.opf\"/></rootfiles></container>");
        Write(archive, "OEBPS/content.opf",
            "<package><metadata><title>导航 spine</title></metadata><manifest><item id=\"ch1\" href=\"text/ch1.xhtml\" media-type=\"application/xhtml+xml\"/><item id=\"nav\" href=\"nav.xhtml\" media-type=\"application/xhtml+xml\" properties=\"nav\"/></manifest><spine><itemref idref=\"ch1\"/><itemref idref=\"nav\"/></spine></package>");
        Write(archive, "OEBPS/text/ch1.xhtml", "<html><body><h1>章节</h1></body></html>");
        Write(archive, "OEBPS/nav.xhtml", "<html xmlns:epub=\"http://www.idpf.org/2007/ops\"><body><nav epub:type=\"toc\"><ol><li><a href=\"text/ch1.xhtml\">章节</a></li></ol></nav></body></html>");
        return path;
    }

    private string CreateEpubWithFallbackToNav(string fileName)
    {
        var path = Path.Combine(_directory, fileName);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        Write(archive, "mimetype", "application/epub+zip", CompressionLevel.NoCompression);
        Write(archive, "META-INF/container.xml", "<container><rootfiles><rootfile full-path=\"OEBPS/content.opf\"/></rootfiles></container>");
        Write(archive, "OEBPS/content.opf",
            "<package><metadata><title>Fallback</title></metadata><manifest><item id=\"ch1\" href=\"text/ch1.xhtml\" media-type=\"application/xhtml+xml\"/><item id=\"binary\" href=\"content.bin\" media-type=\"application/octet-stream\" fallback=\"nav\"/><item id=\"nav\" href=\"nav.xhtml\" media-type=\"application/xhtml+xml\" properties=\"nav\"/></manifest><spine><itemref idref=\"ch1\"/></spine></package>");
        Write(archive, "OEBPS/text/ch1.xhtml", "<html><body><h1>章节</h1></body></html>");
        Write(archive, "OEBPS/content.bin", "binary");
        Write(archive, "OEBPS/nav.xhtml", "<html xmlns:epub=\"http://www.idpf.org/2007/ops\"><body><nav epub:type=\"toc\"><ol><li><a href=\"text/ch1.xhtml\">章节</a></li></ol></nav></body></html>");
        return path;
    }

    private static void Write(ZipArchive archive, string name, string value, CompressionLevel compression = CompressionLevel.Optimal)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name, compression).Open());
        writer.Write(value);
    }

    private static void WriteBytes(ZipArchive archive, string name, byte[] value, CompressionLevel compression = CompressionLevel.Optimal)
    {
        using var stream = archive.CreateEntry(name, compression).Open();
        stream.Write(value);
    }

    private static string ReadEntry(ZipArchive archive, string name)
    {
        using var reader = new StreamReader(archive.GetEntry(name)!.Open());
        return reader.ReadToEnd();
    }
    private enum TocType { Nav, Ncx, None }
}
