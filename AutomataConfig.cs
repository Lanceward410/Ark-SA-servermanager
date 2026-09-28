using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace ArkAutomata;

public sealed class AutomataConfig
{
    public bool UseFixedSchedule { get; set; } = true;

    // Ignored by the GUI scheduler. The console app still waits this many minutes.
    public int ScheduleIntervalMinutes { get; set; } = 360;

    public List<int> FixedScheduleHoursEST { get; set; } = new() { 0, 6, 12, 18 };

    public string TimeZone { get; set; } = "Eastern Standard Time";

    public List<string> BroadcastTemplates { get; set; } = new()
    {
        "Server restart in 15 minutes for maintenance",
        "Discord NSD74ZZGtn - please report any issues!",
        "Special event rates active!"
    };

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

    public int BroadcastStaggerSeconds { get; set; } = 5;

    public int PostShutdownCooldownSeconds { get; set; } = 15;

    public int ServerStartStaggerSeconds { get; set; } = 5;

    public int KickMonitorDurationSeconds { get; set; } = 7;

    public int KickLoopIntervalMs { get; set; } = 1000;

    public int UpdateMaxRetries { get; set; } = 3;

    public int UpdateRetryDelaySeconds { get; set; } = 30;

    public int ProcessExitTimeoutSeconds { get; set; } = 120;

    public int SaveWorldDelaySeconds { get; set; } = 5;

    public int RconTimeoutMs { get; set; } = 30000;

    public string ASCTConfigRelativePath { get; set; } = @"..\ASCTGlobalConfig.json";

    public string DepotDownloaderRelativePath { get; set; } = @"..\depotdownloader\DepotDownloader.exe";

    public int SteamAppId { get; set; } = 2430930; // ASA dedicated server

    public string DepotDownloaderExtraArgs { get; set; } = "";

    public PeriodicMessagesConfig PeriodicMessages { get; set; } = new();

    public static AutomataConfig Load(string path)
    {
        string json = File.ReadAllText(path);
        var settings = new JsonSerializerSettings
        {
            // Without Replace, Newtonsoft appends JSON arrays onto the defaults above.
            ObjectCreationHandling = ObjectCreationHandling.Replace
        };
        return JsonConvert.DeserializeObject<AutomataConfig>(json, settings)
            ?? throw new InvalidOperationException($"Failed to deserialize config from {path}");
    }
}

public sealed class CountdownMessage
{
    public int SecondsRemaining { get; set; }

    public string Message { get; set; } = "";
}

public sealed class PeriodicMessagesConfig
{
    public bool Enabled { get; set; } = true;

    public int IntervalMinutes { get; set; } = 45;

    public bool RandomizeOrder { get; set; } = true;

    public int PauseBeforeRestartMinutes { get; set; } = 10;

    public int ResumeAfterRestartMinutes { get; set; } = 10;

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

// Subset of ASCTGlobalConfig.json. Anything not mapped here is ignored.

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
