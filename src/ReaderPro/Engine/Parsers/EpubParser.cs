using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace ReaderPro.Engine.Parsers;

/// <summary>
/// EPUB/EPUB3 解析器：container.xml → OPF（标题/作者/封面/spine 顺序）→ 逐章 XHTML 文本提取。
/// 不依赖第三方库（PRD 自包含可审计）。
/// </summary>
public sealed class EpubParser : IBookParser
{
    private static readonly XNamespace Xh = "http://www.w3.org/1999/xhtml";

    public BookDocument Parse(string filePath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(filePath);
            var opfPath = FindOpfPath(zip);
            var opf = LoadXml(zip, opfPath);
            var opfDir = Path.GetDirectoryName(opfPath)?.Replace('\\', '/') ?? "";

            var doc = new BookDocument { FilePath = filePath, Format = "epub" };
            ReadMetadata(opf, doc);
            ReadCover(zip, opf, doc, opfDir);
            ReadSpine(zip, opf, doc, opfDir);
            if (doc.Chapters.Count == 0)
                throw new ParserException("EPUB 没有可读章节。");
            return doc;
        }
        catch (ParserException)
        {
            throw;
        }
        catch (Exception e)
        {
            throw new ParserException($"EPUB 解析失败：{filePath}", e);
        }
    }

    private static string FindOpfPath(ZipArchive zip)
    {
        var container = zip.GetEntry("META-INF/container.xml")
            ?? throw new ParserException("缺少 META-INF/container.xml。");
        using var sr = new StreamReader(container.Open(), Encoding.UTF8);
        var xml = XDocument.Load(sr);
        var rootfile = xml.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "rootfile" && e.Attribute("media-type")?.Value == "application/oebps-package+xml")
            ?? throw new ParserException("container.xml 未声明 OPF。");
        var path = rootfile.Attribute("full-path")?.Value
            ?? throw new ParserException("OPF full-path 缺失。");
        return Uri.UnescapeDataString(path);
    }

    private static XDocument LoadXml(ZipArchive zip, string path)
    {
        var entry = zip.GetEntry(path)
            ?? throw new ParserException($"OPF 不存在：{path}");
        using var sr = new StreamReader(entry.Open(), Encoding.UTF8, true);
        return XDocument.Load(sr, LoadOptions.PreserveWhitespace);
    }

    private static void ReadMetadata(XDocument opf, BookDocument doc)
    {
        var md = opf.Descendants().FirstOrDefault(e => e.Name.LocalName == "metadata");
        if (md == null) return;
        doc.Title = GetText(md, "title") ?? doc.Title;
        doc.Author = GetText(md, "creator") ?? "";
    }

    private static string? GetText(XElement parent, string localName)
    {
        var el = parent.Elements().FirstOrDefault(e => e.Name.LocalName == localName);
        return el?.Value.Trim();
    }

    private static void ReadCover(ZipArchive zip, XDocument opf, BookDocument doc, string opfDir)
    {
        var manifest = opf.Descendants().FirstOrDefault(e => e.Name.LocalName == "manifest");
        if (manifest == null) return;
        var coverItem = manifest.Elements()
            .FirstOrDefault(e => (e.Attribute("properties")?.Value ?? "").Contains("cover-image")
                                 || e.Attribute("id")?.Value == "cover-image"
                                 || e.Attribute("id")?.Value == "cover");
        if (coverItem == null) return;
        var href = coverItem.Attribute("href")?.Value;
        if (string.IsNullOrEmpty(href)) return;
        var full = CombinePath(opfDir, href);
        var entry = zip.GetEntry(full);
        if (entry == null) return;
        try
        {
            var coversDir = Path.Combine(AppContext.BaseDirectory, "data", "covers");
            Directory.CreateDirectory(coversDir);
            var ext = Path.GetExtension(full);
            if (string.IsNullOrEmpty(ext)) ext = ".jpg";
            var outPath = Path.Combine(coversDir, Sanitize(doc.Title) + ext);
            entry.ExtractToFile(outPath, overwrite: true);
            doc.CoverPath = outPath;
        }
        catch
        {
            // 封面提取失败不影响正文
        }
    }

    private static void ReadSpine(ZipArchive zip, XDocument opf, BookDocument doc, string opfDir)
    {
        var manifest = opf.Descendants().FirstOrDefault(e => e.Name.LocalName == "manifest");
        var spine = opf.Descendants().FirstOrDefault(e => e.Name.LocalName == "spine");
        if (manifest == null || spine == null) return;

        var items = manifest.Elements()
            .Where(e => e.Name.LocalName == "item")
            .ToDictionary(e => e.Attribute("id")?.Value ?? "", e => e);

        foreach (var itemref in spine.Elements().Where(e => e.Name.LocalName == "itemref"))
        {
            var idref = itemref.Attribute("idref")?.Value;
            if (idref == null || !items.TryGetValue(idref, out var item)) continue;
            var href = item.Attribute("href")?.Value;
            if (string.IsNullOrEmpty(href)) continue;
            var full = CombinePath(opfDir, href);
            var entry = zip.GetEntry(full);
            if (entry == null) continue;
            var chapter = ReadChapter(zip, entry);
            if (chapter.Paragraphs.Count > 0)
                doc.Chapters.Add(chapter);
        }
    }

    private static string CombinePath(string dir, string href)
    {
        href = Uri.UnescapeDataString(href);
        if (href.StartsWith('/'))
            return href.TrimStart('/');
        return (dir.Length > 0 ? dir + "/" : "") + href;
    }

    private static BookChapter ReadChapter(ZipArchive zip, ZipArchiveEntry entry)
    {
        using var sr = new StreamReader(entry.Open(), Encoding.UTF8, true);
        var content = sr.ReadToEnd();

        string title = "";
        var paras = new List<string>();
        try
        {
            using var reader = XmlReader.Create(new StringReader(content),
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            var xml = XDocument.Load(reader, LoadOptions.PreserveWhitespace);

            title = xml.Descendants().FirstOrDefault(e =>
                e.Name.LocalName is "h1" or "h2" or "h3" or "title")?.Value.Trim() ?? "";
            foreach (var p in xml.Descendants().Where(e => e.Name.LocalName == "p"))
            {
                var text = string.Concat(p.DescendantNodes().OfType<XText>().Select(t => t.Value)).Trim();
                if (text.Length > 0) paras.Add(text);
            }
        }
        catch
        {
            // 宽松 HTML 回退：正则提取 <p>/<div> 文本
            foreach (Match m in Regex.Matches(content, @"<(?:p|div)[^>]*>(.*?)</(?:p|div)>",
                         RegexOptions.Singleline | RegexOptions.IgnoreCase))
            {
                var text = StripTags(m.Groups[1].Value).Trim();
                if (text.Length > 0) paras.Add(text);
            }
            var t = Regex.Match(content, @"<(?:h1|h2|title)[^>]*>(.*?)</(?:h1|h2|title)>",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (t.Success) title = StripTags(t.Groups[1].Value).Trim();
        }

        if (title.Length == 0 && paras.Count > 0)
            title = paras[0][..Math.Min(20, paras[0].Length)];
        var chapter = new BookChapter { Title = title };
        chapter.Paragraphs.AddRange(paras);
        return chapter;
    }

    private static string StripTags(string s) =>
        Regex.Replace(System.Net.WebUtility.HtmlDecode(s), "<[^>]+>", "");

    /// <summary>文件名安全化（去非法字符）。</summary>
    private static string Sanitize(string s)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
            sb.Append(invalid.Contains(c) ? '_' : c);
        return sb.ToString().Trim();
    }
}
