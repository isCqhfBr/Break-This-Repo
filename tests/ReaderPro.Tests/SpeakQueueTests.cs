using ReaderPro.Engine.Parsers;
using ReaderPro.ViewModels;
using Xunit;

namespace ReaderPro.Tests;

public class SpeakQueueTests
{
    private static BookChapter Ch(string title, params string[] paras)
    {
        var c = new BookChapter { Title = title };
        foreach (var p in paras) c.Paragraphs.Add(p);
        return c;
    }

    [Fact]
    public void Build_FromMiddle_ReadsToEndWithChapterBreaks()
    {
        var chapters = new List<BookChapter>
        {
            Ch("第一章", "a1", "a2", "a3"),
            Ch("第二章", "b1", "b2"),
            Ch("第三章", "c1"),
        };
        var q = MainViewModel.BuildSpeakQueue(chapters, 0, 1);

        Assert.Equal(7, q.Count);   // a2,a3 + 标题 + b1,b2 + 标题 + c1
        Assert.Equal((0, 1), (q[0].Chapter, q[0].Paragraph));
        Assert.Equal((0, 2), (q[1].Chapter, q[1].Paragraph));
        // 章首标题哨兵（para=-1）
        Assert.Equal(1, q[2].Chapter);
        Assert.Equal(-1, q[2].Paragraph);
        Assert.Equal("第二章", q[2].Text);
        Assert.Equal((1, 0), (q[3].Chapter, q[3].Paragraph));
        Assert.Equal(2, q[^1].Chapter);
        Assert.Equal(0, q[^1].Paragraph);
    }

    [Fact]
    public void Build_FromLaterChapter_OnlyThatChapterOnward()
    {
        var chapters = new List<BookChapter>
        {
            Ch("一", "a"),
            Ch("二", "b1", "b2"),
            Ch("三", "c"),
        };
        var q = MainViewModel.BuildSpeakQueue(chapters, 1, 1);
        Assert.Equal(3, q.Count);   // b2 + 标题 + c
        Assert.Equal("b2", q[0].Text);
    }

    [Fact]
    public void Build_EmptyParagraphChapter_Skipped()
    {
        var chapters = new List<BookChapter>
        {
            Ch("一", "a"),
            Ch("空章"),   // 无段落，跳过
            Ch("二", "b"),
        };
        var q = MainViewModel.BuildSpeakQueue(chapters, 0, 0);
        Assert.Equal(3, q.Count);   // a + 标题 + b（标题插段指向有内容的下一章）
        Assert.Equal(2, q[1].Chapter);   // 空章不占位，标题指向「二」（索引 2）
    }

    [Fact]
    public void Build_OutOfRange_Empty()
    {
        var chapters = new List<BookChapter> { Ch("一", "a") };
        Assert.Empty(MainViewModel.BuildSpeakQueue(chapters, 5, 0));
        Assert.Empty(MainViewModel.BuildSpeakQueue(chapters, 0, 9));
    }
}
