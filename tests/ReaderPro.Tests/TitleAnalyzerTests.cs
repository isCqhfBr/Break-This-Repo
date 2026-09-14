using ReaderPro.Engine.Metadata;
using Xunit;

namespace ReaderPro.Tests;

public class TitleAnalyzerTests
{
    [Theory]
    [InlineData("斗罗大陆.txt", "斗罗大陆", false)]
    [InlineData("【笔趣阁】斗罗大陆.txt", "斗罗大陆", false)]
    [InlineData("斗罗大陆 全文阅读.txt", "斗罗大陆", false)]
    [InlineData("斗罗大陆 作者:唐家三少.txt", "斗罗大陆", false)]
    [InlineData("www.yeudusk.com 斗罗大陆.txt", "斗罗大陆", false)]
    public void CleanTitles_AreNotSuspect(string file, string expected, bool suspect)
    {
        var a = TitleAnalyzer.Analyze(file);
        Assert.Equal(expected, a.CleanTitle);
        Assert.Equal(suspect, a.IsSuspect);
    }

    [Fact]
    public void GarbledFileName_IsSuspect()
    {
        // 吸取旧项目《末日乐园》yeudusk 乱码/水印教训：此类文件名必须进待确认
        var a = TitleAnalyzer.Analyze("末日乐园�ﾊﾅyeudusk�ﾓﾞ�ﾊ.txt");
        Assert.True(a.IsSuspect);
        Assert.Contains(a.Reasons, r => r.Contains("乱码") || r.Contains("网址"));
    }

    [Fact]
    public void VeryLongName_IsSuspect()
    {
        var a = TitleAnalyzer.Analyze("这个书名特别特别长显然是下载站拼接了很多多余信息进去导致超过正常长度限制.txt");
        Assert.True(a.IsSuspect);
        Assert.Contains(a.Reasons, r => r.Contains("过长"));
    }

    [Fact]
    public void ChapterResidue_IsSuspect()
    {
        var a = TitleAnalyzer.Analyze("斗罗大陆 第1234章.txt");
        Assert.True(a.IsSuspect);
        Assert.Contains(a.Reasons, r => r.Contains("章节"));
    }
}

public class TitleSimilarityTests
{
    [Theory]
    [InlineData("斗罗大陆", "斗罗大陆", true)]
    [InlineData("斗罗大陆", "斗罗大陆III龙王传说", true)]   // 包含加权（≈0.5）
    [InlineData("斗罗大陆", "斗破苍穹", false)]
    public void Similarity_Scores(string a, string b, bool close)
    {
        var s = TitleSimilarity.Score(a, b);
        if (close) Assert.True(s >= 0.40, $"实际 {s}");
        else Assert.True(s < 0.3, $"实际 {s}");
    }

    [Fact]
    public void Normalize_StripsNoise()
    {
        Assert.Equal("斗罗大陆", TitleSimilarity.Normalize("《斗罗大陆》 txt下载"));
    }
}
