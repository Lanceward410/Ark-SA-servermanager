using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace ArkAutomata;

// ─── Automata's own configuration (config.json) ────────────────────────────

public sealed class AutomataConfig
{
    /// <summary>Use fixed schedule (true) or interval-based (false).</summary>
    public bool UseFixedSchedule { get; set; } = true;

    /// <summary>How often the update cycle runs, in minutes (used if UseFixedSchedule=false).</summary>
    public int ScheduleIntervalMinutes { get; set; } = 360;

    /// <summary>Restart hours in EST for fixed schedule mode.</summary>
    public List<int> FixedScheduleHoursEST { get; set; } = new() { 0, 6, 12, 18 };

    /// <summary>Time zone for fixed schedule (e.g., "Eastern Standard Time").</summary>
    public string TimeZone { get; set; } = "Eastern Standard Time";

    /// <summary>Pre-saved broadcast message templates.</summary>
    public List<string> BroadcastTemplates { get; set; } = new()
    {
        "Server restart in 15 minutes for maintenance",
        "Discord NSD74ZZGtn - please report any issues!",
        "Special event rates active!"
    };

    /// <summary>Countdown broadcast messages for RESTART operations.</summary>
    public List<CountdownMessage> CountdownMessages { get; set; } = new()
    {
        new() { SecondsRemaining = 300, Message = "Server restarting in 5 minutes. Please find a safe location." },
        new() { SecondsRemaining = 240, Message = "Server restarting in 4 minutes." },
        new() { SecondsRemaining = 180, Message = "Server restarting in 3 minutes." },
        new() { SecondsRemaining = 120, Message = "Server restarting in 2 minutes." },
        new() { SecondsRemaining = 60,  Message = "Server restarting in 1 minute. Discord NSD74ZZGtn" },
        new() { SecondsRemaining = 30,  Message = "Server restarting in 30 seconds!" },
        new() { SecondsRemaining = 15,  Message = "Server restarting in 15 seconds! Disconnecting all players." },
    };

    /// <summary>Countdown broadcast messages for SHUTDOWN operations.</summary>
    public List<CountdownMessage> ShutdownMessages { get; set; } = new()
    {
        new() { SecondsRemaining = 300, Message = "Server restarting in 5 minutes. Please find a safe location." },
        new() { SecondsRemaining = 240, Message = "Server restarting in 4 minutes." },
        new() { SecondsRemaining = 180, Message = "Server restarting in 3 minutes." },
        new() { SecondsRemaining = 120, Message = "Server restarting in 2 minutes." },
        new() { SecondsRemaining = 60,  Message = "Server restarting in 1 minute. Discord NSD74ZZGtn" },
        new() { SecondsRemaining = 30,  Message = "Server restarting in 30 seconds!" },
        new() { SecondsRemaining = 15,  Message = "Server restarting in 15 seconds! Disconnecting all players." },
    };

    /// <summary>Seconds between broadcasting to consecutive servers in each wave.</summary>
    public int BroadcastStaggerSeconds { get; set; } = 5;

    /// <summary>Seconds to wait after all servers are confirmed dead before starting updates.</summary>
    public int PostShutdownCooldownSeconds { get; set; } = 15;

    /// <summary>Seconds between starting each server in Phase 5.</summary>
    public int ServerStartStaggerSeconds { get; set; } = 5;

    /// <summary>How long to run the rapid-kick loop per server (seconds).</summary>
    public int KickMonitorDurationSeconds { get; set; } = 7;

    /// <summary>Milliseconds between each iteration of the kick loop.</summary>
    public int KickLoopIntervalMs { get; set; } = 1000;

    /// <summary>Maximum number of update attempts per server before aborting.</summary>
    public int UpdateMaxRetries { get; set; } = 3;

    /// <summary>Seconds to wait between update retry attempts.</summary>
    public int UpdateRetryDelaySeconds { get; set; } = 30;

    /// <summary>Max seconds to wait for a server process to exit before force-killing.</summary>
    public int ProcessExitTimeoutSeconds { get; set; } = 120;

    /// <summary>Seconds to wait after saveworld before sending doexit.</summary>
    public int SaveWorldDelaySeconds { get; set; } = 5;

    /// <summary>Timeout for RCON operations (connect + command response) in milliseconds.</summary>
    public int RconTimeoutMs { get; set; } = 30000;

    /// <summary>Relative path from the Automata directory to ASCTGlobalConfig.json.</summary>
    public string ASCTConfigRelativePath { get; set; } = @"..\ASCTGlobalConfig.json";

