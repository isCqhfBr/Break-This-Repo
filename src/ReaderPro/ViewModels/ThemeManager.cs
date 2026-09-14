using System.Windows.Media;
// UseWindowsForms 后 System.Drawing 与 WPF 类型冲突，显式别名
using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;

namespace ReaderPro.ViewModels;

/// <summary>主题模式（PRD 3.3：白天/夜间/护眼；M2 扩展 15 套主题与自定义 RGB）。</summary>
public enum ThemeMode { Day, Night, Sepia }

public sealed record ThemeColors(string Name, Color Background, Color Foreground, Color Accent,
    Color CoverFrame, Color ShelfBackground);

/// <summary>内置主题色板。</summary>
public static class ThemeManager
{
    public static ThemeColors Get(ThemeMode mode) => mode switch
    {
        ThemeMode.Night => new ThemeColors("夜间", Color.FromRgb(0x14, 0x16, 0x1B),
            Color.FromRgb(0xC8, 0xCC, 0xD4), Color.FromRgb(0x4E, 0xA8, 0xFE),
            Color.FromRgb(0x24, 0x27, 0x2E), Color.FromRgb(0x0F, 0x11, 0x15)),
        ThemeMode.Sepia => new ThemeColors("护眼", Color.FromRgb(0xF4, 0xEC, 0xD9),
            Color.FromRgb(0x3B, 0x30, 0x20), Color.FromRgb(0x8A, 0x6D, 0x3B),
            Color.FromRgb(0xE8, 0xDD, 0xC4), Color.FromRgb(0xEF, 0xE6, 0xD0)),
        _ => new ThemeColors("白天", Color.FromRgb(0xFA, 0xFA, 0xF7),
            Color.FromRgb(0x21, 0x21, 0x21), Color.FromRgb(0x2F, 0x6F, 0xED),
            Color.FromRgb(0xEE, 0xEE, 0xEA), Color.FromRgb(0xF2, 0xF2, 0xEE)),
    };
}
