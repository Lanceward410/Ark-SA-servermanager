using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ArkAutomata.GUI.ViewModels;

public sealed class ServerViewModel : ViewModelBase
{
    private bool _isOnline;
    private int _playerCount;
    private string _status = "Unknown";

    public required ServerInfo ServerInfo { get; init; }

    public string Name => ServerInfo.Config.Name;
    public int GamePort => ServerInfo.Config.GamePort;
    public int RconPort => ServerInfo.RconPort;
    public string Map => ServerInfo.Config.Map;

    public bool IsOnline
    {
        get => _isOnline;
        set
        {
            if (SetProperty(ref _isOnline, value))
            {
                OnPropertyChanged(nameof(StatusIcon));
                OnPropertyChanged(nameof(StartRestartButtonText));
            }
        }
    }

    public int PlayerCount
    {
        get => _playerCount;
        set => SetProperty(ref _playerCount, value);
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public string StatusIcon => IsOnline ? "🟢" : "🔴";
    public string PlayerCountText => $"{PlayerCount}/{ServerInfo.Config.Slots}";
    public string StartRestartButtonText => IsOnline ? "🔄 Restart" : "▶ Start";

    public async Task UpdateStatusAsync()
    {
        var process = FindServerProcess();
        IsOnline = process != null;

        if (!IsOnline)
        {
            Status = "Offline";
            PlayerCount = 0;
            return;
        }

        try
        {
            using var rcon = new RconClient(10000);
            await rcon.ConnectAsync("127.0.0.1", RconPort, ServerInfo.AdminPassword);
            var response = await rcon.SendCommandAsync("listplayers");
            PlayerCount = ParsePlayerCount(response);
            Status = "Online";
        }
        catch
        {
            Status = "Online (RCON unavailable)";
            PlayerCount = 0;
        }
    }

    private Process? FindServerProcess()
    {
        var targetExe = Path.GetFullPath(ServerInfo.ServerExePath);
        try
        {
            foreach (var proc in Process.GetProcessesByName("ArkAscendedServer"))
            {
                try
                {
                    if (string.Equals(proc.MainModule?.FileName, targetExe, StringComparison.OrdinalIgnoreCase))
                        return proc;
                }
                catch { }
            }
        }
        catch { }
        return null;
    }

    private static int ParsePlayerCount(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return 0;

        // listplayers lines start with the player index.
        var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return lines.Count(line =>
        {
            var trimmed = line.Trim();
            return trimmed.Length > 0 && char.IsDigit(trimmed[0]);
        });
    }
}
