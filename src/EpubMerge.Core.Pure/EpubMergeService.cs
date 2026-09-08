using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace EpubMerge.Core.Pure;

/// <summary>Creates a standards-compatible EPUB 3 container from one or more source EPUBs.</summary>
/// <remarks>The implementation uses only the base class library and rejects encrypted EPUB resources.</remarks>
public sealed class EpubMergeService : IEpubMergeService
{
    // ReSharper disable once InconsistentNaming
    private const string ContainerXml = """
                                        <?xml version="1.0" encoding="UTF-8"?>
                                        <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
                                          <rootfiles>
                                            <rootfile full-path="EPUB/package.opf" media-type="application/oebps-package+xml"/>
                                          </rootfiles>
                                        </container>
                                        """;

    // ReSharper disable once InconsistentNaming
    private const string NavCss = """
                                  body { font-family: sans-serif; line-height: 1.5; }
                                  nav#toc ol { list-style-type: none; padding-left: 1.25em; }
                                  nav#toc > ol { padding-left: 0; }
                                  a { text-decoration: none; color: inherit; }
                                  """;

    /// <summary>Merges source EPUBs asynchronously and writes the result atomically.</summary>
    /// <param name="request">The source files, output path, title, and optional cover.</param>
    /// <param name="progress">Optional progress reporter; reports source reading and copying.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>
    ///     A task that completes when the merged EPUB is ready at <paramref name="request" />.
    ///     <see cref="EpubMergeRequest.OutputPath" />.
    /// </returns>
    /// <remarks>A temporary file in the output directory is moved into place only after the ZIP is complete.</remarks>
    public Task MergeAsync(EpubMergeRequest request, IProgress<EpubMergeProgress>? progress = null,
        CancellationToken cancellationToken = default(CancellationToken))
    {
        EpubMergeValidator.Validate(request);
        return Task.Run(() => Merge(request, progress, cancellationToken), cancellationToken);
    }

    private static void Merge(EpubMergeRequest request, IProgress<EpubMergeProgress>? progress,
        CancellationToken cancellationToken)
    {
        var books = new List<Book>();

        for (var i = 0; i < request.InputPaths.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new EpubMergeProgress(i, request.InputPaths.Count, $"正在读取第 {i + 1} 本 EPUB…", EpubMergeProgressStage.Reading));
            books.Add(ReadBook(request.InputPaths[i], i + 1, cancellationToken));
        }

        var outputPath = Path.GetFullPath(request.OutputPath);
        var outputDirectory = Path.GetDirectoryName(outputPath);
        Directory.CreateDirectory(outputDirectory!);
        var temporaryPath = Path.Combine(outputDirectory!, $".{Path.GetFileName(outputPath)}.{Guid.NewGuid():N}.tmp");
        var written = new HashSet<string>(StringComparer.Ordinal);
        var uid = $"urn:uuid:{Guid.NewGuid()}";

        try
        {
            // Build the complete archive beside the destination so cancellation or a failed source never leaves a partial output.
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                WriteEntry(archive, "mimetype", "application/epub+zip", true);
                WriteEntry(archive, "META-INF/container.xml", ContainerXml);
                WriteEntry(archive, "EPUB/package.opf", BuildOpf(request.Title, uid, books, request.CoverPath));
                WriteEntry(archive, "EPUB/nav.xhtml", BuildNavHtml(request.Title, MergeToc(books)));
                WriteEntry(archive, "EPUB/toc.ncx", BuildNcx(request.Title, uid, MergeToc(books)));
                WriteEntry(archive, "EPUB/style/nav.css", NavCss);
                written.UnionWith(["mimetype", "META-INF/container.xml", "EPUB/package.opf", "EPUB/nav.xhtml", "EPUB/toc.ncx", "EPUB/style/nav.css"]);

                AddManualCover(archive, request.CoverPath, written, cancellationToken);

                for (var i = 0; i < books.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    AddSourceFiles(archive, books[i], written, cancellationToken);
                    progress?.Report(new EpubMergeProgress(i + 1, books.Count, $"已合并 {i + 1}/{books.Count} 本 EPUB", EpubMergeProgressStage.Merging));
                }
            }

