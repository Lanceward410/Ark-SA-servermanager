using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;

namespace ArkAutomata.GUI.Services;

// Tips on a timer. Stays quiet in the window around a scheduled restart.
public sealed class PeriodicMessageService : IDisposable
{
    private readonly Orchestrator _orchestrator;
    private readonly AutomataConfig _config;
    private readonly AutomataLogger _log;
    private readonly SchedulerService _scheduler;
    private System.Timers.Timer? _timer;
    private List<string> _shuffledMessages;
    private int _currentIndex;
    private bool _isPaused;
    private DateTime? _pausedUntil;

    public event Action<string>? MessageSent;

    public PeriodicMessageService(
        Orchestrator orchestrator,
        AutomataConfig config,
        AutomataLogger log,
        SchedulerService scheduler)
    {
        _orchestrator = orchestrator;
        _config = config;
        _log = log;
        _scheduler = scheduler;
        _shuffledMessages = new List<string>();
        _currentIndex = 0;
        _isPaused = false;
    }

    public void Start()
    {
        if (!_config.PeriodicMessages.Enabled)
        {
            _log.Info("Periodic messages are disabled in config.");
            return;
        }

        if (_config.PeriodicMessages.Messages.Count == 0)
        {
            _log.Warning("No periodic messages configured - service will not start.");
            return;
        }

        _log.Info("Periodic message service starting...");
        _log.Info($"Interval: {_config.PeriodicMessages.IntervalMinutes} minutes");
        _log.Info($"Message count: {_config.PeriodicMessages.Messages.Count}");
        _log.Info($"Randomize order: {_config.PeriodicMessages.RandomizeOrder}");

        ShuffleMessages();

        _scheduler.CycleStarted += OnRestartCycleStarted;
        _scheduler.CycleCompleted += OnRestartCycleCompleted;

        int initialDelayMs = _config.PeriodicMessages.ResumeAfterRestartMinutes * 60 * 1000;
        _log.Info($"First message will be sent in {_config.PeriodicMessages.ResumeAfterRestartMinutes} minutes.");

        _timer = new System.Timers.Timer(initialDelayMs);
        _timer.Elapsed += OnTimerElapsed;
        _timer.AutoReset = false; // next delay is not always the same interval
        _timer.Start();
    }

    public void Stop()
    {
        _timer?.Stop();
        _timer?.Dispose();
        _timer = null;
        _log.Info("Periodic message service stopped.");
    }

    private void OnRestartCycleStarted()
    {
        _isPaused = true;
        _timer?.Stop();
        _log.Info("[PeriodicMessages] Paused during restart cycle.");
    }

    private void OnRestartCycleCompleted(bool success)
    {
        if (_config.PeriodicMessages.RandomizeOrder)
        {
            ShuffleMessages();
            _log.Info("[PeriodicMessages] Messages reshuffled after restart.");
        }

        int resumeDelayMs = _config.PeriodicMessages.ResumeAfterRestartMinutes * 60 * 1000;
        _pausedUntil = DateTime.Now.AddMilliseconds(resumeDelayMs);

        _log.Info($"[PeriodicMessages] Will resume in {_config.PeriodicMessages.ResumeAfterRestartMinutes} minutes.");

        _isPaused = false;
        _timer?.Stop();
        _timer = new System.Timers.Timer(resumeDelayMs);
        _timer.Elapsed += OnTimerElapsed;
        _timer.AutoReset = false;
        _timer.Start();
    }