    /// <summary>Relative path from the Automata directory to DepotDownloader.exe.</summary>
    public string DepotDownloaderRelativePath { get; set; } = @"..\depotdownloader\DepotDownloader.exe";

    /// <summary>Steam App ID for the ARK: Survival Ascended dedicated server.</summary>
    public int SteamAppId { get; set; } = 2430930;

    /// <summary>Optional extra arguments passed to DepotDownloader (e.g. "-username X -password Y").</summary>
    public string DepotDownloaderExtraArgs { get; set; } = "";

    /// <summary>Configuration for periodic "tips and tricks" messages.</summary>
    public PeriodicMessagesConfig PeriodicMessages { get; set; } = new();

    public static AutomataConfig Load(string path)
    {
        string json = File.ReadAllText(path);
        var settings = new JsonSerializerSettings
        {
            ObjectCreationHandling = ObjectCreationHandling.Replace
        };
        return JsonConvert.DeserializeObject<AutomataConfig>(json, settings)
            ?? throw new InvalidOperationException($"Failed to deserialize config from {path}");
    }
}

public sealed class CountdownMessage
{
    /// <summary>Seconds remaining until shutdown when this message is broadcast.</summary>
    public int SecondsRemaining { get; set; }

    /// <summary>The broadcast message text.</summary>
    public string Message { get; set; } = "";
}

public sealed class PeriodicMessagesConfig
{
    /// <summary>Enable periodic tip messages.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Interval between messages in minutes.</summary>
    public int IntervalMinutes { get; set; } = 45;

    /// <summary>Randomize message order each restart cycle.</summary>
    public bool RandomizeOrder { get; set; } = true;

    /// <summary>Minutes to pause before a scheduled restart.</summary>
    public int PauseBeforeRestartMinutes { get; set; } = 10;

    /// <summary>Minutes to wait after restart before resuming messages.</summary>
    public int ResumeAfterRestartMinutes { get; set; } = 10;

    /// <summary>List of tip messages to broadcast.</summary>
    public List<string> Messages { get; set; } = new()
    {
        "Shiny dinos spawn up to 30 levels higher than their counterparts, with unique abilities and colors. Craft the Shiny dino tracker to get hunting.",
        "Blue Supply Crates contain boss artifacts and dino tributes!",
        "This server uses the Automated Ark mod. Craft the AA Workbench at Level 90 to get started!",
        "Discord NSD74ZZGtn - Please report any issues and suggestions! Your feedback makes the server better.",
        "When transferring servers, wait 5 seconds before clicking Spawn. If you click too soon, you mave have to try again from main menu.",
        "All maps have 'Ragnarok Style' Dino Level Distribution (less low levels)",
        "Take advantage of maps with classic supply drops! White drops can contain starter kits, while red drops can contain Tek building supplies",
        "Utilities Plus Spyglass can be crafted at level 50. Other Utilities Plus items are found in White Supply Crates!"
    };
}

// ─── ASCT configuration models (ASCTGlobalConfig.json) ─────────────────────
// Only the fields we need are mapped here.

public sealed class ASCTGlobalConfig
{
    [JsonProperty("validateUpdates")]
    public bool ValidateUpdates { get; set; } = true;

    [JsonProperty("Servers")]
    public List<ASCTServerConfig> Servers { get; set; } = new();

    [JsonProperty("ServersInstallationPath")]
    public string ServersInstallationPath { get; set; } = "";

    [JsonProperty("GlobalClusterDir")]
    public string GlobalClusterDir { get; set; } = "";
}

public sealed class ASCTServerConfig
{
    [JsonProperty("ID")]
    public int Id { get; set; }

    [JsonProperty("Name")]
    public string Name { get; set; } = "";

    [JsonProperty("GameDirectory")]
    public string GameDirectory { get; set; } = "";

    [JsonProperty("GamePort")]
    public int GamePort { get; set; }

    [JsonProperty("Map")]
    public string Map { get; set; } = "";

    [JsonProperty("useCustomLaunchArgs")]
    public bool UseCustomLaunchArgs { get; set; }

    [JsonProperty("customLaunchArgs")]
    public string CustomLaunchArgs { get; set; } = "";

    [JsonProperty("Slots")]
    public int Slots { get; set; }

    [JsonProperty("modIDs")]
    public List<int> ModIds { get; set; } = new();

    [JsonProperty("ClusterKey")]
    public string ClusterKey { get; set; } = "";

    [JsonProperty("NoBattleye")]
    public bool NoBattleye { get; set; }

    [JsonProperty("AllowCrossplay")]
    public bool AllowCrossplay { get; set; }
}
