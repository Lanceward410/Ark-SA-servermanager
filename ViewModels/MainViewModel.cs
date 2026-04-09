using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ArkAutomata.GUI.Services;

namespace ArkAutomata.GUI.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly SchedulerService _scheduler;
    private readonly AutomataConfig _config;
    private readonly AutomataLogger _log;
    private readonly System.Timers.Timer _statusUpdateTimer;

    private string _statusText = "Initializing...";
    private string _nextRestartText = "Calculating...";
    private bool _isCycleInProgress;
    private string _broadcastMessage = "";
    private bool _runUpdates = true;
    private string _selectedCountdownDuration = "5 minutes";
    private ServerViewModel? _selectedServer;

    public ObservableCollection<ServerViewModel> Servers { get; } = new();
    public ObservableCollection<string> BroadcastTemplates { get; } = new();
    public ObservableCollection<string> RecentLogs { get; } = new();
    public ObservableCollection<string> CountdownDurations { get; } = new()
    {
        "5 minutes",
        "4 minutes", 
        "3 minutes",
        "2 minutes",
        "1 minute",
        "30 seconds",
        "15 seconds"
    };

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public string NextRestartText
    {
        get => _nextRestartText;
        set => SetProperty(ref _nextRestartText, value);
    }

    public bool IsCycleInProgress
    {
        get => _isCycleInProgress;
        set => SetProperty(ref _isCycleInProgress, value);
    }

    public string BroadcastMessage
    {
        get => _broadcastMessage;
        set => SetProperty(ref _broadcastMessage, value);
    }

    public bool RunUpdates
    {
        get => _runUpdates;
        set => SetProperty(ref _runUpdates, value);
    }

    public string SelectedCountdownDuration
    {
        get => _selectedCountdownDuration;
        set => SetProperty(ref _selectedCountdownDuration, value);
    }

    public ServerViewModel? SelectedServer
    {
        get => _selectedServer;
        set => SetProperty(ref _selectedServer, value);
    }

    public ICommand StartAllServersCommand { get; }
    public ICommand ManualRestartCommand { get; }
    public ICommand ManualShutdownCommand { get; }
    public ICommand StartRestartServerCommand { get; }
    public ICommand StopServerCommand { get; }
    public ICommand BroadcastCommand { get; }
    public ICommand OpenRconConsoleCommand { get; }

    public MainViewModel(SchedulerService scheduler, List<ServerInfo> servers, AutomataConfig config, AutomataLogger log)
    {
        _scheduler = scheduler;
        _config = config;
        _log = log;

        // Initialize server view models
        foreach (var server in servers)
        {
            Servers.Add(new ServerViewModel { ServerInfo = server });
        }

        // Initialize broadcast templates
        foreach (var template in config.BroadcastTemplates)
        {
            BroadcastTemplates.Add(template);
        }

        // Commands
        StartAllServersCommand = new RelayCommand(async () => await OnStartAllServersAsync(), () => !IsCycleInProgress);
        ManualRestartCommand = new RelayCommand(async () => await OnManualRestartAsync(), () => !IsCycleInProgress);
        ManualShutdownCommand = new RelayCommand(async () => await OnManualShutdownAsync(), () => !IsCycleInProgress);
        StartRestartServerCommand = new RelayCommand<ServerViewModel>(async s => await OnStartRestartServerAsync(s), s => s != null && !IsCycleInProgress);
        StopServerCommand = new RelayCommand<ServerViewModel>(async s => await OnStopServerAsync(s), s => s != null && !IsCycleInProgress);
        BroadcastCommand = new RelayCommand(async () => await OnBroadcastAsync(), () => !string.IsNullOrWhiteSpace(BroadcastMessage));
        OpenRconConsoleCommand = new RelayCommand<ServerViewModel>(OnOpenRconConsole, s => s != null);

        // Subscribe to scheduler events
        _scheduler.NextRestartChanged += OnNextRestartChanged;
        _scheduler.StatusChanged += OnStatusChanged;
        _scheduler.CycleStarted += OnCycleStarted;
        _scheduler.CycleCompleted += OnCycleCompleted;

        // Start status update timer (every 5 seconds)
        _statusUpdateTimer = new System.Timers.Timer(5_000);
        _statusUpdateTimer.Elapsed += async (s, e) => await UpdateServerStatusesAsync();
        _statusUpdateTimer.AutoReset = true;
        _statusUpdateTimer.Start();
    }

    public async Task InitializeAsync()
    {
        await UpdateServerStatusesAsync();
        _scheduler.Start();
    }

    private async Task UpdateServerStatusesAsync()
    {
        foreach (var server in Servers)
        {
            await server.UpdateStatusAsync();
        }
        OnPropertyChanged(nameof(Servers));
    }

    private void OnNextRestartChanged(DateTime nextRestart)
    {
        var timeUntil = nextRestart - DateTime.Now;
        if (timeUntil.TotalHours >= 1)
            NextRestartText = $"Next restart: {nextRestart:hh:mm tt} EST ({timeUntil.Hours}h {timeUntil.Minutes}m)";
        else
            NextRestartText = $"Next restart: {nextRestart:hh:mm tt} EST ({timeUntil.Minutes} minutes)";
    }

    private void OnStatusChanged(string status)
    {
        StatusText = status;
    }

    private void OnCycleStarted()
    {
        IsCycleInProgress = true;
    }

    private void OnCycleCompleted(bool success)
    {
        IsCycleInProgress = false;
        _ = UpdateServerStatusesAsync(); // Refresh server statuses
    }

    private async Task OnStartAllServersAsync()
    {
        // Refresh server statuses first
        await UpdateServerStatusesAsync();
        
        var offlineServers = Servers.Where(s => !s.IsOnline).ToList();
        
        if (offlineServers.Count == 0)
        {
            System.Windows.MessageBox.Show(
                "All servers are already running.",
                "Start All Servers",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }

        var result = System.Windows.MessageBox.Show(
            $"Start {offlineServers.Count} offline server(s)?\n\n{string.Join("\n", offlineServers.Select(s => $"  • {s.Name}"))}",
            "Confirm Start All",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);

        if (result == System.Windows.MessageBoxResult.Yes)
        {
            IsCycleInProgress = true;
            StatusText = $"Starting {offlineServers.Count} server(s)...";
            _log.Info($"Starting all offline servers ({offlineServers.Count} total)...");

            try
            {
                var orchestrator = new Orchestrator(_config, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _config.ASCTConfigRelativePath), 
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _config.DepotDownloaderRelativePath), _log);
                
                await orchestrator.StartAllOfflineServersAsync(offlineServers.Select(s => s.ServerInfo).ToList());
                
                // Refresh all server statuses
                await Task.Delay(2000); // Give servers a moment to start
                await UpdateServerStatusesAsync();
                
                StatusText = $"Successfully started {offlineServers.Count} server(s)";
                _log.Info("Start all servers completed successfully");
            }
            catch (Exception ex)
            {
                _log.Error($"Start all servers failed: {ex.Message}");
                StatusText = "Failed to start some servers";
                System.Windows.MessageBox.Show(
                    $"Error starting servers:\n\n{ex.Message}",
                    "Start Failed",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
            finally
            {
                IsCycleInProgress = false;
            }
        }
    }

    private async Task OnManualRestartAsync()
    {
        var updateText = RunUpdates ? "and run server updates" : "(no updates)";
        var result = System.Windows.MessageBox.Show(
            $"Are you sure you want to restart all servers NOW?\n\nPlayers will receive {SelectedCountdownDuration} warning, then servers will restart {updateText}.",
            "Confirm Manual Restart",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (result == System.Windows.MessageBoxResult.Yes)
        {
            int countdownSeconds = GetCountdownSeconds(SelectedCountdownDuration);
            await _scheduler.RunManualCycleAsync(RunUpdates, countdownSeconds);
        }
    }

    private async Task OnManualShutdownAsync()
    {
        var result = System.Windows.MessageBox.Show(
            $"Are you sure you want to SHUT DOWN all servers?\n\nPlayers will receive {SelectedCountdownDuration} warning before shutdown (no restart or update).",
            "Confirm Manual Shutdown",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (result == System.Windows.MessageBoxResult.Yes)
        {
            int countdownSeconds = GetCountdownSeconds(SelectedCountdownDuration);
            await _scheduler.RunManualShutdownAsync(countdownSeconds);
        }
    }

    private async Task OnStartRestartServerAsync(ServerViewModel? server)
    {
        if (server == null) return;

        if (server.IsOnline)
        {
            // Restart
            var result = System.Windows.MessageBox.Show(
                $"Restart {server.Name}?\n\nPlayers will receive {SelectedCountdownDuration} warning before shutdown.",
                "Confirm Restart",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question);

            if (result == System.Windows.MessageBoxResult.Yes)
            {
                await RestartSingleServerAsync(server);
            }
        }
        else
        {
            // Start (no countdown needed)
            await StartSingleServerAsync(server);
        }
    }

    private async Task OnStopServerAsync(ServerViewModel? server)
    {
        if (server == null) return;

        var result = System.Windows.MessageBox.Show(
            $"Stop {server.Name}?\n\nPlayers will receive {SelectedCountdownDuration} warning before shutdown.",
            "Confirm Stop",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (result == System.Windows.MessageBoxResult.Yes)
        {
            await StopSingleServerAsync(server);
        }
    }

    private async Task StartSingleServerAsync(ServerViewModel server)
    {
        _log.Info($"[{server.Name}] Starting server...");
        StatusText = $"Starting {server.Name}...";
        
        try
        {
            var orchestrator = new Orchestrator(_config, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _config.ASCTConfigRelativePath), 
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _config.DepotDownloaderRelativePath), _log);
            await orchestrator.StartSingleServerAsync(server.ServerInfo);
            
            await Task.Delay(2000);
            await server.UpdateStatusAsync();
            StatusText = $"{server.Name} started successfully";
        }
        catch (Exception ex)
        {
            _log.Error($"[{server.Name}] Failed to start: {ex.Message}");
            StatusText = $"Failed to start {server.Name}";
        }
    }

    private async Task RestartSingleServerAsync(ServerViewModel server)
    {
        _log.Info($"[{server.Name}] Restarting server...");
        StatusText = $"Restarting {server.Name}...";
        IsCycleInProgress = true;

        try
        {
            var orchestrator = new Orchestrator(_config, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _config.ASCTConfigRelativePath), 
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _config.DepotDownloaderRelativePath), _log);
            int countdownSeconds = GetCountdownSeconds(SelectedCountdownDuration);
            await orchestrator.RestartSingleServerAsync(server.ServerInfo, RunUpdates, countdownSeconds);
            
            await server.UpdateStatusAsync();
            StatusText = $"{server.Name} restarted successfully";
        }
        catch (Exception ex)
        {
            _log.Error($"[{server.Name}] Failed to restart: {ex.Message}");
            StatusText = $"Failed to restart {server.Name}";
        }
        finally
        {
            IsCycleInProgress = false;
        }
    }

    private async Task StopSingleServerAsync(ServerViewModel server)
    {
        _log.Info($"[{server.Name}] Stopping server...");
        StatusText = $"Stopping {server.Name}...";
        IsCycleInProgress = true;

        try
        {
            var orchestrator = new Orchestrator(_config, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _config.ASCTConfigRelativePath), 
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _config.DepotDownloaderRelativePath), _log);
            int countdownSeconds = GetCountdownSeconds(SelectedCountdownDuration);
            await orchestrator.StopSingleServerAsync(server.ServerInfo, countdownSeconds);
            
            await server.UpdateStatusAsync();
            StatusText = $"{server.Name} stopped successfully";
        }
        catch (Exception ex)
        {
            _log.Error($"[{server.Name}] Failed to stop: {ex.Message}");
            StatusText = $"Failed to stop {server.Name}";
        }
        finally
        {
            IsCycleInProgress = false;
        }
    }

    private int GetCountdownSeconds(string duration)
    {
        return duration switch
        {
            "5 minutes" => 300,
            "4 minutes" => 240,
            "3 minutes" => 180,
            "2 minutes" => 120,
            "1 minute" => 60,
            "30 seconds" => 30,
            "15 seconds" => 15,
            _ => 300 // Default to 5 minutes
        };
    }

    private async Task OnBroadcastAsync()
    {
        foreach (var server in Servers.Where(s => s.IsOnline))
        {
            await SendBroadcastToServer(server, BroadcastMessage);
            await Task.Delay(_config.BroadcastStaggerSeconds * 1000);
        }
        System.Windows.MessageBox.Show($"Message broadcasted to all online servers.", "Broadcast Sent", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        BroadcastMessage = ""; // Clear after send
    }

    private async Task SendBroadcastToServer(ServerViewModel server, string message)
    {
        try
        {
            using var rcon = new RconClient(10000);
            await rcon.ConnectAsync("127.0.0.1", server.RconPort, server.ServerInfo.AdminPassword);
            await rcon.SendCommandAsync($"serverchat {message}");
            _log.Info($"[{server.Name}] Broadcast: \"{message}\"");
        }
        catch (Exception ex)
        {
            _log.Warning($"[{server.Name}] Broadcast failed: {ex.Message}");
        }
    }

    private void OnOpenRconConsole(ServerViewModel? server)
    {
        if (server == null) return;

        var rconWindow = new Views.RconConsoleWindow(server);
        rconWindow.Show();
    }
}

