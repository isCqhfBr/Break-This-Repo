using System.Text;
using System.Text.RegularExpressions;

namespace ReaderPro.Engine.Parsers;

/// <summary>
/// MOBI/PRC 基础解析器（PRD P0 格式）。
/// 支持：PalmDB 记录表、PalmDOC 头、未压缩(0xCCCC) 与 PalmDOC LZ77 压缩(0x01)、
/// EXTH 元数据（书名/作者）、HTML 正文提取、&lt;mbp:pagebreak/&gt; 分章。
/// 暂不支持（M2 计划）：KF8/AZW3 的 HUFF/CDIC 压缩与复杂版式。
/// </summary>
public sealed class MobiParser : IBookParser
{
    private static readonly byte[] BookMobi = Encoding.ASCII.GetBytes("BOOKMOBI");
    private static readonly byte[] ExthMagic = Encoding.ASCII.GetBytes("EXTH");

    public BookDocument Parse(string filePath)
    {
        try
        {
            var bytes = File.ReadAllBytes(filePath);
            var doc = new BookDocument { FilePath = filePath, Format = "mobi" };

            int bo = FindAscii(bytes, BookMobi);
            if (bo < 0) throw new ParserException("不是有效的 MOBI 文件（缺少 BOOKMOBI 头）。");
            if (bo < 16) throw new ParserException("MOBI 文件头损坏。");

            ushort compression = BE16(bytes, bo - 16 + 0x0C);
            uint textEncoding = BE32(bytes, bo + 0x10);     // MOBI 头 textEncoding @ +0x10
            uint headerLength = BE32(bytes, bo + 8);        // MOBI 头 headerLength @ +0x08
            if (headerLength < 24 || bo + (int)headerLength > bytes.Length)
                throw new ParserException("MOBI 头长度异常。");

            var (title, author) = ReadExth(bytes, bo + (int)headerLength);
            doc.Title = title;
            doc.Author = author;

            // PalmDB 记录表：记录数 @0x4C(BE16)，记录列表 @0x4E，每项 8 字节（offset BE32 + 属性/uid 4）
            if (bytes.Length < 0x50) throw new ParserException("PalmDB 头不完整。");
            int numRecords = BE16(bytes, 0x4C);
            if (numRecords < 2) throw new ParserException("MOBI 没有正文记录。");

            using var text = new MemoryStream();
            for (int r = 1; r < numRecords; r++)
            {
                int off = (int)BE32(bytes, 0x4E + r * 8);
                int end = (r + 1 < numRecords) ? (int)BE32(bytes, 0x4E + (r + 1) * 8) : bytes.Length;
                if (off < 0 || off >= bytes.Length || end <= off) continue;
                var chunk = Decompress(bytes[off..end], compression);
                text.Write(chunk);
            }

            var raw = EncodingFor(textEncoding).GetString(text.ToArray());
            BuildChapters(doc, raw);
            if (doc.Chapters.Count == 0 || doc.Chapters.Sum(c => c.Paragraphs.Count) == 0)
                throw new ParserException("MOBI 正文为空或无法提取。");
            return doc;
        }
        catch (ParserException) { throw; }
        catch (Exception e)
        {
            throw new ParserException($"MOBI 解析失败：{filePath}", e);
        }
    }

    private static (string, string) ReadExth(byte[] bytes, int off)
    {
        if (off + 12 > bytes.Length || !AsciiAt(bytes, off, ExthMagic))
            return ("", "");
        int count = (int)BE32(bytes, off + 8);
        int p = off + 12;
        string title = "", author = "";
        for (int i = 0; i < count && p + 8 <= bytes.Length; i++)
        {
            int type = (int)BE32(bytes, p);
            int len = (int)BE32(bytes, p + 4);
            p += 8;
            if (len < 8 || p + len - 8 > bytes.Length) break;
            var data = Encoding.UTF8.GetString(bytes, p, len - 8).TrimEnd('\0');
            p += len - 8;
            switch (type)
            {
                case 0x03 when title.Length == 0: title = data; break;
                case 0x04 when author.Length == 0: author = data; break;
            }
        }
        return (title, author);
    }

    private static byte[] Decompress(byte[] src, ushort compression)
    {
        if (compression == 0xCCCC) return src;                       // 未压缩
        if (compression == 0x01) return PalmDocDecompress(src);      // LZ77 变体
        throw new ParserException($"不支持的 MOBI 压缩类型 0x{compression:X4}（KF8/HUFF 属 M2）。");
    }

