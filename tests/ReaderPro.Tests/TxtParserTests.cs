using System.Text;
using ReaderPro.Engine.Parsers;
using Xunit;

namespace ReaderPro.Tests;

public class TxtParserTests
{
    private static BookDocument ParseText(string text, Encoding? enc = null)
    {
        enc ??= new UTF8Encoding(false);
        var tmp = Path.Combine(Path.GetTempPath(), "rptest_" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllBytes(tmp, enc.GetBytes(text));
        try
        {
            return new TxtParser().Parse(tmp);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void Utf8_NoBom_ChineseParsed()
    {
        var doc = ParseText("第一章 起点\n\n他推开门，看见了一片海。\n\n第二章 归途\n\n风很大。");
        Assert.Equal("第一章 起点", doc.Chapters[0].Title);
        Assert.Equal(2, doc.Chapters.Count);
        Assert.Contains("他推开门，看见了一片海。", doc.Chapters[0].Paragraphs[0]);
    }

    [Fact]
    public void Gbk_Encoding_Detected()
    {
        var doc = ParseText("第一章 测试\n\n中文内容编码识别正常。", Encoding.GetEncoding("GB18030"));
        Assert.Contains("中文内容编码识别正常。", doc.Chapters[0].Paragraphs[0]);
    }

    [Fact]
    public void Utf16Le_WithBom_Detected()
    {
        var doc = ParseText("第一章 测试\n\nUTF16 内容。", Encoding.Unicode);
        Assert.Contains("UTF16 内容。", doc.Chapters[0].Paragraphs[0]);
    }

    [Fact]
    public void WatermarkLines_AreDropped()
    {
        // 吸取旧项目《末日乐园》yeudusk 水印教训：整行水印/广告必须丢弃
        var doc = ParseText(
            "第一章 起点\n\n正文第一段。\nwww.yeudusk.com\nyeuduskwww.yeudusk.com\n" +
            "欢迎广大书友光临阅读，最新、最快、最火的连载作品尽在起点原创！\n\n正文第二段。");
        var joined = string.Join("\n", doc.Chapters[0].Paragraphs);
        Assert.DoesNotContain("yeudusk", joined);
        Assert.DoesNotContain("书友光临", joined);
        Assert.Contains("正文第一段。", joined);
        Assert.Contains("正文第二段。", joined);
    }

    [Fact]
    public void ChapterPatterns_Recognized()
    {
        var doc = ParseText(
            "序章\n\n序言内容。\n\n第一百二十三章 风云\n\n正文内容。\n\nChapter 5\n\n英文章节。");
        var titles = doc.Chapters.Select(c => c.Title).ToArray();
        Assert.Contains("序章", titles);
        Assert.Contains("第一百二十三章 风云", titles);
        Assert.Contains("Chapter 5", titles);
    }

    [Fact]
    public void NoChapters_BecomesSingleBody()
    {
        var doc = ParseText("第一行。\n第二行。\n第三行。");
        Assert.Single(doc.Chapters);
        Assert.Equal("正文", doc.Chapters[0].Title);
        Assert.True(doc.Chapters[0].Paragraphs.Count >= 1);
    }

    [Fact]
    public void BlankLineMode_KeepsParagraphs()
    {
        var doc = ParseText("第一章 测试\n\n第一段。\n\n第二段。\n\n第三段。");
        Assert.Equal(3, doc.Chapters[0].Paragraphs.Count);
    }

    [Fact]
    public void LineByLineMode_MergesByPunctuation()
    {
        // 无空行排版：行尾未闭合（逗号）的续行合并进上一段；句号行断段
        var doc = ParseText(
            "第一章 测试\n" +
            "他看见那扇门缓缓打开，\n" +
            "里面走出一个白发老人。\n" +
            "他愣了愣，\n" +
            "随即笑了。");
        Assert.Single(doc.Chapters);                    // 标题独立成段 → 分章成功
        Assert.Equal(2, doc.Chapters[0].Paragraphs.Count);
        Assert.Contains("白发老人", doc.Chapters[0].Paragraphs[0]);
        Assert.Contains("随即笑了", doc.Chapters[0].Paragraphs[1]);
    }

    [Fact]
    public void LineByLine_ChapterTitleNotMergedIntoBody()
    {
        // 标题行独立成段：分章必须成功
        var doc = ParseText("第一章 起点\n第一段开始，\n继续到这里。");
        Assert.Equal("第一章 起点", doc.Chapters[0].Title);
        Assert.Contains("第一段开始，继续到这里。", doc.Chapters[0].Paragraphs[0]);
    }
}
