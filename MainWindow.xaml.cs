using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using ArkAutomata.GUI.ViewModels;

namespace ArkAutomata.GUI;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;

        StartLogTailing();
    }

    private void OpenLogFolder_Click(object sender, RoutedEventArgs e)
    {
        var logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "autologs");
        if (Directory.Exists(logDir))
        {
            Process.Start("explorer.exe", logDir);
        }
    }

    private async void StartLogTailing()
    {
        var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "autologs", $"automata_{DateTime.Now:yyyy-MM-dd}.log");

        await Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    if (File.Exists(logPath))
                    {
                        var lines = await File.ReadAllLinesAsync(logPath);
                        var lastLines = lines.TakeLast(100);

                        Dispatcher.Invoke(() =>
                        {
                            LogTextBox.Text = string.Join(Environment.NewLine, lastLines);
                            LogTextBox.ScrollToEnd();
                        });
                    }
                }
                catch { }

                await Task.Delay(2000);
            }
        });
    }
}

public class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        return value is bool b ? !b : value;
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        return value is bool b ? !b : value;
    }
}
