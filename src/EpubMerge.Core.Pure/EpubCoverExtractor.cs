using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace EpubMerge.Core.Pure;

public interface IEpubCoverExtractor
{
    ExtractedCover? ExtractCover(string epubPath);
}

public sealed record ExtractedCover(byte[] ImageData, string MimeType, string SuggestedExtension);

public sealed class EpubCoverExtractor : IEpubCoverExtractor
{
    private static readonly IReadOnlyDictionary<string, (string MimeType, string Extension)> ImageTypes =
        new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["image/jpeg"] = ("image/jpeg", ".jpg"),
            ["image/jpg"] = ("image/jpeg", ".jpg"),
            ["image/png"] = ("image/png", ".png"),
            ["image/webp"] = ("image/webp", ".webp"),
            ["image/gif"] = ("image/gif", ".gif"),
            ["image/svg+xml"] = ("image/svg+xml", ".svg")
        };

    public ExtractedCover? ExtractCover(string epubPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(epubPath);
        if (!File.Exists(epubPath)) throw new FileNotFoundException($"找不到 EPUB 文件：{epubPath}", epubPath);

        using var archive = ZipFile.OpenRead(epubPath);
        if (archive.GetEntry("META-INF/encryption.xml") is not null)
            throw new NotSupportedException($"不支持包含 META-INF/encryption.xml 的 EPUB：{epubPath}");

        var container = ReadXml(archive, "META-INF/container.xml");
        var opfPath = container.Descendants().FirstOrDefault(e => e.Name.LocalName == "rootfile")?.Attribute("full-path")?.Value;
        if (string.IsNullOrWhiteSpace(opfPath)) throw new InvalidDataException("container.xml 中没有 rootfile。");

        var opf = ReadXml(archive, opfPath);
        var opfDirectory = DirectoryOf(opfPath);
        var manifest = opf.Descendants().Where(e => e.Name.LocalName == "item")
            .Select(e => new ManifestItem(
                (string?)e.Attribute("id") ?? string.Empty,
                (string?)e.Attribute("href") ?? string.Empty,
                (string?)e.Attribute("media-type") ?? string.Empty,
                (string?)e.Attribute("properties") ?? string.Empty))
            .Where(item => item.Id.Length > 0 && item.Href.Length > 0)
            .ToList();

        var cover = manifest.FirstOrDefault(item => HasProperty(item.Properties, "cover-image"));
        if (cover is null)
        {
            var content = opf.Descendants().FirstOrDefault(e => e.Name.LocalName == "meta" &&
                string.Equals((string?)e.Attribute("name"), "cover", StringComparison.OrdinalIgnoreCase))?.Attribute("content")?.Value;
            cover = manifest.FirstOrDefault(item => string.Equals(item.Id, content, StringComparison.Ordinal));
        }

        cover ??= manifest.FirstOrDefault(item => IsSupportedImage(item.MediaType, item.Href) &&
            $"{item.Id} {item.Href}".Contains("cover", StringComparison.OrdinalIgnoreCase));
        cover ??= manifest.FirstOrDefault(item => IsSupportedImage(item.MediaType, item.Href) &&
            $"{item.Id} {item.Href}".Contains("封面", StringComparison.Ordinal));

        if (cover is null) return null;
        var resourcePath = Join(opfDirectory, cover.Href);
        var entry = FindEntry(archive, resourcePath);
        if (entry is null || !TryGetImageType(cover.MediaType, cover.Href, out var type)) return null;

        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        if (buffer.Length == 0) return null;
        return new ExtractedCover(buffer.ToArray(), type.MimeType, type.Extension);
    }

    private static bool TryGetImageType(string mediaType, string href, out (string MimeType, string Extension) type)
    {
        if (ImageTypes.TryGetValue(mediaType, out type)) return true;
        var extension = Path.GetExtension(href).ToLowerInvariant();
        type = extension switch
        {
            ".jpg" or ".jpeg" => ("image/jpeg", ".jpg"),
            ".png" => ("image/png", ".png"),
            ".webp" => ("image/webp", ".webp"),
            ".gif" => ("image/gif", ".gif"),
            ".svg" => ("image/svg+xml", ".svg"),
            _ => default
        };
        return type != default;
    }

    private static bool IsSupportedImage(string mediaType, string href) => TryGetImageType(mediaType, href, out _);
    private static bool HasProperty(string properties, string property) =>
        properties.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Contains(property, StringComparer.Ordinal);
    private static string DirectoryOf(string path) => path[..Math.Max(0, path.LastIndexOf('/'))];
    private static string Join(string directory, string href)
    {
        var hash = href.IndexOf('#');
        var path = Uri.UnescapeDataString(hash >= 0 ? href[..hash] : href);
        return Normalize(string.Join('/', new[] { directory, path }.Where(value => value.Length > 0)));
    }
    private static string Normalize(string path) => string.Join('/', path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries)
        .Aggregate(new List<string>(), (parts, segment) =>
        {
            if (segment == ".") return parts;
            if (segment == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); return parts; }
            parts.Add(segment);
            return parts;
        }));
    private static ZipArchiveEntry? FindEntry(ZipArchive archive, string path) =>
        archive.GetEntry(path) ?? archive.Entries.FirstOrDefault(e => string.Equals(e.FullName, path, StringComparison.OrdinalIgnoreCase));
    private static XDocument ReadXml(ZipArchive archive, string path)
    {
        var entry = FindEntry(archive, path) ?? throw new InvalidDataException($"EPUB 中找不到文件：{path}");
        try
        {
            using var stream = entry.Open();
            return XDocument.Load(stream, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException)
        {
            using var stream = entry.Open();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            var bytes = buffer.ToArray();
            foreach (var encoding in new[] { Encoding.UTF8, Encoding.Unicode, Encoding.BigEndianUnicode })
            foreach (var quote in new[] { '"', '\'' })
            {
                var source = encoding.GetBytes($"version={quote}1.1{quote}");
                var replacement = encoding.GetBytes($"version={quote}1.0{quote}");
                for (var i = 0; i <= Math.Min(1024, bytes.Length - source.Length); i++)
                    if (bytes.AsSpan(i, source.Length).SequenceEqual(source))
                    {
                        replacement.CopyTo(bytes, i);
                        using var retry = new MemoryStream(bytes, false);
                        return XDocument.Load(retry, LoadOptions.PreserveWhitespace);
                    }
            }
            throw;
        }
    }

    private sealed record ManifestItem(string Id, string Href, string MediaType, string Properties);
}
