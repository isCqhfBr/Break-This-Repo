using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ReaderPro.Engine.Data;
using ReaderPro.Engine.Metadata;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using MessageBox = System.Windows.MessageBox;

namespace ReaderPro.ViewModels;

/// <summary>待确认条目：原书名 → 联网建议。</summary>
public sealed class OrganizeItem : ObservableObject
{
    public required BookRecord Book { get; init; }
    public required string OriginalTitle { get; init; }
    public required string SuspectReasons { get; init; }

    private BookCandidate? _candidate;
    public BookCandidate? Candidate
    {
        get => _candidate;
        set { if (Set(ref _candidate, value)) { OnPropertyChanged(nameof(Status)); OnPropertyChanged(nameof(StatusBrush)); } }
    }

    private bool _accepted;
    public bool Accepted { get => _accepted; set { Set(ref _accepted, value); OnPropertyChanged(nameof(Status)); OnPropertyChanged(nameof(StatusBrush)); } }

    public string SuggestedTitle => Candidate?.Title ?? "—";
    public string SuggestedAuthor => Candidate?.Author ?? "";
    public string Intro => Candidate?.Intro ?? "";
    public string Source => Candidate?.Source ?? "";
    public string Status => Accepted ? "✔ 已采纳" : Candidate == null ? "待比对" : "待确认";
    public Brush StatusBrush => Accepted
        ? new SolidColorBrush(Color.FromRgb(0x16, 0xA0, 0x62))
        : new SolidColorBrush(Color.FromRgb(0xD4, 0x77, 0x06));

    private ImageSource? _cover;
    public ImageSource? Cover { get => _cover; set => Set(ref _cover, value); }
}

/// <summary>
/// 元数据智能中心「智能整理」：书架疑似书名 → 联网比对 → 一次确认。
/// 流水线：本地置信度分析 → 多源查询 → 采纳落库（书名/作者/简介/封面）。
/// </summary>
public sealed class OrganizeViewModel : ObservableObject
{
    private readonly BookRepository _repo;
    private readonly MetadataService _meta = new();

    public ObservableCollection<OrganizeItem> Items { get; } = new();

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; set { Set(ref _isBusy, value); OnPropertyChanged(nameof(StatusText)); } }

    public string StatusText => IsBusy ? "正在联网比对（多书源）…" : $"共 {Items.Count} 条待确认";

    private readonly string _proxy;
    public OrganizeViewModel(BookRepository repo, string proxy = "") { _repo = repo; _proxy = proxy; }

    /// <summary>扫描书架：所有书都进待确认列表（疑似书名给理由；其余书做联网补全简介/封面/作者）。</summary>
    public int Analyze()
    {
        Items.Clear();
        foreach (var b in _repo.All())
        {
            var a = TitleAnalyzer.Analyze(Path.GetFileNameWithoutExtension(b.Path));
            Items.Add(new OrganizeItem
            {
                Book = b,
                OriginalTitle = b.Title,
                SuspectReasons = a.IsSuspect
                    ? string.Join("；", a.Reasons)
                    : "联网补全简介 / 封面 / 作者",
            });
        }
        OnPropertyChanged(nameof(StatusText));
        return Items.Count;
    }

    /// <summary>对全部待确认条目并发联网比对。</summary>
    public async Task CompareAllAsync()
    {
        IsBusy = true;
        try
        {
            var pending = Items.Where(i => !i.Accepted).ToList();
            var tasks = pending.Select(async item =>
            {
                var cands = await _meta.SearchAsync(item.Book.Title, item.Book.Author, _proxy);
                var best = cands.OrderByDescending(c => TitleSimilarity.Score(c.Title, item.Book.Title)).FirstOrDefault();
                return (item, best);
            });
            foreach (var (item, best) in await Task.WhenAll(tasks))
            {
                item.Candidate = best;
                if (best?.CoverUrl.Length > 0)
                    LoadCoverAsync(item, best.CoverUrl);
            }
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(StatusText));
        }
    }

    private async void LoadCoverAsync(OrganizeItem item, string url)
    {
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            var bytes = await http.GetByteArrayAsync(url);
            var dir = Path.Combine(AppContext.BaseDirectory, "data", "covers");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "org_" + Guid.NewGuid().ToString("N") + ".jpg");
            await File.WriteAllBytesAsync(path, bytes);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 160;
            bmp.StreamSource = new MemoryStream(bytes);
            bmp.EndInit();
            bmp.Freeze();
            item.Cover = bmp;
            item.Candidate = item.Candidate! with { CoverUrl = path };   // 落库时用本地路径
        }
        catch { /* 封面失败不阻断确认 */ }
    }

    /// <summary>采纳当前候选并落库（书名/作者/简介/封面）。</summary>
    public void Accept(OrganizeItem item)
    {
        if (item.Candidate == null || item.Accepted) return;
        item.Book.Title = item.Candidate.Title;
        if (item.Candidate.Author.Length > 0) item.Book.Author = item.Candidate.Author;
        if (item.Candidate.Intro.Length > 0) item.Book.Description = item.Candidate.Intro;
        if (item.Candidate.CoverUrl.StartsWith(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "data", "covers"))))
            item.Book.Cover = item.Candidate.CoverUrl;
        item.Accepted = true;
    }

    public void Ignore(OrganizeItem item) => item.Accepted = true;

    public void SaveAll()
    {
        _repo.Save();
        MessageBox.Show($"智能整理完成：已更新 {Items.Count(i => i.Accepted)} 条。", "ReaderPro",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
