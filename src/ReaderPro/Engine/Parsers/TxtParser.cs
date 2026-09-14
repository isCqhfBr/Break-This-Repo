using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace ReaderPro.Engine.Parsers;

/// <summary>
/// TXT 解析器：编码自动识别 → 行级清洗（广告/水印/网址行，吸取旧项目《末日乐园》水印教训）
/// → 智能分段（空行分段 / 标点合并两种排版启发式）→ 章节识别分章。
/// </summary>
public sealed class TxtParser : IBookParser
{
    // 章节标题行（中文网文主流 + 英文）
    private static readonly Regex ChapterLine = new(
        @"^\s*(第[0-9零一二三四五六七八九十百千万两]+[章节卷回集部篇幕][^。！？；：]{0,60}|序章|楔子|引子|前言|序言|番外|尾声|后记|终章|外传|卷\s*[一二三四五六七八九十0-9]+|Chapter\s*\d+[^\n]{0,60}|Prologue|Epilogue)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // 整行水印/广告/网址（整行命中才丢弃，段落内 URL 保留）
    private static readonly Regex[] DropWholeLine =
    {
        new(@"^[a-zA-Z0-9][a-zA-Z0-9\-.]*\.(com|net|org|cc|cn|xyz|top|vip|me)\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"^www\.[^\s]{2,}\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"^http[s]?://[^\s]+\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"^(本书|本作|小说|作品).{0,20}(由|感谢|首发|上传整理|分享)", RegexOptions.Compiled),
        new(@"^(欢迎|感谢).{0,20}(书友|读者|阅读|收藏|投推荐票)", RegexOptions.Compiled),
        new(@"^(最新|最快|最火|热销).{0,15}(更新|连载|排行)", RegexOptions.Compiled),
        new(@"^【本章由.{0,30}$", RegexOptions.Compiled),
        new(@"^[=*#\-_]{8,}\s*$", RegexOptions.Compiled),
    };

    private static readonly HashSet<char> ClosingPunct = new() { '。', '！', '？', '；', '：', '…', '”', '』', '）', '】', '"', ')', '!', '?' };

    public BookDocument Parse(string filePath)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(filePath);
        }
        catch (Exception e)
        {
            throw new ParserException($"读取文件失败：{filePath}", e);
        }
        if (bytes.Length == 0)
            throw new ParserException("文件为空。");

        var text = EncodingDetector.Decode(bytes);
        var lines = SplitAndCleanLines(text);

        // 启发式：空行分段模式（空行占比 ≥ 6% 视为空行分段排版，否则为逐行排版）
        int blank = lines.Count(l => l.Length == 0);
        bool blankMode = lines.Count > 20 && blank * 100 / lines.Count >= 6;

        var paragraphs = blankMode ? GroupByBlankLines(lines) : MergeByPunctuation(lines);

        var doc = new BookDocument
        {
            FilePath = filePath,
            Format = "txt",
            Title = System.IO.Path.GetFileNameWithoutExtension(filePath),
        };
        SplitChapters(doc, paragraphs);
        return doc;
    }

    /// <summary>拆行 + 行级清洗（丢弃水印/广告/网址整行、去控制符、去行首缩进空格）。</summary>
    internal static List<string> SplitAndCleanLines(string text)
    {
        var result = new List<string>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim('\r', '\uFEFF', '\u200B');
            line = line.Trim();
            if (line.Length == 0)
            {
                result.Add("");   // 保留空行用于分段统计
                continue;
            }
            // 去行首全角/半角缩进
            var s = line.TrimStart('　', ' ', '\t');
            if (DropWholeLine.Any(r => r.IsMatch(s)))
                continue;        // 整行广告/水印丢弃
            result.Add(s);
        }
        return result;
    }

    internal static List<string> GroupByBlankLines(List<string> lines)
    {
        var paras = new List<string>();
        var cur = new StringBuilder();
        foreach (var line in lines)
        {
            if (line.Length == 0)
            {
                if (cur.Length > 0) { paras.Add(cur.ToString()); cur.Clear(); }
                continue;
            }
            if (cur.Length > 0) cur.Append('\n');
            cur.Append(line);
        }
        if (cur.Length > 0) paras.Add(cur.ToString());
        return paras;
    }

    /// <summary>逐行排版：按行尾闭合标点合并段落（修复断句/乱换行）；
    /// 章节标题行独立成段（供 SplitChapters 识别）。</summary>
    internal static List<string> MergeByPunctuation(List<string> lines)
    {
        var paras = new List<string>();
        var cur = new StringBuilder();
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.Length == 0)
            {
                if (cur.Length > 0) { paras.Add(cur.ToString()); cur.Clear(); }
                continue;
            }
            if (ChapterLine.IsMatch(line))
            {
                // 章节标题独立成段，不并入正文
                if (cur.Length > 0) { paras.Add(cur.ToString()); cur.Clear(); }
                paras.Add(line);
                continue;
            }
            bool nextIsChapter = i + 1 < lines.Count && lines[i + 1].Length > 0 && ChapterLine.IsMatch(lines[i + 1]);
            if (cur.Length > 0 && (nextIsChapter || IsClosing(line)))
            {
                cur.Append(line);
                paras.Add(cur.ToString());
                cur.Clear();
            }
            else if (cur.Length > 0)
            {
                cur.Append(line);   // 行尾未闭合 → 续行
            }
            else
            {
                cur.Append(line);
            }
        }
        if (cur.Length > 0) paras.Add(cur.ToString());
        return paras;
    }

    private static bool IsClosing(string line)
    {
        var ch = line[^1];
        return ClosingPunct.Contains(ch);
    }

    /// <summary>按章节标题分章；无标题时整本单章。</summary>
    internal static void SplitChapters(BookDocument doc, List<string> paragraphs)
    {
        BookChapter? current = null;
        foreach (var p in paragraphs)
        {
            var firstLine = p.Split('\n')[0].Trim();
            if (ChapterLine.IsMatch(firstLine))
            {
                if (current != null) doc.Chapters.Add(current);
                current = new BookChapter { Title = firstLine.Trim().Trim('　', ' ') };
                var rest = p.Contains('\n') ? p[(firstLine.Length)..].Trim('\n') : "";
                if (rest.Length > 0) current.Paragraphs.Add(rest);
                continue;
            }
            current ??= new BookChapter { Title = "正文" };
            current.Paragraphs.Add(p);
        }
        if (current != null) doc.Chapters.Add(current);
        if (doc.Chapters.Count == 0)
            doc.Chapters.Add(new BookChapter { Title = "正文" });
    }
}
