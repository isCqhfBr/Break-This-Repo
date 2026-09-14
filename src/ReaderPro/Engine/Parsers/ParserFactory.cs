using System.IO;

namespace ReaderPro.Engine.Parsers;

/// <summary>按扩展名分派解析器（M1：txt/epub；mobi 等后续里程碑接入）。</summary>
public static class ParserFactory
{
    private static readonly Dictionary<string, Func<IBookParser>> Registry = new(StringComparer.OrdinalIgnoreCase)
    {
        [".txt"] = () => new TxtParser(),
        [".epub"] = () => new EpubParser(),
        [".mobi"] = () => new MobiParser(),
        [".prc"] = () => new MobiParser(),
        [".azw"] = () => new MobiParser(),
    };

    /// <summary>注册自定义解析器（插件扩展预留，PRD 3.10）。</summary>
    public static void Register(string extension, Func<IBookParser> factory)
        => Registry[extension] = factory;

    public static IBookParser Create(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        if (Registry.TryGetValue(ext, out var factory))
            return factory();
        throw new ParserException($"暂不支持该格式：{ext}");
    }

    public static bool IsSupported(string filePath) =>
        Registry.ContainsKey(Path.GetExtension(filePath));
}
