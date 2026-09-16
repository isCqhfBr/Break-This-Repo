using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using ReaderPro.Engine.Data;
using ReaderPro.ViewModels;
using ReaderPro.Views;
using ListBox = System.Windows.Controls.ListBox;
using MessageBox = System.Windows.MessageBox;
using Button = System.Windows.Controls.Button;
using Cursors = System.Windows.Input.Cursors;

namespace ReaderPro;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly BookRepository _repo;
    private readonly System.Windows.Forms.NotifyIcon _tray = new();
    private bool _exitingFromTray;

    public MainWindow(BookRepository repo, SettingsStore settings)
    {
        InitializeComponent();
        _repo = repo;
        _vm = new MainViewModel(repo, settings);
        DataContext = _vm;
        _vm.ScrollToParagraph += (_, para) =>
        {
            if (para >= 0 && para < BodyList.Items.Count)
                BodyList.ScrollIntoView(BodyList.Items[para]);
        };
        BodyList.Loaded += (_, _) =>
        {
            var sv = FindScrollViewer(BodyList);
            if (sv != null)
            {
                _vm.ChapterScroll = sv;
                sv.ScrollChanged += (_, _) => { };
            }
        };
        InitTray();
    }

    // ---------- 系统托盘（PRD 3.4.3：最小化到托盘、一键唤起） ----------

    private void InitTray()
    {
        _tray.Icon = System.Drawing.SystemIcons.Application;
        _tray.Text = "ReaderPro";
        _tray.Visible = true;
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("显示主窗口", null, (_, _) => ShowFromTray());
        menu.Items.Add("退出", null, (_, _) => { _exitingFromTray = true; Close(); });
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowFromTray();
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer sv) return sv;
            var found = FindScrollViewer(child);
            if (found != null) return found;
        }
        return null;
    }

    // ---------- 书架 ----------

    private void Category_Changed(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is CategoryNode node)
            _vm.SelectedCategory = node;
    }

    private void Organize_Click(object sender, RoutedEventArgs e) => _vm.OpenOrganize();

    // 管理下拉菜单：导入 / 智能整理 / 批量管理
    private void ManageMenu_Click(object sender, RoutedEventArgs e)
    {
        var btn = (Button)sender;
        var menu = new ContextMenu();
        var mi1 = new MenuItem { Header = "扫描导入书本…" };
        mi1.Click += (_, _) => _ = ImportAsync();
        var mi2 = new MenuItem { Header = "智能整理（联网补全简介/作者）" };
        mi2.Click += (_, _) => _vm.OpenOrganize();
        var mi3 = new MenuItem { Header = "批量管理书本…" };
        mi3.Click += (_, _) => new Views.BatchManageWindow(_repo, _vm) { Owner = this }.ShowDialog();
        menu.Items.Add(mi1);
        menu.Items.Add(mi2);
        menu.Items.Add(new Separator());
        menu.Items.Add(mi3);
        menu.PlacementTarget = btn;
        menu.IsOpen = true;
    }

    private void Book_Selected(object sender, SelectionChangedEventArgs e)
    {
        // 单击选中：右侧显示简介（不进阅读）
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is BookCard card)
            _vm.SelectedBook = card;
    }

    private void Book_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        // 双击打开阅读
        if (((ListBox)sender).SelectedItem is BookCard card)
            _ = _vm.OpenBookAsync(card);
    }

    private async Task ImportAsync()
    {
        var dlg = new OpenFolderDialog
        {
            Title = "选择小说书库文件夹（智能扫描导入）",
            Multiselect = false,
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            var added = await _vm.ScanAndImportAsync(dlg.FolderName);
            MessageBox.Show($"导入完成：新增 {added} 本。", "ReaderPro", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("导入失败：" + ex.Message, "ReaderPro", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    // ---------- 阅读 ----------

    private void BackToShelf_Click(object sender, RoutedEventArgs e) => _vm.CloseReading();

    private bool _miniMode;
    private void MiniMode_Click(object sender, RoutedEventArgs e)
    {
        _miniMode = !_miniMode;
        if (_miniMode)
        {
            // 进入小窗：缩成窄长可缩放
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            Width = 420;
            Height = 680;
            Topmost = true;
        }
        else
        {
            // 恢复
            Topmost = false;
            Width = 1100;
            Height = 720;
        }
    }

    private void FontUp_Click(object sender, RoutedEventArgs e) => _vm.FontSizeUp();
    private void FontDown_Click(object sender, RoutedEventArgs e) => _vm.FontSizeDown();
    private void Theme_Click(object sender, RoutedEventArgs e) => _vm.ToggleTheme();
    private void Settings_Click(object sender, RoutedEventArgs e) => _vm.OpenSettings();

    private void Speak_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.IsSpeaking) _vm.TogglePause();
        else _vm.StartSpeaking();
    }

    private void StopSpeak_Click(object sender, RoutedEventArgs e) => _vm.StopSpeaking();

    private void Rate_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        => _vm.SetSpeechRate((int)e.NewValue);

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // 托盘菜单"退出"是用户明确退出，不再询问
        if (!_exitingFromTray)
        {
            if (!_vm.AskOnClose)
            {
                // 已记住选择
                if (_vm.CloseAction == "tray")
                {
                    e.Cancel = true;
                    Hide();
                    return;
                }
            }
            else
            {
                var dlg = new ExitDialog { Owner = this };
                if (dlg.ShowDialog() != true)
                {
                    e.Cancel = true;   // 取消关闭，留在程序
                    return;
                }
                if (dlg.RememberChoice)
                    _vm.RememberCloseChoice(dlg.SelectedAction);
                if (dlg.SelectedAction == "tray")
                {
                    e.Cancel = true;
                    Hide();
                    return;
                }
            }
        }
        // 真退出
        _vm.SaveProgress();
        _vm.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        base.OnClosing(e);
    }
}