            File.Move(temporaryPath, outputPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static Book ReadBook(string path, int index, CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(path);
        RejectEncryptedEpub(archive, path);
        var opfPath = FindRootFile(archive);
        var opfDirectory = PosixDirectory(opfPath);
        var root = ReadXml(archive, opfPath);
        var manifest = ParseManifest(root);
        var spine = ParseSpine(root);
        var book = new Book(path, index, FindTitle(root, Path.GetFileNameWithoutExtension(path)), opfPath, opfDirectory,
            manifest, spine, FindCoverId(root, manifest));

        // Content links are rewritten into a per-book directory; this keeps identical source names from colliding.
        foreach (var itemRef in spine)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (book.ItemsById.TryGetValue(itemRef.IdRef, out var item) && IsXhtml(item.MediaType))
            {
                book.FirstContentHref = $"{book.CopyPrefix}/{JoinHref(opfDirectory, item.Href).Path}";
                break;
            }
        }

        var nav = manifest.FirstOrDefault(item => HasProperty(item.Properties, "nav"));

        if (nav is not null)
        {
            try { book.Toc = ParseNavToc(book, archive, nav); }
            catch (XmlException) { }
        }

        if (book.Toc.Count == 0)
        {
            var ncx = FindNcx(root, manifest);

            if (ncx is not null)
            {
                try { book.Toc = ParseNcxToc(book, archive, ncx); }
                catch (XmlException) { }
            }
        }

        if (book.Toc.Count == 0)
        {
            book.Toc = BuildFallbackToc(book, archive);
        }
        return book;
    }

    private static string FindRootFile(ZipArchive archive)
    {
        var root = ReadXml(archive, "META-INF/container.xml");
        var path = root.Descendants().FirstOrDefault(e => e.Name.LocalName == "rootfile")?.Attribute("full-path")?.Value;
        return !string.IsNullOrWhiteSpace(path) ? path : throw new InvalidDataException("container.xml 中没有 rootfile。");
    }

    private static void RejectEncryptedEpub(ZipArchive archive, string path)
    {
        if (FindEntryName(archive, "META-INF/encryption.xml") is null) return;

        // TODO: Support EPUB font obfuscation by deobfuscating with the source identifier,
        // re-obfuscating with the merged identifier, and rebuilding encryption.xml.
        throw new NotSupportedException($"不支持包含 META-INF/encryption.xml 的 EPUB：{path}");
    }

    private static XDocument ReadXml(ZipArchive archive, string path)
    {
        var actualPath = FindEntryName(archive, path) ?? throw new InvalidDataException($"EPUB 中找不到文件：{path}");
        var entry = archive.GetEntry(actualPath)!;

        try
        {
            using var xmlStream = entry.Open();
            return XDocument.Load(xmlStream, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException)
        {
            using var source = entry.Open();
            using var buffer = new MemoryStream();
            source.CopyTo(buffer);
            var xml10Bytes = DowngradeXml11Declaration(buffer.ToArray());
            if (xml10Bytes is null) throw;
            using var xmlStream = new MemoryStream(xml10Bytes, false);
            return XDocument.Load(xmlStream, LoadOptions.PreserveWhitespace);
        }
    }

    private static byte[]? DowngradeXml11Declaration(byte[] bytes)
    {
        foreach (var encoding in new[] { Encoding.UTF8, Encoding.Unicode, Encoding.BigEndianUnicode })
        {
            foreach (var quote in new[] { '"', '\'' })
            {
                var xml11 = encoding.GetBytes($"version={quote}1.1{quote}");
                var xml10 = encoding.GetBytes($"version={quote}1.0{quote}");
                var replacement = ReplaceInHeader(bytes, xml11, xml10);
                if (replacement is not null) return replacement;
            }
        }
        return null;
    }

    private static byte[]? ReplaceInHeader(byte[] bytes, byte[] source, byte[] replacement)
    {
        var searchLength = Math.Min(bytes.Length - source.Length, 1024);

        for (var index = 0; index <= searchLength; index++)
        {
            if (!bytes.AsSpan(index, source.Length).SequenceEqual(source)) continue;
            var copy = bytes.ToArray();
            replacement.CopyTo(copy, index);
            return copy;
        }
        return null;
    }

    private static List<ManifestItem> ParseManifest(XDocument document)
    {
        var manifest = document.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "manifest")
            ?? throw new InvalidDataException("OPF 缺少 manifest。");
        var items = manifest.Elements().Where(e => e.Name.LocalName == "item")
            .Select(e => new ManifestItem(
                (string?)e.Attribute("id") ?? string.Empty,
                (string?)e.Attribute("href") ?? string.Empty,
                (string?)e.Attribute("media-type") ?? string.Empty,
                (string?)e.Attribute("properties") ?? string.Empty,
                (string?)e.Attribute("fallback") ?? string.Empty))
            .Where(item => item.Id.Length > 0 && item.Href.Length > 0 && item.MediaType.Length > 0)
            .ToList();

        // EPUB requires manifest IDs to be unique, but real-world books can
        // contain duplicates. Preserve the first ID for existing spine idrefs
        // and rename later entries deterministically for the merged OPF.
        var usedIds = new HashSet<string>(StringComparer.Ordinal);
        var normalized = new List<ManifestItem>(items.Count);

        foreach (var item in items)
        {
            var id = item.Id;

            if (!usedIds.Add(id))
            {
                var suffix = 2;

                do
                {
                    id = $"{item.Id}-{suffix++}";
                } while (!usedIds.Add(id));
                normalized.Add(item with { Id = id });
            }
            else
            {
                normalized.Add(item);
            }
        }
        return normalized;
    }

