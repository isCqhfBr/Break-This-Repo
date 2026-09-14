namespace ReaderPro.Engine.Parsers;

/// <summary>格式解析引擎接口（PRD 能力引擎层·格式解析引擎）。</summary>
public interface IBookParser
{
    /// <summary>解析一本书，返回结构化文档；失败抛 <see cref="ParserException"/>。</summary>
    BookDocument Parse(string filePath);
}

public sealed class ParserException : Exception
{
    public ParserException(string message, Exception? inner = null) : base(message, inner) { }
}
