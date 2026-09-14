using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ReaderPro.ViewModels;

namespace ReaderPro.Views;

public partial class SettingsWindow : Window
{
    private readonly MainViewModel _vm;

    public SettingsWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    private void Rate_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        => _vm.SetSpeechRate((int)e.NewValue);

    private void OpenDataDir_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", _vm.DataDir) { UseShellExecute = true }); }
        catch { }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        _vm.SaveSettings();
        Close();
    }
}
