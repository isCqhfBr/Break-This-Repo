using System.Windows;

namespace ReaderPro.Views;

public partial class ExitDialog : Window
{
    /// <summary>"exit"=直接退出；"tray"=最小化到托盘。</summary>
    public string SelectedAction { get; private set; } = "tray";
    public bool RememberChoice { get; private set; }

    public ExitDialog()
    {
        InitializeComponent();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        SelectedAction = RadioExit.IsChecked == true ? "exit" : "tray";
        RememberChoice = Remember.IsChecked == true;
        DialogResult = true;
    }
}
