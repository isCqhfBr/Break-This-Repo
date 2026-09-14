using System.Text.RegularExpressions;

namespace ReaderPro.Engine.Metadata;

/// <summary>文件名清洗结果与置信度判定。</summary>
public sealed record TitleAnalysis(string CleanTitle, bool IsSuspect, List<string> Reasons);

/// <summary>
/// 智能整理第一步：从文件名提取候选书名并做置信度判定（本地规则，无网络）。
/// 疑似有误 → 进入「待确认」列表，由联网比对给出建议。
/// </summary>
public static class TitleAnalyzer
{
    private static readonly string[] NoiseFragments =
    {
        "txt下载", "txt 下载", "全文阅读", "最新章节", "无弹窗", "在线阅读", "免费阅读",
        "小说下载", "小说在线阅读", "更新至", "笔趣阁", "顶点小说", "手机阅读", "最新",
        "完本", "全集", "全文", "下载", "阅读",
    };

    private static readonly Regex Brackets = new(@"【[^】]*】|\[[^\]]*\]|《[^》]*》", RegexOptions.Compiled);
    private static readonly Regex UrlLike = new(@"https?://|www\.", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex DomainLike = new(@"\b[a-z0-9]([a-z0-9-]*[a-z0-9])?(\.[a-z0-9]([a-z0-9-]*[a-z0-9])?){1,}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex AuthorTag = new(@"作者[:：]\s*[^\s，。；、,;]+|作\s*者|by\s+[^\s]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ChapterResidue = new(@"第[0-9一二三四五六七八九十百千]+[章节卷回]", RegexOptions.Compiled);
    private static readonly Regex NumberNoise = new(@"^\d{3,}|-\d{2,}$|_\d+$", RegexOptions.Compiled);
    private static readonly Regex Garbled = new(@"[\uFFFD\uFFFE\uFFFF]", RegexOptions.Compiled);

    /// <summary>清洗文件名 → 候选书名 + 疑似标记（理由列表）。</summary>
    public static TitleAnalysis Analyze(string fileName)
    {
        var reasons = new List<string>();
        var t = Path.GetFileNameWithoutExtension(fileName ?? "").Trim();

        // 去括号内的网站/作者标签：【笔趣阁】xxx → xxx
        t = Brackets.Replace(t, " ").Trim();
        t = AuthorTag.Replace(t, " ").Trim();
        // 去首尾噪音词（保留中间，防误伤真书名）
        foreach (var n in NoiseFragments)
            t = Regex.Replace(t, n, " ", RegexOptions.IgnoreCase).Trim();
        t = UrlLike.Replace(t, " ").Trim();
        t = DomainLike.Replace(t, " ").Trim();
        // 折叠空白
        t = Regex.Replace(t, @"\s+", " ").Trim();
        t = t.Trim(' ', '　', '-', '_', '.', '、');

        if (t.Length == 0)
            reasons.Add("无法提取书名");
        if (t.Length > 24)
            reasons.Add($"书名过长（{t.Length} 字），疑似夹杂多余信息");
        if (UrlLike.IsMatch(t))
            reasons.Add("含网址特征");
        if (ChapterResidue.IsMatch(t))
            reasons.Add("残留章节标记");
        if (NumberNoise.IsMatch(t))
            reasons.Add("疑似以数字/序号结尾");
        if (Garbled.IsMatch(t))
            reasons.Add("含乱码字符");
        if (t.Contains('　') || Regex.IsMatch(t, @"[a-zA-Z]{6,}"))
            reasons.Add("含异常格式");

        return new TitleAnalysis(t, reasons.Count > 0, reasons);
    }
}
