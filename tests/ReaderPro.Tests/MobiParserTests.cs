using System.Text;
using ReaderPro.Engine.Parsers;
using Xunit;

namespace ReaderPro.Tests;

public class MobiParserTests
{
    /// <summary>构造一个未压缩(0xCCCC) MOBI：PalmDB 头 + 记录0(MOBI头+EXTH) + 记录1(HTML正文)。</summary>
    private static string CreateUncompressedMobi(string title, string author)
    {
        var body = Encoding.UTF8.GetBytes(
            "<html><head><title>t</title></head><body>" +
            "<h1>第一章 起点</h1><p>第一段内容。</p><p>第二段。</p>" +
            "<mbp:pagebreak/>" +
            "<h1>第二章 归途</h1><p>第三段。</p>" +
            "</body></html>");

        // 记录 0 = PalmDOC 头(16) + BOOKMOBI 头(232) + EXTH
        var rec0 = new List<byte>(1024);
        rec0.AddRange(new byte[16]);                       // PalmDOC 头占位
        rec0[16 - 16 + 0x0C] = 0xCC;                       // compression = 0xCCCC (BE)
        rec0[16 - 16 + 0x0D] = 0xCC;
        rec0.AddRange(Encoding.ASCII.GetBytes("BOOKMOBI"));
        rec0.AddRange(BE32(232));                          // headerLength
        rec0.AddRange(BE32(2));                            // mobiType
        rec0.AddRange(BE32(65001));                        // textEncoding UTF-8
        rec0.AddRange(BE32(0));                            // uid
        rec0.AddRange(BE32(6));                            // version
        rec0.AddRange(new byte[232 - 28]);                 // 头剩余填充（8+5*4=28 已写）

        var exthBody = new List<byte>();
        exthBody.AddRange(BE32(0x03));                     // Title
        var tBytes = Encoding.UTF8.GetBytes(title);
        exthBody.AddRange(BE32(8 + tBytes.Length));
        exthBody.AddRange(tBytes);
        exthBody.AddRange(BE32(0x04));                     // Author
        var aBytes = Encoding.UTF8.GetBytes(author);
        exthBody.AddRange(BE32(8 + aBytes.Length));
        exthBody.AddRange(aBytes);
        rec0.AddRange(Encoding.ASCII.GetBytes("EXTH"));
        rec0.AddRange(BE32(12 + exthBody.Count));
        rec0.AddRange(BE32(2));
        rec0.AddRange(exthBody);

        var rec0Bytes = rec0.ToArray();

        // PalmDB：78 字节头 + 记录列表（记录0、记录1）+ 记录数据
        var db = new List<byte>(rec0Bytes.Length + body.Length + 0x100);
        db.AddRange(new byte[0x4C]);                       // name 32 + 属性等
        db.AddRange(BE16(2));                              // numRecords @0x4C
        int recListOff = 0x4E;
        int rec0Off = recListOff + 2 * 8;
        int rec1Off = rec0Off + rec0Bytes.Length;
        db.AddRange(BE32(rec0Off));                        // 记录0 offset
        db.AddRange(new byte[4]);
        db.AddRange(BE32(rec1Off));                        // 记录1 offset
        db.AddRange(new byte[4]);
        db.AddRange(rec0Bytes);
        db.AddRange(body);

        var tmp = Path.Combine(Path.GetTempPath(), "rptest_" + Guid.NewGuid().ToString("N") + ".mobi");
        File.WriteAllBytes(tmp, db.ToArray());
        return tmp;
    }

    [Fact]
    public void UncompressedMobi_ParsesTitleAuthorChapters()
    {
        var path = CreateUncompressedMobi("测试书", "佚名");
        try
        {
            var doc = new MobiParser().Parse(path);
            Assert.Equal("测试书", doc.Title);
            Assert.Equal("佚名", doc.Author);
            Assert.Equal(2, doc.Chapters.Count);
            Assert.Equal("第一章 起点", doc.Chapters[0].Title);
            Assert.Equal(2, doc.Chapters[0].Paragraphs.Count);
            Assert.Contains("第一段内容。", doc.Chapters[0].Paragraphs[0]);
            Assert.Equal("第二章 归途", doc.Chapters[1].Title);
            Assert.Contains("第三段。", doc.Chapters[1].Paragraphs[0]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void PalmDocDecompress_BackReference()
    {
        // flag=0x20：bit7 直写'A'、bit6 直写'B'、bit5 回引(len=4, dist=2) → "ABABAB"
        byte[] src = { 0x20, (byte)'A', (byte)'B', 0x04, 0x02 };
        var result = Encoding.ASCII.GetString(MobiParser.PalmDocDecompress(src));
        Assert.Equal("ABABAB", result);
    }

    [Fact]
    public void NotMobi_Throws()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "rptest_" + Guid.NewGuid().ToString("N") + ".mobi");
        File.WriteAllBytes(tmp, Encoding.UTF8.GetBytes("这不是 MOBI 文件"));
        try
        {
            Assert.Throws<ParserException>(() => new MobiParser().Parse(tmp));
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    private static byte[] BE16(ushort v) => new[] { (byte)(v >> 8), (byte)v };
    private static byte[] BE32(uint v) => new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v };
    private static byte[] BE32(int v) => BE32((uint)v);
}
