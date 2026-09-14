using System.Collections.ObjectModel;
using System.Windows;
using ReaderPro.Engine.Data;
using ReaderPro.ViewModels;
using MessageBox = System.Windows.MessageBox;

namespace ReaderPro.Views;

public partial class BatchManageWindow : Window
{
    public sealed class Row
    {
        public bool Chk { get; set; }
        public string Title { get; init; } = "";
        public string Author { get; init; } = "";
        public string TagsText { get; set; } = "";
        public BookRecord Book { get; init; } = null!;
    }

    private readonly BookRepository _repo;
    private readonly MainViewModel _vm;
    private readonly ObservableCollection<Row> _rows = new();

    public BatchManageWindow(BookRepository repo, MainViewModel vm)
    {
        InitializeComponent();
        _repo = repo;
        _vm = vm;
        Grid.ItemsSource = _rows;
        foreach (var b in repo.All())
            _rows.Add(new Row
            {
                Title = b.Title,
                Author = b.Author,
                TagsText = string.Join(", ", b.Tags),
                Book = b,
            });
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        var n = _rows.Count(r => r.Chk);
        StatusText.Text = $"共 {_rows.Count} 本，已选 {n}";
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var r in _rows) r.Chk = true;
        Grid.Items.Refresh();
        RefreshStatus();
    }

    private void SelectNone_Click(object sender, RoutedEventArgs e)
    {
        foreach (var r in _rows) r.Chk = false;
        Grid.Items.Refresh();
        RefreshStatus();
    }

    private void BatchDelete_Click(object sender, RoutedEventArgs e)
    {
        var sel = _rows.Where(r => r.Chk).ToList();
        if (sel.Count == 0) { MessageBox.Show("请先勾选要删除的书。", "批量管理"); return; }
        var ok = MessageBox.Show($"确定从书架移除 {sel.Count} 本书？（不删本地文件）",
            "确认", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (ok != MessageBoxResult.Yes) return;
        foreach (var r in sel) _repo.Remove(r.Book.Id);
        _repo.Save();
        foreach (var r in sel) _rows.Remove(r);
        _vm.Reload();
        RefreshStatus();
    }

    private void BatchTag_Click(object sender, RoutedEventArgs e)
    {
        var tag = TagBox.Text.Trim();
        if (tag.Length == 0) { MessageBox.Show("请输入标签名。", "批量管理"); return; }
        var sel = _rows.Where(r => r.Chk).ToList();
        if (sel.Count == 0) { MessageBox.Show("请先勾选书本。", "批量管理"); return; }
        foreach (var r in sel)
        {
            if (!r.Book.Tags.Contains(tag)) r.Book.Tags.Add(tag);
            r.TagsText = string.Join(", ", r.Book.Tags);
        }
        _repo.Save();
        Grid.Items.Refresh();
        _vm.Reload();
        MessageBox.Show($"已给 {sel.Count} 本书加标签「{tag}」。", "批量管理");
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
