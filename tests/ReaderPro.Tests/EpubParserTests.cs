using System.IO.Compression;
using System.Text;
using ReaderPro.Engine.Parsers;
using Xunit;

namespace ReaderPro.Tests;

public class EpubParserTests
{
    /// <summary>构造一个最小 EPUB（zip：container.xml + OPF + 两章 XHTML）。</summary>
    private static string CreateEpub(string title, string author)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "rptest_" + Guid.NewGuid().ToString("N") + ".epub");
        using (var fs = File.Create(tmp))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            WriteEntry(zip, "mimetype", "application/epub+zip");
            WriteEntry(zip, "META-INF/container.xml",
                "<?xml version=\"1.0\"?>\n" +
                "<container version=\"1.0\" xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\">\n" +
                "  <rootfiles><rootfile full-path=\"OEBPS/content.opf\" media-type=\"application/oebps-package+xml\"/></rootfiles>\n" +
                "</container>");
            WriteEntry(zip, "OEBPS/content.opf",
                "<?xml version=\"1.0\"?>\n" +
                "<package xmlns=\"http://www.idpf.org/2007/opf\" version=\"3.0\" unique-identifier=\"id\">\n" +
                "  <metadata xmlns:dc=\"http://purl.org/dc/elements/1.1/\">\n" +
                $"    <dc:title>{title}</dc:title>\n" +
                $"    <dc:creator>{author}</dc:creator>\n" +
                "  </metadata>\n" +
                "  <manifest>\n" +
                "    <item id=\"c1\" href=\"c1.xhtml\" media-type=\"application/xhtml+xml\"/>\n" +
                "    <item id=\"c2\" href=\"c2.xhtml\" media-type=\"application/xhtml+xml\"/>\n" +
                "  </manifest>\n" +
                "  <spine>\n" +
                "    <itemref idref=\"c1\"/>\n" +
                "    <itemref idref=\"c2\"/>\n" +
                "  </spine>\n" +
                "</package>");
            WriteEntry(zip, "OEBPS/c1.xhtml",
                "<?xml version=\"1.0\"?>\n" +
                "<html xmlns=\"http://www.w3.org/1999/xhtml\">\n" +
                "<head><title>第一章 雾中来客</title></head>\n" +
                "<body>\n" +
                "<h1>第一章 雾中来客</h1>\n" +
                "<p>清晨的雾气还没有散尽。</p>\n" +
                "<p>他沿着<strong>湿滑</strong>的石板路往前走。</p>\n" +
                "</body></html>");
            WriteEntry(zip, "OEBPS/c2.xhtml",
                "<?xml version=\"1.0\"?>\n" +
                "<html xmlns=\"http://www.w3.org/1999/xhtml\">\n" +
                "<head><title>第二章 灯下</title></head>\n" +
                "<body>\n" +
                "<h1>第二章 灯下</h1>\n" +
                "<p>灯在夜里亮了一整晚。</p>\n" +
                "</body></html>");
        }
        return tmp;
    }

    private static void WriteEntry(ZipArchive zip, string name, string content)
    {
        var e = zip.CreateEntry(name);
        using var sw = new StreamWriter(e.Open(), new UTF8Encoding(false));
        sw.Write(content);
    }

    [Fact]
    public void MinimalEpub_ParsesTitleChaptersAndParagraphs()
    {
        var path = CreateEpub("雾中来客", "佚名");
        try
        {
            var doc = new EpubParser().Parse(path);
            Assert.Equal("雾中来客", doc.Title);
            Assert.Equal("佚名", doc.Author);
            Assert.Equal(2, doc.Chapters.Count);
            Assert.Equal("第一章 雾中来客", doc.Chapters[0].Title);
            Assert.Equal(2, doc.Chapters[0].Paragraphs.Count);
            Assert.Contains("湿滑", doc.Chapters[0].Paragraphs[1]);
            Assert.Equal("第二章 灯下", doc.Chapters[1].Title);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MissingContainer_Throws()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "rptest_" + Guid.NewGuid().ToString("N") + ".epub");
        using (var fs = File.Create(tmp))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            WriteEntry(zip, "x.txt", "not an epub");
        }
        try
        {
            Assert.Throws<ParserException>(() => new EpubParser().Parse(tmp));
        }
        finally
        {
            File.Delete(tmp);
        }
    }
}
