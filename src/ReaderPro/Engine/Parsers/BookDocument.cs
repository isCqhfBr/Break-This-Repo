namespace ReaderPro.Engine.Parsers;

/// <summary>一本书的解析结果：标题/作者 + 章节列表 + 可选封面。</summary>
public sealed class BookDocument
{
    public string FilePath { get; init; } = "";
    public string Format { get; init; } = "";      // txt / epub / mobi / pdf ...
    public string Title { get; set; } = "";
    public string Author { get; set; } = "";
    public string? CoverPath { get; set; }
    public List<BookChapter> Chapters { get; } = new();
}

/// <summary>一个章节：标题 + 段落列表（每元素一个自然段，无首行缩进符）。</summary>
public sealed class BookChapter
{
    public string Title { get; set; } = "";
    public List<string> Paragraphs { get; } = new();
}
