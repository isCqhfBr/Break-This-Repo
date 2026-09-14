using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ReaderPro.Engine.Metadata;

/// <summary>
/// 元数据多源查询（智能整理第二步：联网比对）。
/// 源：豆瓣图书搜索（结构化 JSON：书名/作者/封面/出版信息）、必应搜索（兜底书名，过滤噪音）。
/// 各源独立容错：单源失败不影响其他；并发执行、8s 超时。
/// 注：知轩藏书 zxcs.me 与百度在本机 DNS/反爬不可达（2026-09 实测），未纳入；豆瓣命中时信息最全，必应仅兜底书名。
/// </summary>
public sealed class MetadataService
{
    private static readonly HttpClient Http = CreateHttp();

    private static HttpClient CreateHttp()
    {
        var h = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            UseCookies = true,
            AllowAutoRedirect = true,
        })
        {
            Timeout = TimeSpan.FromSeconds(8),
        };
        h.DefaultRequestVersion = new Version(2, 0);
        h.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
        h.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        h.DefaultRequestHeaders.AcceptLanguage.ParseAdd("zh-CN,zh;q=0.9,en;q=0.8");
        h.DefaultRequestHeaders.Referrer = new Uri("https://cn.bing.com/");
        return h;
    }

    public async Task<List<BookCandidate>> SearchAsync(string title, string author, string proxy = "", CancellationToken ct = default)
    {
        // 有代理时，国外源（Google Books / Open Library）走代理；必应/豆瓣直连
        var proxyHttp = BuildProxyHttp(proxy);
        var tasks = new List<Task<List<BookCandidate>>>
        {
            DoubanSearchAsync(title, author, ct),
            BingSearchAsync(title, ct),
        };
        if (proxyHttp != null)
        {
            tasks.Add(GoogleBooksAsync(title, author, proxyHttp, ct));
            tasks.Add(OpenLibraryAsync(title, author, proxyHttp, ct));
        }
        var results = await Task.WhenAll(tasks);
        var merged = results.SelectMany(r => r).Take(8).ToList();

        // 豆瓣结果里前 2 个：进详情页抓真正的"内容简介"（搜索结果的 abstract 是出版信息）
        var toEnrich = merged.Where(c => c.Source == "豆瓣" && c.DetailUrl.Length > 0).Take(2).ToList();
        var enrichTasks = toEnrich.Select(async c =>
        {
            try
            {
                var realIntro = await FetchDoubanIntroAsync(c.DetailUrl, ct);
                if (realIntro.Length > 20)
                {
                    var i = merged.IndexOf(c);
                    if (i >= 0) merged[i] = c with { Intro = realIntro };
                }
            }
            catch { /* 详情失败保留原 abstract */ }
        });
        await Task.WhenAll(enrichTasks);
        return merged;
    }

    /// <summary>抓豆瓣图书详情页的"内容简介"（id="link-report" 内的 .intro 块；v:description 已随页面改版移除）。</summary>
    private static async Task<string> FetchDoubanIntroAsync(string detailUrl, CancellationToken ct)
    {
        var html = await Http.GetStringAsync(detailUrl, ct);
        var m = Regex.Match(html, @"id=""link-report""[\s\S]*?<div class=""intro"">([\s\S]*?)</div>",
            RegexOptions.IgnoreCase);
        if (!m.Success) return "";
        var raw = m.Groups[1].Value;
        raw = Regex.Replace(raw, @"</p>\s*<p[^>]*>", "\n", RegexOptions.IgnoreCase);
        raw = Regex.Replace(raw, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        raw = Regex.Replace(raw, @"<[^>]+>", "");
        raw = System.Net.WebUtility.HtmlDecode(raw);
        raw = Regex.Replace(raw, @"[ \t\u00a0]+", " ");
        raw = Regex.Replace(raw, @"(\s*\n\s*){2,}", "\n").Trim();
        return raw;
    }

    // ---------- 源 1：豆瓣图书搜索（结构化元数据） ----------

    private static async Task<List<BookCandidate>> DoubanSearchAsync(string title, string author, CancellationToken ct)
    {
        try
        {
            var url = "https://search.douban.com/book/subject_search?cat=1001&search_text=" +
                      Uri.EscapeDataString(title);
            var html = await Http.GetStringAsync(url, ct);

            // window.__DATA__ = {...}; 块后还有 window.__USER__ 等脚本，不能用正则锚 </script>，用 IndexOf 截段
            var start = html.IndexOf("window.__DATA__", StringComparison.Ordinal);
            if (start < 0) return new();
            var endScript = html.IndexOf("</script>", start, StringComparison.Ordinal);
            if (endScript < 0) return new();
            var seg = html.Substring(start, endScript - start);
            var eq = seg.IndexOf('=');
            var semi = seg.LastIndexOf(';');
            if (eq < 0 || semi <= eq) return new();
            var json = seg[(eq + 1)..semi].Trim();

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("items", out var items) ||
                items.ValueKind != JsonValueKind.Array) return new();

            var list = new List<BookCandidate>();
            foreach (var item in items.EnumerateArray().Take(5))
            {
                var t = Get(item, "title");
                if (t.Length == 0) continue;
                t = Regex.Replace(t, @"^\[[^\]]+\]\s*", "").Trim();   // 去 [丛书] 等前缀
                var a = Get(item, "author");
                var cover = Get(item, "cover_url");
                var intro = Get(item, "abstract");
                var detail = Get(item, "url");
                if (author.Length > 0 && a.Length > 0 && !a.Contains(author, StringComparison.OrdinalIgnoreCase))
                    continue;   // 作者不符则跳过（降低误配）
                list.Add(new BookCandidate(t, a, intro, cover, "豆瓣", detail));
            }
            return list;
        }
        catch { return new(); }
    }

    private static string Get(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var v)) return "";
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString() ?? "",
            JsonValueKind.Array when v.GetArrayLength() > 0 =>
                v[0].TryGetProperty("name", out var n) ? n.GetString() ?? "" : v[0].ToString(),
            _ => "",
        };
    }

    // ---------- 源 2：必应搜索（兜底书名，过滤噪音） ----------

    private static readonly Regex[] BingNoise =
    {
        new(@"最新章节|小说全文|全文阅读|在线阅读|txt下载|小说下载|最新章节列表|无弹窗", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"\s*[-_–—|]\s*.*$", RegexOptions.Compiled),      // 站点后缀
        new(@"（.*?）|\(.*?\)", RegexOptions.Compiled),        // 括号内（站点/作者/简介尾）
        new(@"[。，,、\s]+(全集|全本|完结|小说|下载|全文)+$", RegexOptions.Compiled | RegexOptions.IgnoreCase),  // 尾部噪音
    };

    private static async Task<List<BookCandidate>> BingSearchAsync(string title, CancellationToken ct)
    {
        try
        {
            var url = "https://cn.bing.com/search?q=" + Uri.EscapeDataString(title + " 小说 简介");
            var html = await Http.GetStringAsync(url, ct);
            var list = new List<BookCandidate>();
            // 旧 h2 正则定位标题；在每个标题后 600 字符内找 <p> 描述片段当简介
            foreach (Match m in Regex.Matches(html, @"<h2[^>]*>\s*<a[^>]*>(.*?)</a>",
                         RegexOptions.Singleline | RegexOptions.IgnoreCase))
            {
                var t = System.Net.WebUtility.HtmlDecode(Regex.Replace(m.Groups[1].Value, "<[^>]+>", "")).Trim();
                var seg = html.Substring(m.Index, Math.Min(600, html.Length - m.Index));
                var pm = Regex.Match(seg, @"<p[^>]*>(.*?)</p>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                var d = pm.Success
                    ? Regex.Replace(pm.Groups[1].Value, "<[^>]+>", "")
                    : "";
                d = System.Net.WebUtility.HtmlDecode(Regex.Replace(d, @"\s+", " ")).Trim();
                foreach (var n in BingNoise) t = n.Replace(t, " ").Trim();
                t = Regex.Replace(t, @"\s+", " ").Trim();
                if (t.Length < 2 || list.Count >= 5) continue;
                if (TitleSimilarity.Score(t, title) < 0.45) continue;
                if (list.Any(c => c.Title == t)) continue;
                list.Add(new BookCandidate(t, "", d, "", "必应"));
            }
            return list;
        }
        catch { return new(); }
    }

    // ---------- 代理 ----------
    private static HttpClient? BuildProxyHttp(string proxy)
    {
        if (string.IsNullOrWhiteSpace(proxy)) return null;
        try
        {
            var handler = new HttpClientHandler
            {
                Proxy = new System.Net.WebProxy(proxy),
                UseProxy = true,
                AutomaticDecompression = System.Net.DecompressionMethods.All,
            };
            return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(12) };
        }
        catch { return null; }
    }

    // ---------- 源 3：Google Books（走代理，有完整 description/作者/封面） ----------
    private static async Task<List<BookCandidate>> GoogleBooksAsync(string title, string author, HttpClient ph, CancellationToken ct)
    {
        try
        {
            var url = "https://www.googleapis.com/books/v1/volumes?q=" +
                      Uri.EscapeDataString("intitle:" + title) + "&maxResults=3";
            using var doc = JsonDocument.Parse(await ph.GetByteArrayAsync(url, ct));
            var list = new List<BookCandidate>();
            if (!doc.RootElement.TryGetProperty("items", out var items)) return list;
            foreach (var it in items.EnumerateArray())
            {
                if (!it.TryGetProperty("volumeInfo", out var v)) continue;
                var t = v.TryGetProperty("title", out var tt) ? tt.GetString() ?? "" : "";
                if (t.Length < 2 || TitleSimilarity.Score(t, title) < 0.45) continue;
                var au = v.TryGetProperty("authors", out var a) && a.ValueKind == JsonValueKind.Array && a.GetArrayLength() > 0
                    ? a[0].GetString() ?? "" : "";
                var desc = v.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
                var cover = v.TryGetProperty("imageLinks", out var il) && il.TryGetProperty("thumbnail", out var th)
                    ? th.GetString() ?? "" : "";
                if (list.Any(c => c.Title == t)) continue;
                list.Add(new BookCandidate(t, au, desc, cover, "Google"));
            }
            return list;
        }
        catch { return new(); }
    }

    // ---------- 源 4：Open Library（走代理，免费无认证） ----------
    private static async Task<List<BookCandidate>> OpenLibraryAsync(string title, string author, HttpClient ph, CancellationToken ct)
    {
        try
        {
            var url = "https://openlibrary.org/search.json?title=" +
                      Uri.EscapeDataString(title) + "&limit=3";
            using var doc = JsonDocument.Parse(await ph.GetByteArrayAsync(url, ct));
            var list = new List<BookCandidate>();
            if (!doc.RootElement.TryGetProperty("docs", out var docs)) return list;
            foreach (var it in docs.EnumerateArray())
            {
                var t = it.TryGetProperty("title", out var tt) ? tt.GetString() ?? "" : "";
                if (t.Length < 2 || TitleSimilarity.Score(t, title) < 0.45) continue;
                var au = it.TryGetProperty("author_name", out var a) && a.ValueKind == JsonValueKind.Array && a.GetArrayLength() > 0
                    ? a[0].GetString() ?? "" : "";
                // Open Library 无 description，用 first_sentence 或 subject 拼简介
                var desc = it.TryGetProperty("first_sentence", out var fs) ? fs.GetString() ?? "" : "";
                if (desc.Length < 20 && it.TryGetProperty("subject", out var sub) && sub.ValueKind == JsonValueKind.Array && sub.GetArrayLength() > 0)
                    desc = string.Join(" / ", sub.EnumerateArray().Take(5).Select(x => x.GetString() ?? ""));
                if (list.Any(c => c.Title == t)) continue;
                list.Add(new BookCandidate(t, au, desc, "", "OpenLib"));
            }
            return list;
        }
        catch { return new(); }
    }
}
