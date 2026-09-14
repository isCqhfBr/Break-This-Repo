using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ReaderPro.Engine.Data;
using ReaderPro.Engine.Parsers;
using ReaderPro.Services;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;

namespace ReaderPro.ViewModels;

public sealed class CategoryNode
{
    public required string Name { get; init; }
    public string Icon { get; init; } = "▸";
    public ObservableCollection<CategoryNode> Children { get; } = new();
    public Func<BookRecord, bool>? Filter { get; init; }
}

/// <summary>书架卡片（含封面缩略图，INPC 支持延迟加载）。</summary>
public sealed class BookCard : ObservableObject
{
    public required BookRecord Book { get; init; }
    private ImageSource? _cover;
    public ImageSource? Cover { get => _cover; set => Set(ref _cover, value); }
    public string Title => Book.Title;
    public string Sub => Book.Author.Length > 0 ? Book.Author : Book.Format.ToUpperInvariant();
    public string Description => Book.Description ?? "";
    public string ProgressText => Book.Progress > 0 ? $"{(int)Book.Progress}%" : "";
}

/// <summary>正文段落（含朗读高亮标记，内容体验中心：读到哪高亮到哪）。</summary>
public sealed class ParagraphItem : ObservableObject
{
    public required string Text { get; init; }
    private bool _isCurrent;
    public bool IsCurrent { get => _isCurrent; set => Set(ref _isCurrent, value); }
}

