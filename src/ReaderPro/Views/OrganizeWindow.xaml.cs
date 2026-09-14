using System.Windows;
using ReaderPro.Engine.Data;
using ReaderPro.ViewModels;
using MessageBox = System.Windows.MessageBox;

namespace ReaderPro.Views;

public partial class OrganizeWindow : Window
{
    private readonly OrganizeViewModel _vm;

    public OrganizeWindow(BookRepository repo, string proxy = "")
    {
        InitializeComponent();
        _vm = new OrganizeViewModel(repo, proxy);
        DataContext = _vm;
        Loaded += (_, _) =>
        {
            var count = _vm.Analyze();
            if (count == 0)
            {
                MessageBox.Show("当前书架没有疑似书名有误的书。", "智能整理",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                Close();
            }
        };
    }

    private async void Compare_Click(object sender, RoutedEventArgs e) => await _vm.CompareAllAsync();

    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is OrganizeItem item)
            _vm.Accept(item);
    }

    private void Ignore_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is OrganizeItem item)
            _vm.Ignore(item);
    }

    private void AcceptAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _vm.Items.ToList())
            _vm.Accept(item);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _vm.SaveAll();
        Close();
    }
}
