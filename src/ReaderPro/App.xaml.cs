using System.IO;
using System.Windows;
using ReaderPro.Engine.Data;
using Application = System.Windows.Application;

namespace ReaderPro;

/// <summary>应用入口。启动时建立数据目录（data/ 与程序分离，PRD 数据可迁移）。</summary>
public partial class App : Application
{
    public static string DataRoot { get; private set; } = "";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // 全局崩溃日志（闪退定位：data/crash.log）
        DispatcherUnhandledException += (s, args) =>
        {
            try { File.WriteAllText(Path.Combine(DataRoot, "crash.log"),
                $"[UI {DateTime.Now:yyyy-MM-dd HH:mm:ss}]\n{args.Exception}"); } catch { }
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            try { File.WriteAllText(Path.Combine(DataRoot, "crash.log"),
                $"[NonUI {DateTime.Now:yyyy-MM-dd HH:mm:ss}]\n{args.ExceptionObject}"); } catch { }
        };

        // data/ 与程序同级（便携模式）；也可由启动参数 --data=path 覆盖
        DataRoot = Path.Combine(AppContext.BaseDirectory, "data");
        try
        {
            Directory.CreateDirectory(Path.Combine(DataRoot, "covers"));
            Directory.CreateDirectory(Path.Combine(DataRoot, "cache"));
        }
        catch
        {
            // 只读目录场景降级到用户目录
            DataRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ReaderPro");
            Directory.CreateDirectory(Path.Combine(DataRoot, "covers"));
            Directory.CreateDirectory(Path.Combine(DataRoot, "cache"));
        }

        var repo = new BookRepository(DataRoot);
        var settings = new SettingsStore(DataRoot);
        var win = new MainWindow(repo, settings);
        MainWindow = win;
        win.Show();
    }
}