    /// <summary>PalmDOC LZ77 解压：flag 字节，bit7=1 时回引（len=((c&gt;&gt;2)+3), dist=((c&amp;3)&lt;&lt;8)|d）。</summary>
    public static byte[] PalmDocDecompress(byte[] src)
    {
        using var dst = new MemoryStream(src.Length * 2);
        int i = 0;
        while (i < src.Length)
        {
            byte flags = src[i++];
            for (int bit = 0; bit < 8 && i < src.Length; bit++)
            {
                if ((flags & (0x80 >> bit)) != 0)
                {
                    if (i + 1 >= src.Length) break;
                    byte c = src[i], d = src[i + 1];
                    i += 2;
                    int length = (c >> 2) + 3;
                    int distance = ((c & 0x03) << 8) | d;
                    if (distance == 0 || distance > dst.Length) break;
                    var pos = (int)dst.Position;
                    for (int k = 0; k < length; k++)
                    {
                        var buf = dst.GetBuffer();
                        dst.WriteByte(buf[pos - distance + k]);
                    }
                }
                else
                {
                    dst.WriteByte(src[i++]);
                }
            }
        }
        return dst.ToArray();
    }

    private static Encoding EncodingFor(uint code) => code switch
    {
        1252 => Encoding.GetEncoding(1252),
        65001 or 0 => Encoding.UTF8,
        _ => Encoding.UTF8,
    };

    private static void BuildChapters(BookDocument doc, string raw)
    {
        // 按 <mbp:pagebreak 切章（网文 MOBI 每章一个分页符）；否则整本一章
        var parts = Regex.Split(raw, @"(?=<mbp:pagebreak)", RegexOptions.IgnoreCase);
        int idx = 0;
        foreach (var part in parts)
        {
            var h = Regex.Match(part, @"<h[1-3][^>]*>(.*?)</h[1-3]>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            var title = h.Success ? StripTags(h.Groups[1].Value).Trim() : $"第 {++idx} 章";
            var paras = ExtractParagraphs(part);
            if (paras.Count == 0) continue;
            var chapter = new BookChapter { Title = title };
            chapter.Paragraphs.AddRange(paras);
            doc.Chapters.Add(chapter);
        }
        if (doc.Chapters.Count == 0)
        {
            var paras = ExtractParagraphs(raw);
            if (paras.Count > 0)
            {
                var chapter = new BookChapter { Title = doc.Title.Length > 0 ? doc.Title : "正文" };
                chapter.Paragraphs.AddRange(paras);
                doc.Chapters.Add(chapter);
            }
        }
    }

    private static List<string> ExtractParagraphs(string html)
    {
        var paras = new List<string>();
        foreach (Match m in Regex.Matches(html, @"<(?:p|div)[^>]*>(.*?)</(?:p|div)>",
                     RegexOptions.Singleline | RegexOptions.IgnoreCase))
        {
            var text = StripTags(m.Groups[1].Value).Trim();
            if (text.Length > 0) paras.Add(text);
        }
        if (paras.Count == 0)
        {
            // 宽松回退：去全部标签后按空行分段
            var text = StripTags(html).Trim();
            foreach (var block in Regex.Split(text, @"\n\s*\n"))
            {
                var t = block.Replace("\n", "").Trim();
                if (t.Length > 0) paras.Add(t);
            }
        }
        return paras;
    }

    private static string StripTags(string s) =>
        System.Net.WebUtility.HtmlDecode(Regex.Replace(s, "<[^>]+>", ""));

    private static int FindAscii(byte[] data, byte[] needle)
    {
        for (int i = 0; i <= data.Length - needle.Length; i++)
        {
            bool ok = true;
            for (int j = 0; j < needle.Length; j++)
                if (data[i + j] != needle[j]) { ok = false; break; }
            if (ok) return i;
        }
        return -1;
    }

    private static bool AsciiAt(byte[] data, int off, byte[] needle)
    {
        if (off < 0 || off + needle.Length > data.Length) return false;
        for (int j = 0; j < needle.Length; j++)
            if (data[off + j] != needle[j]) return false;
        return true;
    }

    private static ushort BE16(byte[] b, int o) => (ushort)((b[o] << 8) | b[o + 1]);
    private static uint BE32(byte[] b, int o) =>
        ((uint)b[o] << 24) | ((uint)b[o + 1] << 16) | ((uint)b[o + 2] << 8) | b[o + 3];
}