    private async void OnTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        try
        {
            if (_isPaused)
            {
                _log.Info("[PeriodicMessages] Skipped (service paused).");
                return;
            }

            if (IsTooCloseToRestart())
            {
                _log.Info("[PeriodicMessages] Skipped (too close to scheduled restart).");

                RescheduleAfterRestartWindow();
                return;
            }

            await SendNextMessageAsync();

            int intervalMs = _config.PeriodicMessages.IntervalMinutes * 60 * 1000;
            _timer = new System.Timers.Timer(intervalMs);
            _timer.Elapsed += OnTimerElapsed;
            _timer.AutoReset = false;
            _timer.Start();
        }
        catch (Exception ex)
        {
            _log.Error($"[PeriodicMessages] Error: {ex.Message}");
            
            _timer = new System.Timers.Timer(5 * 60 * 1000);
            _timer.Elapsed += OnTimerElapsed;
            _timer.AutoReset = false;
            _timer.Start();
        }
    }

    private bool IsTooCloseToRestart()
    {
        if (!_config.UseFixedSchedule)
            return false;

        var estZone = TimeZoneInfo.FindSystemTimeZoneById(_config.TimeZone);
        var nowEst = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, estZone);

        foreach (var restartHour in _config.FixedScheduleHoursEST)
        {
            var nextRestart = new DateTime(nowEst.Year, nowEst.Month, nowEst.Day, restartHour, 0, 0);
            
            if (nextRestart < nowEst)
                continue;

            var minutesUntilRestart = (nextRestart - nowEst).TotalMinutes;
            
            if (minutesUntilRestart <= _config.PeriodicMessages.PauseBeforeRestartMinutes)
            {
                _log.Info($"[PeriodicMessages] Within {_config.PeriodicMessages.PauseBeforeRestartMinutes}-minute window before {restartHour:D2}:00 restart.");
                return true;
            }

            // Assumes the previous slot was 6 hours earlier. Wrong if the schedule is tighter than that.
            var minutesSinceRestart = (nowEst - nextRestart.AddHours(-6)).TotalMinutes;
            if (minutesSinceRestart >= 0 && minutesSinceRestart <= _config.PeriodicMessages.ResumeAfterRestartMinutes)
            {
                return true;
            }
        }

        return false;
    }

    private void RescheduleAfterRestartWindow()
    {
        var estZone = TimeZoneInfo.FindSystemTimeZoneById(_config.TimeZone);
        var nowEst = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, estZone);

        var nextRestartHour = _config.FixedScheduleHoursEST
            .Select(h => new DateTime(nowEst.Year, nowEst.Month, nowEst.Day, h, 0, 0))
            .Where(dt => dt > nowEst)
            .OrderBy(dt => dt)
            .FirstOrDefault();

        if (nextRestartHour == default)
        {
            nextRestartHour = new DateTime(nowEst.Year, nowEst.Month, nowEst.Day, _config.FixedScheduleHoursEST.First(), 0, 0).AddDays(1);
        }

        var safeTime = nextRestartHour.AddMinutes(_config.PeriodicMessages.ResumeAfterRestartMinutes);
        var delay = safeTime - nowEst;
        
        if (delay.TotalMilliseconds > 0)
        {
            _log.Info($"[PeriodicMessages] Rescheduling to {safeTime:HH:mm} EST (after restart window).");
            _timer = new System.Timers.Timer(delay.TotalMilliseconds);
            _timer.Elapsed += OnTimerElapsed;
            _timer.AutoReset = false;
            _timer.Start();
        }
    }

    private async Task SendNextMessageAsync()
    {
        if (_shuffledMessages.Count == 0)
        {
            _log.Warning("[PeriodicMessages] No messages to send.");
            return;
        }

        string message = _shuffledMessages[_currentIndex];
        _log.Info($"[PeriodicMessages] Broadcasting tip {_currentIndex + 1}/{_shuffledMessages.Count}: {message.Substring(0, Math.Min(50, message.Length))}...");

        await _orchestrator.BroadcastToAllServersAsync(message);

        MessageSent?.Invoke(message);

        _currentIndex = (_currentIndex + 1) % _shuffledMessages.Count;

        if (_currentIndex == 0 && _config.PeriodicMessages.RandomizeOrder)
        {
            _log.Info("[PeriodicMessages] Completed full cycle - reshuffling messages.");
            ShuffleMessages();
        }
    }

    private void ShuffleMessages()
    {
        _shuffledMessages = _config.PeriodicMessages.Messages.ToList();
        
        if (_config.PeriodicMessages.RandomizeOrder)
        {
            var rng = new Random();
            _shuffledMessages = _shuffledMessages.OrderBy(_ => rng.Next()).ToList();
            _log.Info("[PeriodicMessages] Messages shuffled.");
        }

        _currentIndex = 0;
    }

    public void Dispose()
    {
        Stop();
        if (_scheduler != null)
        {
            _scheduler.CycleStarted -= OnRestartCycleStarted;
            _scheduler.CycleCompleted -= OnRestartCycleCompleted;
        }
    }
}
