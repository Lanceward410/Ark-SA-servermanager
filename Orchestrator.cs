using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace ArkAutomata;

// ─── Enriched server info (ASCT config + RCON details from .ini) ───────────

public sealed class ServerInfo
{
    public required ASCTServerConfig Config { get; init; }
    public int RconPort { get; init; }
    public string AdminPassword { get; init; } = "";

    public string ServerExePath =>
        Path.Combine(Config.GameDirectory, "ShooterGame", "Binaries", "Win64", "ArkAscendedServer.exe");
}

// ─── Main orchestrator ─────────────────────────────────────────────────────

public sealed class Orchestrator
{
    private readonly AutomataConfig _config;
    private readonly string _asctConfigPath;
    private readonly string _depotDownloaderPath;
    private readonly AutomataLogger _log;

    public Orchestrator(
        AutomataConfig config,
        string asctConfigPath,
        string depotDownloaderPath,
        AutomataLogger logger)
    {
        _config = config;
        _asctConfigPath = asctConfigPath;
        _depotDownloaderPath = depotDownloaderPath;
        _log = logger;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Top-level cycle
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Runs one complete update cycle: broadcast → kick → save → exit → update → start.
    /// Returns true if the entire cycle completed successfully.
    /// </summary>
    public async Task<bool> RunUpdateCycleAsync(bool runUpdates = true, int countdownSeconds = 300)
    {
        _log.Info("============================================================");
        _log.Info(runUpdates ? "  UPDATE CYCLE STARTED (WITH UPDATES)" : "  RESTART CYCLE STARTED (NO UPDATES)");
        _log.Info("============================================================");
        var cycleStart = DateTime.Now;

        try
        {
            // ── Load server information ────────────────────────────────
            var servers = LoadServerInfos();
            if (servers.Count == 0)
            {
                _log.Error("No servers found in ASCT configuration. Aborting cycle.");
                return false;
            }

            foreach (var s in servers)
                _log.Info($"  Server: {s.Config.Name}  |  Port {s.Config.GamePort}  |  RCON {s.RconPort}");

            // ── Determine which servers are currently running ──────────
            var runningServers = servers.Where(s => FindServerProcess(s) != null).ToList();
            _log.Info($"{runningServers.Count}/{servers.Count} server(s) currently running.");

            if (runningServers.Count > 0)
            {
                // Phase 1 — Countdown broadcasts (staggered per server)
                _log.Info("────────────────────────────────────────────────────────────");
                _log.Info("[Phase 1] Broadcasting restart warnings...");
                _log.Info("────────────────────────────────────────────────────────────");
                await Phase1_BroadcastCountdownAsync(runningServers, countdownSeconds, isRestart: true);

                // Phase 2 — Kick all players, save, exit (parallel across servers)
                _log.Info("────────────────────────────────────────────────────────────");
                _log.Info("[Phase 2] Shutdown sequence: kick → save → doexit");
                _log.Info("────────────────────────────────────────────────────────────");
                await Phase2_ShutdownServersAsync(runningServers);
            }
            else
            {
                _log.Info("No servers running — skipping Phase 1 and Phase 2.");
            }

            // Phase 3 — Confirm all processes dead + cooldown
            _log.Info("────────────────────────────────────────────────────────────");
            _log.Info("[Phase 3] Confirming all server processes have exited...");
            _log.Info("────────────────────────────────────────────────────────────");
            await Phase3_ConfirmProcessExitAsync(servers);

            _log.Info($"[Phase 3] Post-shutdown cooldown: {_config.PostShutdownCooldownSeconds}s...");
            await Task.Delay(_config.PostShutdownCooldownSeconds * 1000);

            // Phase 4 — Sequential DepotDownloader updates (optional)
            if (runUpdates)
            {
                _log.Info("────────────────────────────────────────────────────────────");
                _log.Info("[Phase 4] Updating servers via DepotDownloader (sequential)...");
                _log.Info("────────────────────────────────────────────────────────────");
                bool allUpdated = await Phase4_UpdateServersAsync(servers);

                if (!allUpdated)
                {
                    _log.Error("[Phase 4] One or more updates FAILED. Servers will NOT be restarted.");
                    _log.Error("Manual intervention required. Check logs above for details.");
                    return false;
                }
            }
            else
            {
                _log.Info("────────────────────────────────────────────────────────────");
                _log.Info("[Phase 4] SKIPPING updates (per user configuration)");
                _log.Info("────────────────────────────────────────────────────────────");
            }

            // Phase 5 — Start servers sequentially with stagger
            _log.Info("────────────────────────────────────────────────────────────");
            _log.Info("[Phase 5] Starting servers (staggered)...");
            _log.Info("────────────────────────────────────────────────────────────");
            await Phase5_StartServersAsync(servers);

            var elapsed = DateTime.Now - cycleStart;
            _log.Info("============================================================");
            _log.Info($"  {(runUpdates ? "UPDATE" : "RESTART")} CYCLE COMPLETED — {FormatElapsedTime(elapsed)}");
            _log.Info("============================================================");
            return true;
        }
        catch (Exception ex)
        {
            _log.Error($"Update cycle failed with unhandled exception:\n{ex}");
            return false;
        }
    }

    /// <summary>
    /// Shutdown servers only (no update/restart): broadcast → kick → save → exit → confirm.
    /// Returns true if shutdown completed successfully.
    /// </summary>
    public async Task<bool> ShutdownServersOnlyAsync(int countdownSeconds = 300)
    {
        _log.Info("============================================================");
        _log.Info("  SHUTDOWN SEQUENCE STARTED (NO RESTART)");
        _log.Info("============================================================");
        var cycleStart = DateTime.Now;

        try
        {
            // ── Load server information ────────────────────────────────
            var servers = LoadServerInfos();
            if (servers.Count == 0)
            {
                _log.Error("No servers found in ASCT configuration. Aborting shutdown.");
                return false;
            }

            foreach (var s in servers)
                _log.Info($"  Server: {s.Config.Name}  |  Port {s.Config.GamePort}  |  RCON {s.RconPort}");

            // ── Determine which servers are currently running ──────────
            var runningServers = servers.Where(s => FindServerProcess(s) != null).ToList();
            _log.Info($"{runningServers.Count}/{servers.Count} server(s) currently running.");

            if (runningServers.Count > 0)
            {
                // Phase 1 — Countdown broadcasts (staggered per server)
                _log.Info("────────────────────────────────────────────────────────────");
                _log.Info("[Phase 1] Broadcasting shutdown warnings...");
                _log.Info("────────────────────────────────────────────────────────────");
                await Phase1_BroadcastCountdownAsync(runningServers, countdownSeconds, isRestart: false);

                // Phase 2 — Kick all players, save, exit (parallel across servers)
                _log.Info("────────────────────────────────────────────────────────────");
                _log.Info("[Phase 2] Shutdown sequence: kick → save → doexit");
                _log.Info("────────────────────────────────────────────────────────────");
                await Phase2_ShutdownServersAsync(runningServers);
            }
            else
            {
                _log.Info("No servers running — nothing to shut down.");
                return true;
            }

            // Phase 3 — Confirm all processes dead + cooldown
            _log.Info("────────────────────────────────────────────────────────────");
            _log.Info("[Phase 3] Confirming all server processes have exited...");
            _log.Info("────────────────────────────────────────────────────────────");
            await Phase3_ConfirmProcessExitAsync(servers);

            var elapsed = DateTime.Now - cycleStart;
            _log.Info("============================================================");
            _log.Info($"  SHUTDOWN COMPLETED — {FormatElapsedTime(elapsed)}");
            _log.Info("============================================================");
            return true;
        }
        catch (Exception ex)
        {
            _log.Error($"Shutdown sequence failed with unhandled exception:\n{ex}");
            return false;
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Single-server operations
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Start a single server.
    /// </summary>
    public Task<bool> StartSingleServerAsync(ServerInfo server)
    {
        _log.Info($"[{server.Config.Name}] Starting server...");
        
        try
        {
            var process = StartServerProcess(server);
            if (process == null)
            {
                _log.Error($"[{server.Config.Name}] Failed to start server process");
                return Task.FromResult(false);
            }

            _log.Info($"[{server.Config.Name}] Server started successfully");
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _log.Error($"[{server.Config.Name}] Failed to start: {ex.Message}");
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// Start all offline servers (staggered). Skips servers that are already running.
    /// </summary>
    public async Task<bool> StartAllOfflineServersAsync(List<ServerInfo> offlineServers)
    {
        _log.Info("============================================================");
        _log.Info($"  STARTING {offlineServers.Count} OFFLINE SERVER(S)");
        _log.Info("============================================================");

        int successCount = 0;
        int skippedCount = 0;
        int failedCount = 0;

        for (int i = 0; i < offlineServers.Count; i++)
        {
            var server = offlineServers[i];

            // Double-check if server is already running (race condition protection)
            var existingProcess = FindServerProcess(server);
            if (existingProcess != null)
            {
                _log.Info($"[{server.Config.Name}] Already running - skipping");
                skippedCount++;
                continue;
            }

            // Add stagger delay between servers (except for first one)
            if (i > 0)
            {
                _log.Info($"Stagger delay: {_config.ServerStartStaggerSeconds}s before next server...");
                await Task.Delay(_config.ServerStartStaggerSeconds * 1000);
            }

            try
            {
                var process = StartServerProcess(server);
                if (process != null)
                {
                    successCount++;
                }
                else
                {
                    failedCount++;
                }
            }
            catch (Exception ex)
            {
                _log.Error($"[{server.Config.Name}] Failed to start: {ex.Message}");
                failedCount++;
            }
        }

        _log.Info("============================================================");
        _log.Info($"  START ALL COMPLETED: {successCount} started, {skippedCount} skipped, {failedCount} failed");
        _log.Info("============================================================");

        return failedCount == 0;
    }

    /// <summary>
    /// Restart a single server with countdown, optional update.
    /// </summary>
    public async Task<bool> RestartSingleServerAsync(ServerInfo server, bool runUpdate = true, int countdownSeconds = 300)
    {
        _log.Info($"[{server.Config.Name}] ══════════════════════════════════════");
        _log.Info($"[{server.Config.Name}] SINGLE SERVER RESTART ({(runUpdate ? "WITH UPDATE" : "NO UPDATE")})");
        _log.Info($"[{server.Config.Name}] ══════════════════════════════════════");

        try
        {
            var servers = new List<ServerInfo> { server };

            // Countdown with RESTART messages
            _log.Info($"[{server.Config.Name}] Broadcasting restart countdown...");
            await Phase1_BroadcastCountdownAsync(servers, countdownSeconds, isRestart: true);

            // Shutdown
            _log.Info($"[{server.Config.Name}] Shutting down...");
            await Phase2_ShutdownServersAsync(servers);
            await Phase3_ConfirmProcessExitAsync(servers);

            await Task.Delay(_config.PostShutdownCooldownSeconds * 1000);

            // Update (optional)
            if (runUpdate)
            {
                _log.Info($"[{server.Config.Name}] Running update...");
                bool updated = await UpdateSingleServerAsync(server);
                if (!updated)
                {
                    _log.Error($"[{server.Config.Name}] Update failed - aborting restart");
                    return false;
                }
            }
            else
            {
                _log.Info($"[{server.Config.Name}] Skipping update");
            }

            // Start
            _log.Info($"[{server.Config.Name}] Starting server...");
            var process = StartServerProcess(server);
            if (process == null)
            {
                _log.Error($"[{server.Config.Name}] Failed to start server process");
                return false;
            }

            _log.Info($"[{server.Config.Name}] Restart completed successfully");
            return true;
        }
        catch (Exception ex)
        {
            _log.Error($"[{server.Config.Name}] Restart failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Stop a single server with countdown.
    /// </summary>
    public async Task<bool> StopSingleServerAsync(ServerInfo server, int countdownSeconds = 300)
    {
        _log.Info($"[{server.Config.Name}] ══════════════════════════════════════");
        _log.Info($"[{server.Config.Name}] SINGLE SERVER STOP");
        _log.Info($"[{server.Config.Name}] ══════════════════════════════════════");

        try
        {
            var servers = new List<ServerInfo> { server };

            // Countdown with SHUTDOWN messages
            _log.Info($"[{server.Config.Name}] Broadcasting shutdown countdown...");
            await Phase1_BroadcastCountdownAsync(servers, countdownSeconds, isRestart: false);

            // Shutdown
            _log.Info($"[{server.Config.Name}] Shutting down...");
            await Phase2_ShutdownServersAsync(servers);
            await Phase3_ConfirmProcessExitAsync(servers);

            _log.Info($"[{server.Config.Name}] Server stopped successfully");
            return true;
        }
        catch (Exception ex)
        {
            _log.Error($"[{server.Config.Name}] Stop failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Broadcasts a message to all servers via RCON.
    /// </summary>
    public async Task BroadcastToAllServersAsync(string message)
    {
        var servers = LoadServerInfos();
        var runningServers = servers.Where(s => FindServerProcess(s) != null).ToList();

        if (runningServers.Count == 0)
        {
            _log.Warning($"No running servers to broadcast message to.");
            return;
        }

        _log.Info($"Broadcasting to {runningServers.Count} server(s): {message}");

        foreach (var server in runningServers)
        {
            await BroadcastToServerAsync(server, message);
            
            // Small stagger to avoid overwhelming RCON
            if (server != runningServers.Last())
                await Task.Delay(_config.BroadcastStaggerSeconds * 1000);
        }
    }

    private async Task<bool> UpdateSingleServerAsync(ServerInfo server)
    {
        // Read the validate flag from the live ASCT config
        bool validate;
        try
        {
            var asctJson = File.ReadAllText(_asctConfigPath);
            var asctCfg = JsonConvert.DeserializeObject<ASCTGlobalConfig>(asctJson);
            validate = asctCfg?.ValidateUpdates ?? true;
        }
        catch
        {
            validate = true;
        }

        for (int attempt = 1; attempt <= _config.UpdateMaxRetries; attempt++)
        {
            _log.Info($"[{server.Config.Name}] Update attempt {attempt}/{_config.UpdateMaxRetries}...");

            var args = $"-app {_config.SteamAppId} -dir \"{server.Config.GameDirectory}\"";
            if (validate)
                args += " -validate";
            if (!string.IsNullOrWhiteSpace(_config.DepotDownloaderExtraArgs))
                args += $" {_config.DepotDownloaderExtraArgs}";

            var result = await RunProcessCapturedAsync(_depotDownloaderPath, args);

            if (!string.IsNullOrWhiteSpace(result.StdOut))
            {
                string trimmedOut = result.StdOut.Length > 2000
                    ? result.StdOut[..2000] + "\n... (output truncated)"
                    : result.StdOut;
                _log.Info($"[{server.Config.Name}] DepotDownloader stdout:\n{trimmedOut}");
            }
            if (!string.IsNullOrWhiteSpace(result.StdErr))
                _log.Warning($"[{server.Config.Name}] DepotDownloader stderr:\n{result.StdErr}");

            _log.Info($"[{server.Config.Name}] Exit code: {result.ExitCode}");

            if (result.ExitCode == 0)
            {
                _log.Info($"[{server.Config.Name}] Update completed successfully");
                return true;
            }

            _log.Warning($"[{server.Config.Name}] Update failed on attempt {attempt}.");

            if (attempt < _config.UpdateMaxRetries)
            {
                _log.Info($"[{server.Config.Name}] Retrying in {_config.UpdateRetryDelaySeconds}s...");
                await Task.Delay(_config.UpdateRetryDelaySeconds * 1000);
            }
        }

        _log.Error($"[{server.Config.Name}] Update FAILED after {_config.UpdateMaxRetries} attempts");
        return false;
    }

    private Process? StartServerProcess(ServerInfo server)
    {
        try
        {
            if (!File.Exists(server.ServerExePath))
            {
                _log.Error($"[{server.Config.Name}] Server executable not found: {server.ServerExePath}");
                return null;
            }

            string launchArgs;
            if (server.Config.UseCustomLaunchArgs && !string.IsNullOrWhiteSpace(server.Config.CustomLaunchArgs))
            {
                launchArgs = server.Config.CustomLaunchArgs;
            }
            else
            {
                launchArgs = BuildDefaultLaunchArgs(server);
                _log.Warning($"[{server.Config.Name}] No custom launch args — using generated defaults.");
            }

            var psi = new ProcessStartInfo
            {
                FileName = server.ServerExePath,
                Arguments = launchArgs,
                WorkingDirectory = Path.GetDirectoryName(server.ServerExePath)!,
                UseShellExecute = true
            };

            var process = Process.Start(psi);
            _log.Info($"[{server.Config.Name}] Started. Args: {launchArgs}");
            return process;
        }
        catch (Exception ex)
        {
            _log.Error($"[{server.Config.Name}] Failed to start server: {ex.Message}");
            return null;
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Phase 1 — Countdown broadcasts
    // ════════════════════════════════════════════════════════════════════════

    private async Task Phase1_BroadcastCountdownAsync(List<ServerInfo> servers, int countdownSeconds = 300, bool isRestart = true)
    {
        var messages = GenerateCountdownMessages(countdownSeconds, isRestart);

        if (messages.Count == 0)
        {
            _log.Warning("No countdown messages generated — skipping Phase 1.");
            return;
        }

        // The first message's SecondsRemaining defines the total countdown duration.
        int totalCountdownSec = messages[0].SecondsRemaining;
        var shutdownTime = DateTime.UtcNow.AddSeconds(totalCountdownSec);

        _log.Info($"Countdown: {totalCountdownSec}s until shutdown ({messages.Count} messages configured).");

        for (int i = 0; i < messages.Count; i++)
        {
            var msg = messages[i];

            // Wait until it's time to send this message
            var sendAt = shutdownTime.AddSeconds(-msg.SecondsRemaining);
            var delay = sendAt - DateTime.UtcNow;
            if (delay.TotalMilliseconds > 0)
                await Task.Delay(delay);

            // Broadcast to each server, staggered
            for (int s = 0; s < servers.Count; s++)
            {
                if (s > 0)
                    await Task.Delay(_config.BroadcastStaggerSeconds * 1000);

                await BroadcastToServerAsync(servers[s], msg.Message);
            }
        }

        // Wait any remaining time until the shutdown moment
        var remaining = shutdownTime - DateTime.UtcNow;
        if (remaining.TotalMilliseconds > 0)
        {
            _log.Info($"Final countdown: {remaining.TotalSeconds:F0}s remaining...");
            await Task.Delay(remaining);
        }

        _log.Info("Countdown complete — proceeding to shutdown.");
    }

    private async Task BroadcastToServerAsync(ServerInfo server, string message)
    {
        try
        {
            using var rcon = new RconClient(_config.RconTimeoutMs);
            await rcon.ConnectAsync("127.0.0.1", server.RconPort, server.AdminPassword);
            await rcon.SendCommandAsync($"serverchat {message}");
            _log.Info($"[{server.Config.Name}] Broadcast: \"{message}\"");
        }
        catch (Exception ex)
        {
            _log.Warning($"[{server.Config.Name}] Broadcast failed: {ex.Message}");
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Phase 2 — Kick all players → Save → Exit (parallel across servers)
    // ════════════════════════════════════════════════════════════════════════

    private async Task Phase2_ShutdownServersAsync(List<ServerInfo> servers)
    {
        var tasks = servers.Select(ShutdownSingleServerAsync).ToArray();
        await Task.WhenAll(tasks);
        _log.Info("[Phase 2] All shutdown commands issued.");
    }

    private async Task ShutdownSingleServerAsync(ServerInfo server)
    {
        try
        {
            using var rcon = new RconClient(_config.RconTimeoutMs);
            await rcon.ConnectAsync("127.0.0.1", server.RconPort, server.AdminPassword);

            // ── Rapid kick loop ────────────────────────────────────────
            _log.Info($"[{server.Config.Name}] Rapid kick loop started ({_config.KickMonitorDurationSeconds}s)...");
            var kickEnd = DateTime.UtcNow.AddSeconds(_config.KickMonitorDurationSeconds);
            int totalKicked = 0;

            while (DateTime.UtcNow < kickEnd)
            {
                try
                {
                    string response = await rcon.SendCommandAsync("listplayers");
                    var playerIds = ParsePlayerList(response);

                    foreach (var pid in playerIds)
                    {
                        try
                        {
                            await rcon.SendCommandAsync($"kickplayer {pid}");
                            totalKicked++;
                            _log.Info($"[{server.Config.Name}] Kicked: {pid}");
                        }
                        catch (Exception ex)
                        {
                            _log.Warning($"[{server.Config.Name}] Failed to kick {pid}: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _log.Warning($"[{server.Config.Name}] listplayers error: {ex.Message}");
                }

                var untilEnd = kickEnd - DateTime.UtcNow;
                if (untilEnd.TotalMilliseconds > 0)
                    await Task.Delay(Math.Min(_config.KickLoopIntervalMs, (int)untilEnd.TotalMilliseconds));
            }

            _log.Info($"[{server.Config.Name}] Kick loop finished — {totalKicked} kick command(s) sent.");

            // ── Save world ─────────────────────────────────────────────
            _log.Info($"[{server.Config.Name}] Saving world...");
            try
            {
                await rcon.SendCommandAsync("saveworld");
                _log.Info($"[{server.Config.Name}] saveworld acknowledged. Waiting {_config.SaveWorldDelaySeconds}s for flush...");
                await Task.Delay(_config.SaveWorldDelaySeconds * 1000);
            }
            catch (Exception ex)
            {
                _log.Warning($"[{server.Config.Name}] saveworld failed: {ex.Message} — proceeding to doexit anyway.");
            }

            // ── Graceful exit ──────────────────────────────────────────
            _log.Info($"[{server.Config.Name}] Sending doexit...");
            try
            {
                await rcon.SendCommandAsync("doexit");
            }
            catch
            {
                // Expected — the server closes the connection as part of its shutdown.
            }

            _log.Info($"[{server.Config.Name}] doexit issued.");
        }
        catch (Exception ex)
        {
            _log.Warning($"[{server.Config.Name}] RCON shutdown sequence failed: {ex.Message}");
            _log.Warning($"[{server.Config.Name}] Server will be force-killed in Phase 3 if still running.");
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Phase 3 — Confirm every server process has exited
    // ════════════════════════════════════════════════════════════════════════

    private async Task Phase3_ConfirmProcessExitAsync(List<ServerInfo> servers)
    {
        foreach (var server in servers)
        {
            var process = FindServerProcess(server);
            if (process == null)
            {
                _log.Info($"[{server.Config.Name}] Process not running — OK.");
                continue;
            }

            _log.Info($"[{server.Config.Name}] Waiting for process (PID {process.Id}) to exit (timeout {_config.ProcessExitTimeoutSeconds}s)...");

            try
            {
                using var cts = new CancellationTokenSource(_config.ProcessExitTimeoutSeconds * 1000);
                await process.WaitForExitAsync(cts.Token);
                _log.Info($"[{server.Config.Name}] Process exited gracefully.");
            }
            catch (OperationCanceledException)
            {
                _log.Warning($"[{server.Config.Name}] Process did not exit within {_config.ProcessExitTimeoutSeconds}s — force-killing.");
                try
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(10_000);
                    _log.Info($"[{server.Config.Name}] Process force-killed successfully.");
                }
                catch (Exception ex)
                {
                    _log.Error($"[{server.Config.Name}] Failed to kill process: {ex.Message}");
                }
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Phase 4 — Sequential server updates via DepotDownloader
    // ════════════════════════════════════════════════════════════════════════

    private async Task<bool> Phase4_UpdateServersAsync(List<ServerInfo> servers)
    {
        // Read the validate flag from the live ASCT config
        bool validate;
        try
        {
            var asctJson = File.ReadAllText(_asctConfigPath);
            var asctCfg = JsonConvert.DeserializeObject<ASCTGlobalConfig>(asctJson);
            validate = asctCfg?.ValidateUpdates ?? true;
        }
        catch
        {
            validate = true; // Default to validation if we can't read the flag
        }

        foreach (var server in servers)
        {
            bool success = false;

            for (int attempt = 1; attempt <= _config.UpdateMaxRetries; attempt++)
            {
                _log.Info($"[{server.Config.Name}] Update attempt {attempt}/{_config.UpdateMaxRetries}...");

                var args = $"-app {_config.SteamAppId} -dir \"{server.Config.GameDirectory}\"";
                if (validate)
                    args += " -validate";
                if (!string.IsNullOrWhiteSpace(_config.DepotDownloaderExtraArgs))
                    args += $" {_config.DepotDownloaderExtraArgs}";

                var result = await RunProcessCapturedAsync(_depotDownloaderPath, args);

                // Log output (trim to avoid massive log dumps)
                if (!string.IsNullOrWhiteSpace(result.StdOut))
                {
                    string trimmedOut = result.StdOut.Length > 2000
                        ? result.StdOut[..2000] + "\n... (output truncated)"
                        : result.StdOut;
                    _log.Info($"[{server.Config.Name}] DepotDownloader stdout:\n{trimmedOut}");
                }
                if (!string.IsNullOrWhiteSpace(result.StdErr))
                    _log.Warning($"[{server.Config.Name}] DepotDownloader stderr:\n{result.StdErr}");

                _log.Info($"[{server.Config.Name}] Exit code: {result.ExitCode}");

                if (result.ExitCode == 0)
                {
                    success = true;
                    _log.Info($"[{server.Config.Name}] Update completed successfully.");
                    break;
                }

                _log.Warning($"[{server.Config.Name}] Update failed on attempt {attempt}.");

                if (attempt < _config.UpdateMaxRetries)
                {
                    _log.Info($"[{server.Config.Name}] Retrying in {_config.UpdateRetryDelaySeconds}s...");
                    await Task.Delay(_config.UpdateRetryDelaySeconds * 1000);
                }
            }

            if (!success)
            {
                _log.Error($"[{server.Config.Name}] UPDATE FAILED after {_config.UpdateMaxRetries} attempt(s).");
                return false;
            }
        }

        _log.Info("All servers updated successfully.");
        return true;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Phase 5 — Start servers sequentially with stagger
    // ════════════════════════════════════════════════════════════════════════

    private async Task Phase5_StartServersAsync(List<ServerInfo> servers)
    {
        for (int i = 0; i < servers.Count; i++)
        {
            var server = servers[i];

            if (i > 0)
            {
                _log.Info($"Stagger delay: {_config.ServerStartStaggerSeconds}s before next server...");
                await Task.Delay(_config.ServerStartStaggerSeconds * 1000);
            }

            try
            {
                if (!File.Exists(server.ServerExePath))
                {
                    _log.Error($"[{server.Config.Name}] Server executable not found: {server.ServerExePath}");
                    continue;
                }

                string launchArgs;
                if (server.Config.UseCustomLaunchArgs && !string.IsNullOrWhiteSpace(server.Config.CustomLaunchArgs))
                {
                    launchArgs = server.Config.CustomLaunchArgs;
                }
                else
                {
                    launchArgs = BuildDefaultLaunchArgs(server);
                    _log.Warning($"[{server.Config.Name}] No custom launch args — using generated defaults.");
                }

                var psi = new ProcessStartInfo
                {
                    FileName = server.ServerExePath,
                    Arguments = launchArgs,
                    WorkingDirectory = Path.GetDirectoryName(server.ServerExePath)!,
                    UseShellExecute = true
                };

                Process.Start(psi);
                _log.Info($"[{server.Config.Name}] Started. Args: {launchArgs}");
            }
            catch (Exception ex)
            {
                _log.Error($"[{server.Config.Name}] Failed to start server: {ex.Message}");
            }
        }

        _log.Info("All servers have been started.");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Helpers
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generate countdown messages dynamically based on total countdown duration and operation type.
    /// </summary>
    private List<CountdownMessage> GenerateCountdownMessages(int totalSeconds, bool isRestart = true)
    {
        // Use the appropriate message set from config
        var sourceMessages = isRestart ? _config.CountdownMessages : _config.ShutdownMessages;

        // Filter messages to only include those that fit within the countdown duration
        return sourceMessages
            .Where(m => m.SecondsRemaining <= totalSeconds)
            .OrderByDescending(m => m.SecondsRemaining)
            .ToList();
    }

    /// <summary>
    /// Load all servers from the ASCT config, enriching each with RCON port and admin password
    /// read from the server's own GameUserSettings.ini.
    /// </summary>
    private List<ServerInfo> LoadServerInfos()
    {
        var json = File.ReadAllText(_asctConfigPath);
        var asctConfig = JsonConvert.DeserializeObject<ASCTGlobalConfig>(json)
            ?? throw new InvalidOperationException("Failed to parse ASCT configuration.");

        var servers = new List<ServerInfo>();

        foreach (var sc in asctConfig.Servers)
        {
            var (rconPort, adminPass) = ReadServerIniSettings(sc.GameDirectory);

            if (rconPort == 0)
            {
                _log.Warning($"[{sc.Name}] Could not read RCONPort from GameUserSettings.ini — skipping this server.");
                continue;
            }

            if (string.IsNullOrEmpty(adminPass))
            {
                _log.Warning($"[{sc.Name}] ServerAdminPassword is empty — RCON commands may fail.");
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

    /// <summary>
    /// Parse RCONPort and ServerAdminPassword from a server's GameUserSettings.ini.
    /// </summary>
    private static (int rconPort, string adminPassword) ReadServerIniSettings(string gameDirectory)
    {
        var iniPath = Path.Combine(
            gameDirectory, "ShooterGame", "Saved", "Config", "WindowsServer", "GameUserSettings.ini");

        if (!File.Exists(iniPath))
            return (0, "");

        int rconPort = 0;
        string adminPassword = "";

        foreach (var rawLine in File.ReadLines(iniPath))
        {
            var line = rawLine.Trim();

            if (line.StartsWith("RCONPort=", StringComparison.OrdinalIgnoreCase))
            {
                int.TryParse(line.AsSpan("RCONPort=".Length), out rconPort);
            }
            else if (line.StartsWith("ServerAdminPassword=", StringComparison.OrdinalIgnoreCase))
            {
                adminPassword = line["ServerAdminPassword=".Length..];
            }
        }

        return (rconPort, adminPassword);
    }

    /// <summary>
    /// Find a running ArkAscendedServer.exe process that belongs to a specific server
    /// by matching the full executable path.
    /// </summary>
    private static Process? FindServerProcess(ServerInfo server)
    {
        var targetExe = Path.GetFullPath(server.ServerExePath);

        try
        {
            foreach (var proc in Process.GetProcessesByName("ArkAscendedServer"))
            {
                try
                {
                    if (string.Equals(proc.MainModule?.FileName, targetExe, StringComparison.OrdinalIgnoreCase))
                        return proc;
                }
                catch
                {
                    // Access denied for processes owned by other users — skip.
                }
            }
        }
        catch
        {
            // GetProcessesByName can throw if the process list changes during enumeration.
        }

        return null;
    }

    /// <summary>
    /// Parse the output of the RCON "listplayers" command into a list of player IDs.
    /// Expected line format: "0. PlayerName, SteamID_or_EOS_ID"
    /// </summary>
    private static List<string> ParsePlayerList(string response)
    {
        var ids = new List<string>();
        if (string.IsNullOrWhiteSpace(response))
            return ids;

        // Match lines like "0. SomeName, 76561198012345678" or "0. SomeName, EOS_00abc..."
        var regex = new Regex(@"^\d+\.\s+.+,\s+(\S+)\s*$", RegexOptions.Multiline);
        foreach (Match match in regex.Matches(response))
        {
            if (match.Groups[1].Success)
                ids.Add(match.Groups[1].Value);
        }

        return ids;
    }

    /// <summary>
    /// Format elapsed time in a human-readable way (e.g., "2 minutes 35 seconds").
    /// </summary>
    private static string FormatElapsedTime(TimeSpan elapsed)
    {
        if (elapsed.TotalHours >= 1)
        {
            return $"{(int)elapsed.TotalHours} hour{((int)elapsed.TotalHours != 1 ? "s" : "")} {elapsed.Minutes} minute{(elapsed.Minutes != 1 ? "s" : "")} {elapsed.Seconds} second{(elapsed.Seconds != 1 ? "s" : "")}";
        }
        else if (elapsed.TotalMinutes >= 1)
        {
            return $"{elapsed.Minutes} minute{(elapsed.Minutes != 1 ? "s" : "")} {elapsed.Seconds} second{(elapsed.Seconds != 1 ? "s" : "")}";
        }
        else
        {
            return $"{elapsed.Seconds} second{(elapsed.Seconds != 1 ? "s" : "")}";
        }
    }

    /// <summary>
    /// Build minimal launch arguments when customLaunchArgs is not available.
    /// This is a fallback — customLaunchArgs from ASCT config should be preferred.
    /// </summary>
    private static string BuildDefaultLaunchArgs(ServerInfo server)
    {
        var sc = server.Config;
        var mods = sc.ModIds.Count > 0 ? $" \"-mods={string.Join(",", sc.ModIds)}\"" : "";
        return $"\"{sc.Map}\" \"-port={sc.GamePort}\" -WinLiveMaxPlayers={sc.Slots}{mods}";
    }

    /// <summary>
    /// Run an external process, capturing stdout and stderr.
    /// </summary>
    private static async Task<ProcessResult> RunProcessCapturedAsync(string fileName, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var process = new Process { StartInfo = psi };
        process.Start();

        // Read stdout and stderr concurrently to avoid deadlocks
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        return new ProcessResult(
            ExitCode: process.ExitCode,
            StdOut: await stdoutTask,
            StdErr: await stderrTask
        );
    }

    private sealed record ProcessResult(int ExitCode, string StdOut, string StdErr);
}
