using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ArkAutomata.GUI.ViewModels;

namespace ArkAutomata.GUI.Views;

public partial class RconConsoleWindow : Window
{
    private readonly ServerViewModel _server;
    private RconClient? _rcon;
    private bool _isConnected;

    public RconConsoleWindow(ServerViewModel server)
    {
        InitializeComponent();
        _server = server;

        ServerNameText.Text = $"🔧 RCON Console - {_server.Name}";
        ServerInfoText.Text = $"Game Port: {_server.GamePort}  |  RCON Port: {_server.RconPort}";

        _ = ConnectAsync();
    }

    private async Task ConnectAsync()
    {
        try
        {
            AppendOutput("Connecting to RCON...");
            _rcon = new RconClient(10000);
            await _rcon.ConnectAsync("127.0.0.1", _server.RconPort, _server.ServerInfo.AdminPassword);
            _isConnected = true;
            AppendOutput("✅ Connected successfully!\n");
        }
        catch (Exception ex)
        {
            AppendOutput($"❌ Connection failed: {ex.Message}\n");
            _isConnected = false;
        }
    }

    private async void SendCommand_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteCommandAsync();
    }

    private async void CommandTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await ExecuteCommandAsync();
        }
    }

    private async Task ExecuteCommandAsync()
    {
        var command = CommandTextBox.Text.Trim();
        if (string.IsNullOrEmpty(command))
            return;

        if (!_isConnected || _rcon == null)
        {
            AppendOutput("❌ Not connected to RCON. Reconnecting...\n");
            await ConnectAsync();
            return;
        }

        AppendOutput($"> {command}");

        try
        {
            var response = await _rcon.SendCommandAsync(command);
            AppendOutput(string.IsNullOrWhiteSpace(response) ? "(No response)" : response);
            AppendOutput(""); // Blank line
        }
        catch (Exception ex)
        {
            AppendOutput($"❌ Error: {ex.Message}\n");
        }

        CommandTextBox.Clear();
    }

    private async void QuickCommand_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is string command)
        {
            CommandTextBox.Text = command;
            await ExecuteCommandAsync();
        }
    }

    private void QuickBroadcast_Click(object sender, RoutedEventArgs e)
    {
        var inputWindow = new Window
        {
            Title = "Broadcast to Server",
            Width = 400,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this
        };

        var stackPanel = new System.Windows.Controls.StackPanel { Margin = new Thickness(10) };
        var textBox = new System.Windows.Controls.TextBox { Height = 30, Margin = new Thickness(0, 10, 0, 10) };
        var button = new System.Windows.Controls.Button { Content = "Send", Height = 30, Width = 80, HorizontalAlignment = HorizontalAlignment.Right };

        button.Click += (s, args) =>
        {
            if (!string.IsNullOrWhiteSpace(textBox.Text))
            {
                CommandTextBox.Text = $"serverchat {textBox.Text}";
                _ = ExecuteCommandAsync();
            }
            inputWindow.Close();
        };

        stackPanel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Enter broadcast message:" });
        stackPanel.Children.Add(textBox);
        stackPanel.Children.Add(button);
        inputWindow.Content = stackPanel;

        inputWindow.ShowDialog();
    }

    private void AppendOutput(string text)
    {
        OutputTextBox.Text += text + Environment.NewLine;
        OutputScrollViewer.ScrollToBottom();
    }

    protected override void OnClosed(EventArgs e)
    {
        _rcon?.Dispose();
        base.OnClosed(e);
    }
}