/// <summary>主视图模型：书架 / 分类 / 导入 / 阅读 / 排版 / 主题 / 听书。</summary>
public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly BookRepository _repo;
    private readonly SettingsStore _settingsStore;
    private readonly AppSettings _settings;
    private readonly TtsService _tts = new();
    private readonly Dispatcher _ui = Application.Current.Dispatcher;
    private readonly Dictionary<string, ImageSource> _coverCache = new();
    private readonly SemaphoreSlim _coverGate = new(2, 2);   // 封面后台解码并发 ≤2（吸取旧项目卡顿教训）

    public ObservableCollection<BookCard> Books { get; } = new();
    public ObservableCollection<CategoryNode> Categories { get; } = new();
    public ObservableCollection<BookChapter> Chapters { get; } = new();
    public ObservableCollection<ParagraphItem> Paragraphs { get; } = new();

    public MainViewModel(BookRepository repo, SettingsStore settingsStore)
    {
        _repo = repo;
        _settingsStore = settingsStore;
        _settings = _settingsStore.Load();
        BuildCategories();
        foreach (var b in _repo.All())
            Books.Add(new BookCard { Book = b });
        ApplyCategory(null);
        // 应用已保存设置（系统服务中心：设置持久化）
        _fontSize = _settings.FontSize;
        _theme = Enum.TryParse<ThemeMode>(_settings.Theme, out var tm) ? tm : ThemeMode.Day;
        _tts.SetRate(_settings.SpeechRate);
        _tts.SetVolume(_settings.SpeechVolume);
        if (_settings.VoiceName.Length > 0) _tts.SetVoice(_settings.VoiceName);
        _shelfColumns = _settings.ShelfColumns;
        _metadataProxy = _settings.MetadataProxy;
    }

    // ---------- 书架布局（列数 + 右侧简介面板） ----------

    private int _shelfColumns = 2;
    public int ShelfColumns
    {
        get => _shelfColumns;
        set
        {
            if (Set(ref _shelfColumns, Math.Clamp(value, 1, 4)))
            {
                _settings.ShelfColumns = _shelfColumns;
                SaveSettings();
                OnPropertyChanged(nameof(ShelfItemWidth));
                OnPropertyChanged(nameof(IntroPanelVisibility));
            }
        }
    }
    public double ShelfItemWidth => _shelfColumns switch { 1 => 520, 2 => 280, 3 => 210, _ => 170 };
    public int[] ShelfColumnsOptions { get; } = { 1, 2, 3 };
    public Visibility IntroPanelVisibility => _shelfColumns <= 2 ? Visibility.Visible : Visibility.Collapsed;

    private string _metadataProxy = "";
    public string MetadataProxy
    {
        get => _metadataProxy;
        set { if (Set(ref _metadataProxy, value ?? "")) { _settings.MetadataProxy = _metadataProxy; SaveSettings(); } }
    }

    private BookCard? _selectedBook;
    public BookCard? SelectedBook
    {
        get => _selectedBook;
        set { if (Set(ref _selectedBook, value)) OnPropertyChanged(nameof(SelectedBookInfo)); }
    }
    /// <summary>右侧简介面板显示文本（选中书）。</summary>
    public string SelectedBookInfo
    {
        get
        {
            if (_selectedBook?.Book is not { } b) return "";
            var intro = b.Description?.Trim() ?? "";
            if (intro.Length == 0) intro = "（暂无简介，可点工具栏「智能整理」联网补全）";
            return $"{b.Title}\n{b.Author}\n\n{intro}";
        }
    }

    // ---------- 分类 ----------

    private void BuildCategories()
    {
        Categories.Clear();
        Categories.Add(new CategoryNode { Name = "全部书籍", Icon = "▣", Filter = null });
        Categories.Add(new CategoryNode { Name = "我的收藏", Icon = "★",
            Filter = b => b.Tags.Contains("收藏") });
        Categories.Add(new CategoryNode { Name = "按作者", Icon = "✎" });
        Categories.Add(new CategoryNode { Name = "按格式", Icon = "▤" });
        Categories.Add(new CategoryNode { Name = "标签", Icon = "♯" });

        foreach (var g in _repo.All().GroupBy(b => b.Author).OrderBy(g => g.Key))
            Categories[2].Children.Add(new CategoryNode { Name = g.Key,
                Filter = b => b.Author == g.Key });
        foreach (var g in _repo.All().GroupBy(b => b.Format).OrderBy(g => g.Key))
            Categories[3].Children.Add(new CategoryNode { Name = g.Key,
                Filter = b => string.Equals(b.Format, g.Key, StringComparison.OrdinalIgnoreCase) });
        foreach (var tag in _repo.All().SelectMany(b => b.Tags).Distinct().OrderBy(t => t))
            Categories[4].Children.Add(new CategoryNode { Name = tag,
                Filter = b => b.Tags.Contains(tag) });
    }

    private CategoryNode? _selectedCategory;
    public CategoryNode? SelectedCategory
    {
        get => _selectedCategory;
        set { if (Set(ref _selectedCategory, value)) ApplyCategory(value); }
    }

    private void ApplyCategory(CategoryNode? node)
    {
        IEnumerable<BookRecord> q = _repo.All();
        if (node?.Filter != null) q = q.Where(node.Filter);
        if (!string.IsNullOrWhiteSpace(SearchText))
            q = q.Where(b => b.Title.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                          || b.Author.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        Books.Clear();
        foreach (var b in q.OrderByDescending(b => b.LastRead))
            Books.Add(new BookCard { Book = b });
        foreach (var card in Books) QueueCover(card);
    }

    public void Reload() => ApplyCategory(SelectedCategory);

    private string _searchText = "";
    public string SearchText
    {
        get => _searchText;
        set { if (Set(ref _searchText, value)) ApplyCategory(SelectedCategory); }
    }

    // ---------- 封面加载（后台解码 + 缓存，避免滚动卡顿） ----------

    private void QueueCover(BookCard card)
    {
        if (card.Cover != null) return;
        if (_coverCache.TryGetValue(card.Book.Id, out var cached)) { card.Cover = cached; return; }
        _ = Task.Run(async () =>
        {
            await _coverGate.WaitAsync();
            try
            {
                var src = LoadCoverImage(card.Book);
                if (src == null) return;
                lock (_coverCache)
                {
                    if (_coverCache.Count > 400) _coverCache.Remove(_coverCache.Keys.First());
                    _coverCache[card.Book.Id] = src;
                }
                _ = _ui.BeginInvoke(() => card.Cover = src);
            }
            finally { _coverGate.Release(); }
        });
    }

    private static ImageSource? LoadCoverImage(BookRecord book)
    {
        try
        {
            var path = book.Cover;
            if (path == null || !File.Exists(path)) return null;
            using var fs = File.OpenRead(path);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;   // 立即解码，流可关闭
            bmp.DecodePixelWidth = 200;
            bmp.StreamSource = fs;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    // ---------- 导入（智能扫描） ----------

    public async Task<int> ScanAndImportAsync(string folder)
    {
        var files = await Task.Run(() => _repo.ScanFolder(folder));
        int added = 0;
        foreach (var f in files)
        {
            if (_repo.FindByPath(f) != null) continue;   // 避免重复导入（PRD 智能导入）
            try
            {
                var doc = await Task.Run(() => ParserFactory.Create(f).Parse(f));
                var book = new BookRecord
                {
                    Title = doc.Title.Length > 0 ? doc.Title : Path.GetFileNameWithoutExtension(f),
                    Author = doc.Author,
                    Format = Path.GetExtension(f).TrimStart('.').ToLowerInvariant(),
                    Path = f,
                    Cover = doc.CoverPath,
                    FileSize = new FileInfo(f).Length,
                    LastRead = DateTime.MinValue,
                };
                _repo.AddOrUpdate(book);
                added++;
            }
            catch { /* 单文件失败跳过，不中断整批（PRD 智能扫描容错） */ }
        }
        _repo.Save();
        _ = _ui.BeginInvoke(() =>
        {
            Books.Clear();
            foreach (var b in _repo.All().OrderByDescending(b => b.LastRead))
                Books.Add(new BookCard { Book = b });
            foreach (var card in Books.ToList()) QueueCover(card);
        });
        return added;
    }

    // ---------- 智能整理（元数据智能中心入口） ----------

    /// <summary>打开「智能整理」确认窗口；关闭后刷新书架。</summary>
    public void OpenOrganize()
    {
        var win = new Views.OrganizeWindow(_repo, _settings.MetadataProxy) { Owner = Application.Current.MainWindow };
        win.ShowDialog();
        ReloadShelf();
    }

    private void ReloadShelf()
    {
        Books.Clear();
        foreach (var b in _repo.All().OrderByDescending(b => b.LastRead))
            Books.Add(new BookCard { Book = b });
        foreach (var card in Books.ToList()) QueueCover(card);
        BuildCategories();   // 书名/作者/标签可能已变，重建分类树
    }

    // ---------- 阅读 ----------

    public bool IsReading { get; private set; }
    public BookRecord? CurrentBook { get; private set; }

    public async Task<bool> OpenBookAsync(BookCard card)
    {
        try
        {
            var doc = await Task.Run(() => ParserFactory.Create(card.Book.Path).Parse(card.Book.Path));
            _ui.Invoke(() =>
            {
                CurrentBook = card.Book;
                CurrentBook.LastRead = DateTime.Now;
                Chapters.Clear();
                foreach (var c in doc.Chapters) Chapters.Add(c);
                IsReading = true;
                OnPropertyChanged(nameof(IsReading));
                OnPropertyChanged(nameof(CurrentBook));
                SelectChapter(0);
                ApplyTheme();
            });
            return true;
        }
        catch (Exception e)
        {
            MessageBox.Show("打开失败：" + e.Message, "ReaderPro", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    public void CloseReading()
    {
        StopSpeaking();
        SaveProgress();
        IsReading = false;
        OnPropertyChanged(nameof(IsReading));
    }

    private int _selectedChapter;
    public int SelectedChapterIndex
    {
        get => _selectedChapter;
        set { if (Set(ref _selectedChapter, value)) SelectChapter(value); }
    }

    public void SelectChapter(int index)
    {
        if (index < 0 || index >= Chapters.Count) return;
        RebuildParagraphs(index);
        _chapter = index;
        OnPropertyChanged(nameof(ChapterTitle));
        if (IsSpeaking && !_isSpeechFollow)
            StartSpeaking(index, 0);   // 用户手动换章 → 朗读跟随新章节（进度互通）
    }

    /// <summary>按当前缩进设置重建章节段落（首行缩进两全角字符，PRD 3.3）。</summary>
    private void RebuildParagraphs(int index)
    {
        Paragraphs.Clear();
        foreach (var p in Chapters[index].Paragraphs)
            Paragraphs.Add(new ParagraphItem
            {
                Text = p.Length > 0 && p[0] != '　' ? "　　" + p : p,
            });
    }

    private int _chapter;
    public string ChapterTitle => _chapter < Chapters.Count ? Chapters[_chapter].Title : "";

    public string BodyText => string.Join("\n\n", Paragraphs.Select(p => p.Text));

    public void SaveProgress()
    {
        if (CurrentBook == null) return;
        var scroll = 0.0;
        if (ChapterScroll != null)
        {
            var doc = (System.Windows.Controls.ScrollViewer)ChapterScroll;
            scroll = doc.ExtentHeight > 0 ? doc.VerticalOffset / doc.ExtentHeight : 0;
        }
        _repo.SaveProgress(CurrentBook.Id, _chapter, scroll);
        CurrentBook.Progress = _chapter + scroll;   // 粗略进度
        _repo.Save();
    }

    public System.Windows.Controls.ScrollViewer? ChapterScroll { get; set; }

    // ---------- 排版（PRD 3.3 像素级参数） ----------

    private double _fontSize = 20;
    public double FontSize { get => _fontSize; set { if (Set(ref _fontSize, Math.Clamp(value, 12, 48))) { SaveSettings(); } } }
    public double LineHeight => Math.Round(_fontSize * 1.8, 1);
    public double ParagraphSpacing => Math.Round(_fontSize * 0.7, 1);
    public double LeftMargin => Math.Max(24, _fontSize * 1.2);
    public double RightMargin => LeftMargin;

    public void FontSizeUp() => FontSize += 1;
    public void FontSizeDown() => FontSize -= 1;

    // ---------- 主题 ----------

    private ThemeMode _theme = ThemeMode.Day;
    public ThemeMode ThemeMode
    {
        get => _theme;
        set { if (Set(ref _theme, value)) { ApplyTheme(); SaveSettings(); } }
    }

    public void ToggleTheme() => ThemeMode = (ThemeMode)(((int)ThemeMode + 1) % 3);

    public Brush PageBackground => new SolidColorBrush(ThemeManager.Get(_theme).Background);
    public Brush PageForeground => new SolidColorBrush(ThemeManager.Get(_theme).Foreground);
    public Brush AccentBrush => new SolidColorBrush(ThemeManager.Get(_theme).Accent);
    public Brush ShelfBackground => new SolidColorBrush(ThemeManager.Get(_theme).ShelfBackground);
    public Color AccentColor => ThemeManager.Get(_theme).Accent;

    public void ApplyTheme()
    {
        OnPropertyChanged(nameof(PageBackground));
        OnPropertyChanged(nameof(PageForeground));
        OnPropertyChanged(nameof(AccentBrush));
        OnPropertyChanged(nameof(ShelfBackground));
        OnPropertyChanged(nameof(AccentColor));
        OnPropertyChanged(nameof(ThemeMode));
        OnPropertyChanged(nameof(ThemeName));
        OnPropertyChanged(nameof(ParagraphHighlightBrush));
    }

    // ---------- 听书（内容体验中心：读到哪听到哪，进度互通） ----------

    public bool IsSpeaking { get; private set; }
    public bool IsPaused { get; private set; }
    public string[] Voices => _tts.InstalledVoices;

    public string[] ThemeNames { get; } = { "白天", "夜间", "护眼" };
    public string ThemeName
    {
        get => _theme switch { ThemeMode.Night => "夜间", ThemeMode.Sepia => "护眼", _ => "白天" };
        set => ThemeMode = value switch { "夜间" => ThemeMode.Night, "护眼" => ThemeMode.Sepia, _ => ThemeMode.Day };
    }

    public string SpeakButtonText => !IsSpeaking ? "▶ 朗读" : IsPaused ? "▶ 继续" : "⏸ 暂停";
    public Brush ParagraphHighlightBrush => new SolidColorBrush(Color.FromArgb(0x2A,
        AccentColor.R, AccentColor.G, AccentColor.B));

    /// <summary>朗读位置变化 → 请求正文滚动跟随（由 View 订阅 ScrollIntoView）。</summary>
    public event Action<int, int>? ScrollToParagraph;

    private int _speakingChapter = -1;
    private int _speakingPara = -1;
    private bool _isSpeechFollow;   // 朗读驱动换章时的守卫，防止 SelectChapter 递归重启朗读

    /// <summary>组装跨章朗读队列：从 fromChapter/fromParagraph 读到书尾，章首插章节标题提示。</summary>
    public static List<SpeakUnit> BuildSpeakQueue(IReadOnlyList<BookChapter> chapters, int fromChapter, int fromParagraph)
    {
        var q = new List<SpeakUnit>();
        for (int c = fromChapter; c < chapters.Count; c++)
        {
            var ch = chapters[c];
            if (ch.Paragraphs.Count == 0) continue;
            if (q.Count > 0 && ch.Title.Length > 0)
                q.Add(new SpeakUnit(c, -1, ch.Title));   // 章首标题提示（para=-1 哨兵，不高亮不滚动）
            int start = c == fromChapter ? Math.Max(0, fromParagraph) : 0;
            for (int p = start; p < ch.Paragraphs.Count; p++)
                q.Add(new SpeakUnit(c, p, ch.Paragraphs[p]));
        }
        return q;
    }

    public string SpeakingStatus
    {
        get
        {
            if (!IsSpeaking) return "";
            if (_speakingChapter < 0) return "";
            var prefix = IsPaused ? "⏸ 已暂停" : "▶ 正在朗读";
            if (_speakingPara < 0) return $"{prefix}：{_speakingChapterText}";
            var total = _speakingChapter < Chapters.Count ? Chapters[_speakingChapter].Paragraphs.Count : 0;
            return $"{prefix}：{_speakingChapterText} · 第 {_speakingPara + 1}/{total} 段";
        }
    }

    private string _speakingChapterText = "";
    private string SpeakingChapterText
    {
        get => _speakingChapterText;
        set { if (Set(ref _speakingChapterText, value)) OnPropertyChanged(nameof(SpeakingStatus)); }
    }

    /// <summary>从指定章节/段开始跨章朗读（阅读位置 = 朗读起点，进度互通）。</summary>
    public void StartSpeaking(int fromChapter = -1, int fromParagraph = 0)
    {
        if (Chapters.Count == 0) return;
        if (fromChapter < 0) fromChapter = _chapter;
        if (fromChapter >= Chapters.Count) return;
        var q = BuildSpeakQueue(Chapters.ToList(), fromChapter, fromParagraph);
        if (q.Count == 0) return;
        _tts.UnitStarted -= OnUnitStarted;
        _tts.UnitStarted += OnUnitStarted;
        _tts.Speak(q, 0);
        IsSpeaking = true;
        IsPaused = false;
        _speakingChapter = fromChapter;
        _speakingPara = fromParagraph;
        SpeakingChapterText = Chapters[fromChapter].Title;
        OnPropertyChanged(nameof(IsSpeaking));
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(SpeakButtonText));
        OnPropertyChanged(nameof(SpeakingStatus));
    }

    private void OnUnitStarted(int chapter, int para)
    {
        _ui.BeginInvoke(() =>
        {
            if (!IsSpeaking) return;
            _speakingChapter = chapter;
            _speakingPara = para;
            SpeakingChapterText = chapter < Chapters.Count ? Chapters[chapter].Title : "";
            if (chapter != _chapter)
            {
                _isSpeechFollow = true;
                try { SelectChapter(chapter); }
                finally { _isSpeechFollow = false; }
            }
            ClearParagraphHighlight();
            if (para >= 0 && para < Paragraphs.Count)
            {
                Paragraphs[para].IsCurrent = true;
                ScrollToParagraph?.Invoke(chapter, para);
            }
            OnPropertyChanged(nameof(SpeakingStatus));
        });
    }

    private void ClearParagraphHighlight()
    {
        foreach (var p in Paragraphs)
            p.IsCurrent = false;
    }

    public void TogglePause()
    {
        if (!IsSpeaking) return;
        if (IsPaused)
        {
            _tts.Resume();
            IsPaused = false;
        }
        else
        {
            _tts.Pause();
            IsPaused = true;
        }
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(SpeakButtonText));
        OnPropertyChanged(nameof(SpeakingStatus));
    }

    public void StopSpeaking()
    {
        _tts.Stop();
        IsSpeaking = false;
        IsPaused = false;
        _speakingChapter = -1;
        _speakingPara = -1;
        SpeakingChapterText = "";
        ClearParagraphHighlight();
        OnPropertyChanged(nameof(IsSpeaking));
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(SpeakButtonText));
        OnPropertyChanged(nameof(SpeakingStatus));
    }

    public void SetSpeechRate(int rate)
    {
        _tts.SetRate(rate);
        _settings.SpeechRate = Math.Clamp(rate, -10, 10);
        SaveSettings();
    }

    private int _volume = 100;
    public int Volume
    {
        get => _volume;
        set { if (Set(ref _volume, Math.Clamp(value, 0, 100))) { _tts.SetVolume(_volume); SaveSettings(); } }
    }

    private string _voiceName = "";
    public string SelectedVoice
    {
        get => _voiceName;
        set { if (Set(ref _voiceName, value)) { _tts.SetVoice(value); _settings.VoiceName = value; SaveSettings(); } }
    }

    public string DataDir => App.DataRoot;
    public static string AppVersion => "v0.2.6";

    // ---------- 退出行为（系统服务中心：托盘 + 退出确认） ----------
    public bool AskOnClose => _settings.AskOnClose;
    public string CloseAction => _settings.CloseAction;

    public void RememberCloseChoice(string action)
    {
        _settings.AskOnClose = false;
        _settings.CloseAction = action;
        SaveSettings();
    }

    /// <summary>设置持久化（data/settings.json）。</summary>
    public void SaveSettings()
    {
        _settings.FontSize = _fontSize;
        _settings.Theme = _theme.ToString();
        _settings.SpeechVolume = _volume;
        _settings.VoiceName = _voiceName;
        _settingsStore.Save(_settings);
    }

    /// <summary>打开统一设置面板（系统服务中心）。</summary>
    public void OpenSettings()
    {
        var win = new Views.SettingsWindow(this) { Owner = Application.Current.MainWindow };
        win.ShowDialog();
    }

    public void Dispose()
    {
        _tts.Dispose();
        _coverGate.Dispose();
    }
}
