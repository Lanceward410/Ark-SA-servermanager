using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using ArkAutomata.GUI.Services;
using ArkAutomata.GUI.ViewModels;
using Hardcodet.Wpf.TaskbarNotification;
using Newtonsoft.Json;

namespace ArkAutomata.GUI;

public partial class App : Application
{
    private MainWindow? _mainWindow;
    private TaskbarIcon? _notifyIcon;
    private MainViewModel? _viewModel;
    private SchedulerService? _scheduler;
    private PeriodicMessageService? _periodicMessageService;
    private readonly Mutex _mutex = new(true, @"Global\ArkAutomata_GUI_SingleInstance");

    private async void Application_Startup(object sender, StartupEventArgs e)
    {
        if (!_mutex.WaitOne(TimeSpan.Zero, true))
        {
            MessageBox.Show("ARK Automata is already running.", "Already Running", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        try
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var configPath = Path.Combine(baseDir, "config.json");
            var asctConfigPath = Path.Combine(baseDir, "..", "ASCTGlobalConfig.json");
            var depotDownloaderPath = Path.Combine(baseDir, "..", "depotdownloader", "DepotDownloader.exe");

            if (!File.Exists(configPath))
            {
                MessageBox.Show($"config.json not found at: {configPath}", "Configuration Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
                return;
            }

            var config = AutomataConfig.Load(configPath);
            asctConfigPath = Path.GetFullPath(Path.Combine(baseDir, config.ASCTConfigRelativePath));
            depotDownloaderPath = Path.GetFullPath(Path.Combine(baseDir, config.DepotDownloaderRelativePath));

            var logger = new AutomataLogger(Path.Combine(baseDir, "autologs"));
            logger.Info("════════════════════════════════════════════════════");
            logger.Info("  ARK Automata GUI Starting");
            logger.Info("════════════════════════════════════════════════════");

            var asctJson = File.ReadAllText(asctConfigPath);
            var asctConfig = JsonConvert.DeserializeObject<ASCTGlobalConfig>(asctJson);
            var servers = LoadServerInfos(asctConfig!, logger);

            if (servers.Count == 0)
            {
                MessageBox.Show("No servers found in ASCT configuration.", "Configuration Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
                return;
            }

            var orchestrator = new Orchestrator(config, asctConfigPath, depotDownloaderPath, logger);
            _scheduler = new SchedulerService(orchestrator, config, logger);

            _periodicMessageService = new PeriodicMessageService(orchestrator, config, logger, _scheduler);

            _viewModel = new MainViewModel(_scheduler, servers, config, logger);

            _notifyIcon = (TaskbarIcon)FindResource("NotifyIcon");
            _notifyIcon.DataContext = this;

            _mainWindow = new MainWindow(_viewModel);
            MainWindow = _mainWindow;

            bool startMinimized = e.Args.Contains("--minimize");

            if (startMinimized)
            {
                _mainWindow.WindowState = WindowState.Minimized;
                _mainWindow.ShowInTaskbar = false;
                _notifyIcon.ShowBalloonTip("ARK Automata", "Running in background", BalloonIcon.Info);
            }
            else
            {
                _mainWindow.Show();
            }

            await _viewModel.InitializeAsync();

            _periodicMessageService?.Start();

            logger.Info("GUI initialized successfully");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Fatal error during startup:\n\n{ex.Message}\n\nStack trace:\n{ex.StackTrace}", "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            throw;
        }
    }

    private void Application_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show($"Unhandled exception:\n\n{e.Exception.Message}\n\n{e.Exception.StackTrace}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private static List<ServerInfo> LoadServerInfos(ASCTGlobalConfig asctConfig, AutomataLogger logger)
    {
        var servers = new List<ServerInfo>();

        foreach (var sc in asctConfig.Servers)
        {
            var (rconPort, adminPass) = ReadServerIniSettings(sc.GameDirectory);

            if (rconPort == 0)
            {
                logger.Warning($"[{sc.Name}] Could not read RCONPort - skipping");
                continue;
            }

            servers.Add(new ServerInfo
            {
                Config = sc,
                RconPort = rconPort,
                AdminPassword = adminPass
            });
        }

        return servers;
    }

    private static (int rconPort, string adminPassword) ReadServerIniSettings(string gameDirectory)
    {
        var iniPath = Path.Combine(gameDirectory, "ShooterGame", "Saved", "Config", "WindowsServer", "GameUserSettings.ini");

        if (!File.Exists(iniPath))
            return (0, "");

        int rconPort = 0;
        string adminPassword = "";

        foreach (var rawLine in File.ReadLines(iniPath))
        {
            var line = rawLine.Trim();

            if (line.StartsWith("RCONPort=", StringComparison.OrdinalIgnoreCase))
                int.TryParse(line.AsSpan("RCONPort=".Length), out rconPort);
            else if (line.StartsWith("ServerAdminPassword=", StringComparison.OrdinalIgnoreCase))
                adminPassword = line["ServerAdminPassword=".Length..];
        }

        return (rconPort, adminPassword);
    }

    private void ShowWindow_Click(object sender, RoutedEventArgs e)
    {
        ShowMainWindow();
    }

    private async void UpdateNow_Click(object sender, RoutedEventArgs e)
    {
        if (_scheduler != null && _viewModel != null)
        {
            var result = MessageBox.Show(
                "Run manual update cycle now?",
                "Confirm Update",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                await _scheduler.RunManualCycleAsync();
                _notifyIcon?.ShowBalloonTip("Update Complete", "Manual update cycle finished", BalloonIcon.Info);
            }
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Shutdown();
    }

    private void ShowMainWindow()
    {
        if (_mainWindow != null)
        {
            _mainWindow.Show();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.ShowInTaskbar = true;
            _mainWindow.Activate();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _periodicMessageService?.Dispose();
        _scheduler?.Stop();
        _notifyIcon?.Dispose();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
