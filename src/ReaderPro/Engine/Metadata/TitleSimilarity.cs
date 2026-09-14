using System.Text.RegularExpressions;

namespace ReaderPro.Engine.Metadata;

/// <summary>书源候选结果（归一化）。</summary>
public sealed record BookCandidate(string Title, string Author, string Intro, string CoverUrl, string Source, string DetailUrl = "");

/// <summary>书名相似度工具（归一化 + 编辑距离 + 包含加权）。</summary>
public static class TitleSimilarity
{
    /// <summary>0..1，越接近 1 越相似。</summary>
    public static double Score(string a, string b)
    {
        var x = Normalize(a);
        var y = Normalize(b);
        if (x.Length == 0 || y.Length == 0) return 0;
        if (x == y) return 1.0;
        if (x.Contains(y) || y.Contains(x))
            return 0.9 * (double)Math.Min(x.Length, y.Length) / Math.Max(x.Length, y.Length) + 0.1;
        var dist = Levenshtein(x, y);
        return 1.0 - (double)dist / Math.Max(x.Length, y.Length);
    }

    public static string Normalize(string s)
    {
        s = Regex.Replace(s ?? "", @"[\s\p{P}《》「」『』【】\u3000]+", "").ToLowerInvariant();
        // 去掉常见前后缀噪音
        s = Regex.Replace(s, @"^(txt|全文|小说|书)+", "");
        s = Regex.Replace(s, @"(txt|下载|阅读|全文|完结|完本)+$", "");
        return s;
    }

    public static int Levenshtein(string a, string b)
    {
        var dp = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) dp[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) dp[0, j] = j;
        for (int i = 1; i <= a.Length; i++)
            for (int j = 1; j <= b.Length; j++)
                dp[i, j] = Math.Min(
                    Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1),
                    dp[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        return dp[a.Length, b.Length];
    }
}
