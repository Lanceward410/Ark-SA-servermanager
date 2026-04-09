using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;

namespace ArkAutomata.GUI.Services;

/// <summary>
/// Manages scheduled server restarts at fixed clock times (e.g., 12:00 PM, 2:00 PM EST).
/// </summary>
public sealed class SchedulerService : IDisposable
{
    private readonly Orchestrator _orchestrator;
    private readonly AutomataConfig _config;
    private readonly AutomataLogger _log;
    private System.Timers.Timer? _timer;
    private DateTime _nextScheduledRestart;
    private bool _isRunningCycle;

    public event Action<DateTime>? NextRestartChanged;
    public event Action<string>? StatusChanged;
    public event Action? CycleStarted;
    public event Action<bool>? CycleCompleted; // bool = success

    public DateTime NextScheduledRestart => _nextScheduledRestart;
    public bool IsRunning => _timer?.Enabled ?? false;
    public bool IsCycleInProgress => _isRunningCycle;

    public SchedulerService(Orchestrator orchestrator, AutomataConfig config, AutomataLogger log)
    {
        _orchestrator = orchestrator;
        _config = config;
        _log = log;
        _nextScheduledRestart = DateTime.MinValue;
    }

    public void Start()
    {
        if (_timer != null)
            return; // Already started

        _log.Info("Scheduler service starting...");

        // Calculate first scheduled restart
        _nextScheduledRestart = CalculateNextScheduledRestart();
        _log.Info($"Next scheduled restart: {_nextScheduledRestart:yyyy-MM-dd HH:mm:ss} EST");
        NextRestartChanged?.Invoke(_nextScheduledRestart);
        StatusChanged?.Invoke($"Idle - next restart at {_nextScheduledRestart:HH:mm} EST");

        // Check every 30 seconds
        _timer = new System.Timers.Timer(30_000);
        _timer.Elapsed += OnTimerElapsed;
        _timer.AutoReset = true;
        _timer.Start();
    }

    public void Stop()
    {
        _timer?.Stop();
        _timer?.Dispose();
        _timer = null;
        _log.Info("Scheduler service stopped.");
        StatusChanged?.Invoke("Stopped");
    }

    private async void OnTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        if (_isRunningCycle)
            return; // Cycle already in progress

        var estZone = TimeZoneInfo.FindSystemTimeZoneById(_config.TimeZone);
        var nowEst = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, estZone);

        if (nowEst >= _nextScheduledRestart)
        {
            await RunScheduledCycleAsync();
        }
    }

    private async Task RunScheduledCycleAsync()
    {
        _isRunningCycle = true;
        CycleStarted?.Invoke();
        StatusChanged?.Invoke("Running scheduled update cycle...");
        _log.Info("═══════════════════════════════════════════════════════");
        _log.Info("  SCHEDULED UPDATE CYCLE STARTED");
        _log.Info("═══════════════════════════════════════════════════════");

        bool success = await _orchestrator.RunUpdateCycleAsync();

        _isRunningCycle = false;
        CycleCompleted?.Invoke(success);

        // Calculate next restart
        _nextScheduledRestart = CalculateNextScheduledRestart();
        _log.Info($"Next scheduled restart: {_nextScheduledRestart:yyyy-MM-dd HH:mm:ss} EST");
        NextRestartChanged?.Invoke(_nextScheduledRestart);
        StatusChanged?.Invoke($"Idle - next restart at {_nextScheduledRestart:HH:mm} EST");
    }

    public async Task<bool> RunManualCycleAsync(bool runUpdates = true, int countdownSeconds = 300)
    {
        if (_isRunningCycle)
        {
            _log.Warning("Cannot start manual cycle - another cycle is already in progress.");
            return false;
        }

        _isRunningCycle = true;
        CycleStarted?.Invoke();
        var updateText = runUpdates ? "with updates" : "without updates";
        StatusChanged?.Invoke($"Running MANUAL restart cycle ({updateText})...");
        _log.Info("═══════════════════════════════════════════════════════");
        _log.Info($"  MANUAL UPDATE CYCLE STARTED ({updateText.ToUpper()})");
        _log.Info("═══════════════════════════════════════════════════════");

        bool success = await _orchestrator.RunUpdateCycleAsync(runUpdates, countdownSeconds);

        _isRunningCycle = false;
        CycleCompleted?.Invoke(success);

        // Next scheduled restart is UNCHANGED
        StatusChanged?.Invoke($"Manual cycle complete - next scheduled restart: {_nextScheduledRestart:HH:mm} EST");
        return success;
    }

    public async Task<bool> RunManualShutdownAsync(int countdownSeconds = 300)
    {
        if (_isRunningCycle)
        {
            _log.Warning("Cannot start manual shutdown - another cycle is already in progress.");
            return false;
        }

        _isRunningCycle = true;
        CycleStarted?.Invoke();
        StatusChanged?.Invoke("Running MANUAL shutdown (no restart)...");
        _log.Info("═══════════════════════════════════════════════════════");
        _log.Info("  MANUAL SHUTDOWN STARTED (NO RESTART)");
        _log.Info("═══════════════════════════════════════════════════════");

        bool success = await _orchestrator.ShutdownServersOnlyAsync(countdownSeconds);

        _isRunningCycle = false;
        CycleCompleted?.Invoke(success);

        // Next scheduled restart is UNCHANGED
        StatusChanged?.Invoke($"Manual shutdown complete - next scheduled restart: {_nextScheduledRestart:HH:mm} EST");
        return success;
    }

    private DateTime CalculateNextScheduledRestart()
    {
        var estZone = TimeZoneInfo.FindSystemTimeZoneById(_config.TimeZone);
        var nowEst = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, estZone);

        // Get restart hours from config
        var restartHours = _config.FixedScheduleHoursEST.OrderBy(h => h).ToList();

        // Find next restart hour
        int currentHour = nowEst.Hour;
        var nextHours = restartHours.Where(h => h > currentHour).ToList();
        
        if (nextHours.Count == 0)
        {
            // No more restarts today - use first hour tomorrow
            int nextHour = restartHours.First();
            var tomorrow = nowEst.Date.AddDays(1);
            return new DateTime(tomorrow.Year, tomorrow.Month, tomorrow.Day, nextHour, 0, 0, DateTimeKind.Unspecified);
        }

        // Next restart is today
        return new DateTime(nowEst.Year, nowEst.Month, nowEst.Day, nextHours[0], 0, 0, DateTimeKind.Unspecified);
    }

    public void Dispose()
    {
        Stop();
    }
}