    private static List<SpineItem> ParseSpine(XDocument document)
    {
        var spine = document.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "spine")
            ?? throw new InvalidDataException("OPF 缺少 spine。");
        return spine.Elements().Where(e => e.Name.LocalName == "itemref")
            .Select(e => new SpineItem((string?)e.Attribute("idref") ?? string.Empty, (string?)e.Attribute("linear") ?? string.Empty))
            .Where(item => item.IdRef.Length > 0).ToList();
    }

    private static string FindTitle(XDocument document, string fallback)
    {
        var metadata = document.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "metadata");
        var title = metadata?.Descendants().FirstOrDefault(e => e.Name.LocalName == "title")?.Value;
        return CleanTitle(title, fallback);
    }

    private static string FindCoverId(XDocument document, List<ManifestItem> manifest)
    {
        var propertyCover = manifest.FirstOrDefault(i => HasProperty(i.Properties, "cover-image"));
        if (propertyCover is not null) return propertyCover.Id;
        var metadata = document.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "metadata");
        var legacyCover = metadata?.Elements().FirstOrDefault(e => e.Name.LocalName == "meta" &&
            string.Equals((string?)e.Attribute("name"), "cover", StringComparison.OrdinalIgnoreCase))?.Attribute("content")?.Value;
        if (!string.IsNullOrWhiteSpace(legacyCover) && manifest.Any(i => i.Id == legacyCover)) return legacyCover;
        return manifest.FirstOrDefault(i => IsImage(i.MediaType) && $"{i.Id} {i.Href}".Contains("cover", StringComparison.OrdinalIgnoreCase) ||
            IsImage(i.MediaType) && $"{i.Id} {i.Href}".Contains("封面", StringComparison.Ordinal))?.Id ?? string.Empty;
    }

    private static ManifestItem? FindNcx(XDocument document, List<ManifestItem> manifest)
    {
        var tocId = document.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "spine")?.Attribute("toc")?.Value;
        return manifest.FirstOrDefault(i => i.Id == tocId) ?? manifest.FirstOrDefault(i => i.MediaType == "application/x-dtbncx+xml");
    }

    private static List<TocNode> ParseNavToc(Book book, ZipArchive archive, ManifestItem nav)
    {
        var path = JoinHref(book.OpfDirectory, nav.Href).Path;
        var entryName = FindEntryName(archive, path);
        if (entryName is null) return [];
        var document = ReadXml(archive, entryName);
        var navElement = document.Descendants().FirstOrDefault(e => e.Name.LocalName == "nav" &&
            (AttributeByLocalName(e, "type").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Contains("toc") ||
                ((string?)e.Attribute("role"))?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Contains("doc-toc") == true));
        var list = navElement?.Elements().FirstOrDefault(e => e.Name.LocalName == "ol");
        return list is null ? [] : ParseOrderedList(book, list, PosixDirectory(path));
    }

    private static List<TocNode> ParseOrderedList(Book book, XElement list, string baseDirectory)
    {
        return list.Elements().Where(e => e.Name.LocalName == "li")
            .Select(li =>
            {
                var label = li.Elements().FirstOrDefault(e => e.Name.LocalName is "a" or "span");
                var children = li.Elements().FirstOrDefault(e => e.Name.LocalName == "ol");
                var href = label?.Name.LocalName == "a" ? (string?)label.Attribute("href") : null;
                return new TocNode(CleanTitle(label?.Value, "Untitled"), href is null ? string.Empty : LinkToNewHref(book, href, baseDirectory),
                    children is null ? [] : ParseOrderedList(book, children, baseDirectory));
            }).ToList();
    }

    private static List<TocNode> ParseNcxToc(Book book, ZipArchive archive, ManifestItem ncx)
    {
        var path = JoinHref(book.OpfDirectory, ncx.Href).Path;
        var entryName = FindEntryName(archive, path);
        if (entryName is null) return [];
        var navMap = ReadXml(archive, entryName).Descendants().FirstOrDefault(e => e.Name.LocalName == "navMap");
        return navMap is null ? [] : navMap.Elements().Where(e => e.Name.LocalName == "navPoint").Select(point => ParseNavPoint(book, point, PosixDirectory(path))).ToList();
    }

    private static TocNode ParseNavPoint(Book book, XElement point, string baseDirectory)
    {
        var title = CleanTitle(point.Elements().FirstOrDefault(e => e.Name.LocalName == "navLabel")?.Value, "Untitled");
        var src = point.Elements().FirstOrDefault(e => e.Name.LocalName == "content")?.Attribute("src")?.Value;
        return new TocNode(title, string.IsNullOrWhiteSpace(src) ? string.Empty : LinkToNewHref(book, src, baseDirectory),
            point.Elements().Where(e => e.Name.LocalName == "navPoint").Select(child => ParseNavPoint(book, child, baseDirectory)).ToList());
    }

    private static List<TocNode> BuildFallbackToc(Book book, ZipArchive archive)
    {
        var result = new List<TocNode>();

        for (var i = 0; i < book.Spine.Count; i++)
        {
            var item = book.ItemsById.GetValueOrDefault(book.Spine[i].IdRef);
            if (item is null || !IsXhtml(item.MediaType)) continue;
            var path = JoinHref(book.OpfDirectory, item.Href).Path;
            var entryName = FindEntryName(archive, path);
            result.Add(new TocNode(GuessContentTitle(archive, entryName, $"Chapter {i + 1}"), $"{book.CopyPrefix}/{path}", []));
        }
        return result;
    }

    private static string GuessContentTitle(ZipArchive archive, string? entryName, string fallback)
    {
        if (entryName is null) return fallback;

        try
        {
            var document = ReadXml(archive, entryName);

            foreach (var name in new[] { "h1", "h2", "title" })
            {
                var title = CleanTitle(document.Descendants().FirstOrDefault(e => e.Name.LocalName == name)?.Value, string.Empty);
                if (title.Length > 0) return title;
            }
        }
        catch (InvalidDataException) { }
        catch (XmlException) { }
        return fallback;
    }

    private static void AddSourceFiles(ZipArchive destination, Book book, HashSet<string> written, CancellationToken token)
    {
        using var source = ZipFile.OpenRead(book.Path);

        foreach (var entry in source.Entries)
        {
            token.ThrowIfCancellationRequested();
            var name = entry.FullName.Replace('\\', '/');
            if (name.EndsWith('/') || name == "mimetype" || name.StartsWith("META-INF/", StringComparison.Ordinal)) continue;
            var target = $"EPUB/{book.CopyPrefix}/{name}";
            if (!written.Add(target)) continue;
            var targetEntry = destination.CreateEntry(target, CompressionLevel.Optimal);
            using var input = entry.Open();
            using var output = targetEntry.Open();
            CopyTo(input, output, token);
        }
    }

    private static void AddManualCover(ZipArchive archive, string? coverPath, HashSet<string> written, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(coverPath)) return;
        var zipPath = ManualCoverZipPath(coverPath);
        if (!written.Add(zipPath)) return;
        var entry = archive.CreateEntry(zipPath, CompressionLevel.Optimal);
        using var input = File.OpenRead(coverPath);
        using var output = entry.Open();
        CopyTo(input, output, token);
    }

    private static void CopyTo(Stream input, Stream output, CancellationToken token)
    {
        var buffer = new byte[64 * 1024];
        int read;

        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            token.ThrowIfCancellationRequested();
            output.Write(buffer, 0, read);
        }
    }

    private static List<TocNode> MergeToc(IEnumerable<Book> books)
    {
        // Keep each source book as a top-level node so duplicate chapter titles remain distinguishable.
        return books.Select(book => new TocNode(book.Title,
            book.FirstContentHref.Length > 0 ? book.FirstContentHref : book.Toc.FirstOrDefault()?.Href ?? string.Empty, book.Toc)).ToList();
    }

    private static string BuildOpf(string title, string uid, List<Book> books, string? coverPath)
    {
        var modified = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var manualType = string.IsNullOrWhiteSpace(coverPath) ? string.Empty : CoverMediaType(coverPath);
        var coverId = manualType.Length > 0 ? "manual-cover-image" : FindMergedCoverItemId(books);
        var manifest = new List<string>
        {
            "    <item id=\"nav\" href=\"nav.xhtml\" media-type=\"application/xhtml+xml\" properties=\"nav\"/>",
            "    <item id=\"ncx\" href=\"toc.ncx\" media-type=\"application/x-dtbncx+xml\"/>",
            "    <item id=\"nav-css\" href=\"style/nav.css\" media-type=\"text/css\"/>"
        };

        if (manualType.Length > 0)
        {
            manifest.Add($"    <item id=\"manual-cover-image\" href=\"{EscapeAttribute(ManualCoverHref(coverPath!))}\" media-type=\"{manualType}\" properties=\"cover-image\"/>");
        }
        var spine = new List<string>();

        foreach (var book in books)
        {
            var includedItems = book.Manifest
                .Where(item => item.MediaType != "application/x-dtbncx+xml" && !HasProperty(item.Properties, "nav"))
                .ToList();
            var includedIds = includedItems.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);

            foreach (var item in includedItems)
            {
                var itemId = MergedItemId(book, item);
                var properties = item.Properties.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                    .Where(property => property is not "nav" and not "cover-image").ToList();
                if (itemId == coverId) properties.Add("cover-image");
                var attributes = new List<string> { $"id=\"{EscapeAttribute(itemId)}\"", $"href=\"{EscapeAttribute(ManifestHref(book, item))}\"", $"media-type=\"{EscapeAttribute(item.MediaType)}\"" };
                if (properties.Count > 0) attributes.Add($"properties=\"{EscapeAttribute(string.Join(' ', properties))}\"");

                if (item.Fallback.Length > 0 && includedIds.Contains(item.Fallback))
                {
                    attributes.Add($"fallback=\"b{book.Index:000}-{EscapeAttribute(item.Fallback)}\"");
                }
                manifest.Add($"    <item {string.Join(' ', attributes)}/>");
            }

            foreach (var itemRef in book.Spine)
            {
                if (!book.ItemsById.TryGetValue(itemRef.IdRef, out var item) ||
                    item.MediaType == "application/x-dtbncx+xml" || HasProperty(item.Properties, "nav"))
                {
                    continue;
                }
                var linear = itemRef.Linear.Length > 0 ? $" linear=\"{EscapeAttribute(itemRef.Linear)}\"" : string.Empty;
                spine.Add($"    <itemref idref=\"{EscapeAttribute(MergedItemId(book, item))}\"{linear}/>");
            }
        }
        var coverMeta = coverId.Length > 0 ? $"    <meta name=\"cover\" content=\"{EscapeAttribute(coverId)}\"/>" : string.Empty;
        return $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="pub-id" prefix="rendition: http://www.idpf.org/vocab/rendition/#">
                  <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                    <dc:identifier id="pub-id">{EscapeText(uid)}</dc:identifier>
                    <dc:title>{EscapeText(title)}</dc:title>
                    <dc:language>zh-CN</dc:language>
                    <meta property="dcterms:modified">{modified}</meta>
                {coverMeta}
                  </metadata>
                  <manifest>
                {string.Join(Environment.NewLine, manifest)}
                  </manifest>
                  <spine toc="ncx">
                {string.Join(Environment.NewLine, spine)}
                  </spine>
                </package>
                """;
    }

    private static string BuildNavHtml(string title, List<TocNode> toc)
    {
        return $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <!DOCTYPE html>
                <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops" lang="zh-CN" xml:lang="zh-CN">
                <head><meta charset="UTF-8"/><title>{EscapeText(title)}</title><link rel="stylesheet" type="text/css" href="style/nav.css"/></head>
                <body><nav id="toc" epub:type="toc" role="doc-toc"><h1>{EscapeText(title)}</h1>
                {RenderNavNodes(toc, 2)}
                </nav></body></html>
                """;
    }

    private static string RenderNavNodes(List<TocNode> nodes, int level)
    {
        var indent = new string(' ', level);
        var lines = new List<string> { $"{indent}<ol>" };

        foreach (var node in nodes)
        {
            var label = EscapeText(node.Title);
            var body = node.Href.Length == 0 ? $"<span>{label}</span>" : $"<a href=\"{EscapeAttribute(node.Href)}\">{label}</a>";

            if (node.Children.Count == 0)
            {
                lines.Add($"{indent}  <li>{body}</li>");
            }
            else
            {
                lines.Add($"{indent}  <li>{body}");
                lines.Add(RenderNavNodes(node.Children, level + 4));
                lines.Add($"{indent}  </li>");
            }
        }
        lines.Add($"{indent}</ol>");
        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildNcx(string title, string uid, List<TocNode> toc)
    {
        var order = 0;
        var points = RenderNcxNodes(toc, ref order, 4);
        return $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <ncx xmlns="http://www.daisy.org/z3986/2005/ncx/" version="2005-1">
                  <head><meta name="dtb:uid" content="{EscapeAttribute(uid)}"/><meta name="dtb:depth" content="3"/><meta name="dtb:totalPageCount" content="0"/><meta name="dtb:maxPageNumber" content="0"/></head>
                  <docTitle><text>{EscapeText(title)}</text></docTitle>
                  <navMap>
                {points}
                  </navMap>
                </ncx>
                """;
    }

    private static string RenderNcxNodes(List<TocNode> nodes, ref int order, int level)
    {
        var lines = new List<string>();
        var indent = new string(' ', level);

        foreach (var node in nodes)
        {
            order++;
            lines.Add($"{indent}<navPoint id=\"navPoint-{order}\" playOrder=\"{order}\">");
            lines.Add($"{indent}  <navLabel><text>{EscapeText(node.Title)}</text></navLabel>");
            lines.Add($"{indent}  <content src=\"{EscapeAttribute(node.Href)}\"/>");
            if (node.Children.Count > 0) lines.Add(RenderNcxNodes(node.Children, ref order, level + 2));
            lines.Add($"{indent}</navPoint>");
        }
        return string.Join(Environment.NewLine, lines);
    }

    private static void WriteEntry(ZipArchive archive, string path, string text, bool stored = false)
    {
        var bytes = new UTF8Encoding(false).GetBytes(text);
        var entry = archive.CreateEntry(path, stored ? CompressionLevel.NoCompression : CompressionLevel.Optimal);
        using var output = entry.Open();
        output.Write(bytes, 0, bytes.Length);
    }

    private static string? FindEntryName(ZipArchive archive, string path)
    {
        path = path.Replace('\\', '/').TrimStart('/');
        if (archive.GetEntry(path) is not null) return path;
        var encoded = path.Replace(" ", "%20", StringComparison.Ordinal);
        if (archive.GetEntry(encoded) is not null) return encoded;
        return archive.Entries.FirstOrDefault(e => string.Equals(e.FullName, path, StringComparison.OrdinalIgnoreCase))?.FullName;
    }

    private static (string Path, string Fragment) JoinHref(string baseDirectory, string href)
    {
        // Resolve links relative to their owning document and preserve #fragment targets separately.
        var hash = href.IndexOf('#');
        var path = Uri.UnescapeDataString(hash >= 0 ? href[..hash] : href);
        var fragment = hash >= 0 ? href[(hash + 1)..] : string.Empty;
        var combined = path.StartsWith('/') ? path.TrimStart('/') : string.Join('/', new[] { baseDirectory, path }.Where(s => s.Length > 0));
        return (NormalizePosix(combined), fragment);
    }

    private static string LinkToNewHref(Book book, string href, string baseDirectory)
    {
        if (Uri.TryCreate(href, UriKind.Absolute, out _) || href.StartsWith("//", StringComparison.Ordinal)) return href;
        var (path, fragment) = JoinHref(baseDirectory, href);
        if (path.Length == 0) path = book.FirstContentHref.Replace(book.CopyPrefix + "/", string.Empty, StringComparison.Ordinal);
        return $"{book.CopyPrefix}/{path}" + (fragment.Length > 0 ? $"#{fragment}" : string.Empty);
    }

    private static string ManifestHref(Book book, ManifestItem item)
    {
        return $"{book.CopyPrefix}/{JoinHref(book.OpfDirectory, item.Href).Path}";
    }
    private static string MergedItemId(Book book, ManifestItem item)
    {
        return $"b{book.Index:000}-{item.Id}";
    }
    private static string FindMergedCoverItemId(IEnumerable<Book> books)
    {
        foreach (var book in books)
        {
            var item = book.ItemsById.GetValueOrDefault(book.CoverId);

            if (item is not null && item.MediaType != "application/x-dtbncx+xml" && !HasProperty(item.Properties, "nav"))
            {
                return MergedItemId(book, item);
            }
        }
        return string.Empty;
    }
    private static bool IsXhtml(string mediaType)
    {
        return mediaType is "application/xhtml+xml" or "text/html" or "application/x-dtbook+xml";
    }
    private static bool IsImage(string mediaType)
    {
        return mediaType.StartsWith("image/", StringComparison.Ordinal);
    }
    private static bool HasProperty(string properties, string property)
    {
        return properties.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Contains(property, StringComparer.Ordinal);
    }
    private static string AttributeByLocalName(XElement element, string name)
    {
        return element.Attributes().FirstOrDefault(a => a.Name.LocalName == name)?.Value ?? string.Empty;
    }
    private static string PosixDirectory(string path)
    {
        var index = path.LastIndexOf('/');
        return index < 0 ? string.Empty : path[..index];
    }
    private static string NormalizePosix(string path)
    {
        return string.Join('/', path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries).Aggregate(new List<string>(), (parts, segment) =>
        {
            if (segment == ".") return parts;

            if (segment == "..")
            {
                if (parts.Count > 0) parts.RemoveAt(parts.Count - 1);
                return parts;
            }
            parts.Add(segment);
            return parts;
        }));
    }
    private static string CleanTitle(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
    private static string EscapeText(string? value)
    {
        return SecurityElement.Escape(value ?? string.Empty);
    }
    private static string EscapeAttribute(string? value)
    {
        return EscapeText(value).Replace("\"", "&quot;", StringComparison.Ordinal).Replace("'", "&apos;", StringComparison.Ordinal);
    }
    private static string CoverMediaType(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch { ".jpg" or ".jpeg" => "image/jpeg", ".png" => "image/png", ".gif" => "image/gif", ".webp" => "image/webp", ".svg" => "image/svg+xml", _ => string.Empty };
    }
    private static string ManualCoverZipPath(string path)
    {
        return "EPUB/cover" + (Path.GetExtension(path).Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ? ".jpg" : Path.GetExtension(path).ToLowerInvariant());
    }
    private static string ManualCoverHref(string path)
    {
        return ManualCoverZipPath(path).Replace("EPUB/", string.Empty, StringComparison.Ordinal);
    }

    private sealed record ManifestItem(string Id, string Href, string MediaType, string Properties, string Fallback);
    private sealed record SpineItem(string IdRef, string Linear);
    private sealed record TocNode(string Title, string Href, List<TocNode> Children);

    [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Local")]
    private sealed class Book
    {
        public Book(string path, int index, string title, string opfPath, string opfDirectory, List<ManifestItem> manifest, List<SpineItem> spine, string coverId)
        {
            Path = path;
            Index = index;
            Title = title;
            OpfPath = opfPath;
            OpfDirectory = opfDirectory;
            Manifest = manifest;
            Spine = spine;
            CoverId = coverId;
            ItemsById = manifest.ToDictionary(i => i.Id, StringComparer.Ordinal);
            CopyPrefix = $"book_{index:000}";
        }
        public string Path { get; }
        public int Index { get; }
        public string Title { get; }
        public string OpfPath { get; }
        public string OpfDirectory { get; }
        public List<ManifestItem> Manifest { get; }
        public List<SpineItem> Spine { get; }
        public string CoverId { get; }
        public Dictionary<string, ManifestItem> ItemsById { get; }
        public string CopyPrefix { get; }
        public string FirstContentHref { get; set; } = string.Empty;
        public List<TocNode> Toc { get; set; } = [];
    }
}
