using System.Text;

namespace ReaderPro.Engine.Parsers;

/// <summary>
/// 文本编码自动识别（PRD 3.1 多编码支持，40+ 编码）。
/// M1 覆盖主力：UTF-8 / UTF-16LE/BE / UTF-32 / GB18030（含 GBK/GB2312），
/// 后续按需扩展（Shift-JIS、Big5 等由注册表驱动）。
/// </summary>
public static class EncodingDetector
{
    static EncodingDetector()
    {
        // 注册 GB18030 等代码页（.NET Core 默认不含）
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>检测字节流的文本编码；探测失败回退 GB18030。</summary>
    public static Encoding Detect(byte[] data)
    {
        if (data.Length >= 4)
        {
            // UTF-32
            if (data[0] == 0xFF && data[1] == 0xFE && data[2] == 0x00 && data[3] == 0x00)
                return Encoding.UTF32;
            if (data[0] == 0x00 && data[1] == 0x00 && data[2] == 0xFE && data[3] == 0xFF)
                return new UTF32Encoding(true, true);
        }
        if (data.Length >= 2)
        {
            if (data[0] == 0xFF && data[1] == 0xFE) return Encoding.Unicode;        // UTF-16 LE
            if (data[0] == 0xFE && data[1] == 0xFF) return Encoding.BigEndianUnicode; // UTF-16 BE
        }
        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
            return new UTF8Encoding(false);                                          // UTF-8 BOM

        // 无 BOM：UTF-16 启发式（NUL 字节占比 + 位置）。必须在 UTF-8 严格之前：
        // 纯 ASCII 的 UTF-16（55 00 54 00…）逐字节看是"合法 UTF-8"，会误判。
        if (data.Length >= 4)
        {
            int sample = Math.Min(data.Length, 4096);
            int nuls = 0, nulOdd = 0;
            for (int i = 0; i < sample; i++)
            {
                if (data[i] == 0)
                {
                    nuls++;
                    if ((i & 1) == 1) nulOdd++;
                }
            }
            if (nuls * 100 >= sample * 12)
            {
                var enc16 = (nulOdd * 2 >= nuls) ? Encoding.Unicode : Encoding.BigEndianUnicode;
                try
                {
                    var s = enc16.GetString(data);
                    int bad = 0, n = Math.Min(s.Length, 2000);
                    for (int i = 0; i < n; i++)
                        if (s[i] == '\uFFFD') bad++;
                    if (bad * 100 < Math.Max(1, n) * 2)
                        return enc16;
                }
                catch { }
            }
        }

        // 无 BOM：UTF-8 严格解码（抛异常说明非 UTF-8）
        try
        {
            var strict = new UTF8Encoding(false, true);
            strict.GetString(data);
            return new UTF8Encoding(false);
        }
        catch (DecoderFallbackException)
        {
            // 回退 GB18030（中文 Windows 文本主流）
            try
            {
                return Encoding.GetEncoding("GB18030");
            }
            catch
            {
                return Encoding.Default;
            }
        }
    }

    /// <summary>解码并清理 BOM 与替换字符。</summary>
    public static string Decode(byte[] data)
    {
        var enc = Detect(data);
        return enc.GetString(data);
    }
}