// Simple relay command implementation
public class RelayCommand : ICommand
{
    private readonly Func<Task>? _executeAsync;
    private readonly Action? _execute;
    private readonly Func<bool>? _canExecute;

    public event EventHandler? CanExecuteChanged;

    public RelayCommand(Func<Task> executeAsync, Func<bool>? canExecute = null)
    {
        _executeAsync = executeAsync;
        _canExecute = canExecute;
    }

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    public async void Execute(object? parameter)
    {
        if (_executeAsync != null)
            await _executeAsync();
        else
            _execute?.Invoke();
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public class RelayCommand<T> : ICommand
{
    private readonly Func<T?, Task>? _executeAsync;
    private readonly Action<T?>? _execute;
    private readonly Func<T?, bool>? _canExecute;

    public event EventHandler? CanExecuteChanged;

    public RelayCommand(Func<T?, Task> executeAsync, Func<T?, bool>? canExecute = null)
    {
        _executeAsync = executeAsync;
        _canExecute = canExecute;
    }

    public RelayCommand(Action<T?> execute, Func<T?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke((T?)parameter) ?? true;

    public async void Execute(object? parameter)
    {
        if (_executeAsync != null)
            await _executeAsync((T?)parameter);
        else
            _execute?.Invoke((T?)parameter);
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
