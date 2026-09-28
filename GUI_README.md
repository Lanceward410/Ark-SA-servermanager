# ARK Automata GUI

Dashboard for the ASA servers: fixed-time restarts, broadcasts, RCON, and a tray icon so it can sit in the background.

## Build and run

```powershell
cd C:\ARK.Server.Creation.Tool\Automata
dotnet build ArkAutomata.GUI.csproj
.\bin\Debug\net9.0-windows\ArkAutomata.GUI.exe
```

`ark.ico` has to sit in this folder or the tray icon will not show.

```powershell
# Window hidden, tray only. Used by the startup task.
.\bin\Debug\net9.0-windows\ArkAutomata.GUI.exe --minimize
```

## Dashboard

- **Restart All Servers Now** runs a cycle immediately. It does not move the next scheduled time.
- Broadcast goes to every server, or to the row you have selected. The dropdown is `BroadcastTemplates` from config.
- **RCON Console** on a row opens a terminal for that server: free-form commands, a few shortcuts (listplayers, saveworld), and the reply text.

Tray menu: open the dashboard, run an update, or exit. Double-click the icon to open the window.

The log tab tails `autologs/automata_YYYY-MM-DD.log` (last 100 lines, every 2 seconds). Player counts refresh about every 30 seconds.

## Schedule

`SchedulerService` checks every 30 seconds. When the clock hits the next hour in `FixedScheduleHoursEST`, it runs the update cycle and then computes the following slot. A manual restart or shutdown leaves that slot where it is.

Hours are interpreted in `TimeZone` (Eastern Standard Time).

## config.json

```json
{
  "UseFixedSchedule": true,
  "FixedScheduleHoursEST": [0, 2, 4, 6, 8, 10, 12, 14, 16, 18, 20, 22],
  "TimeZone": "Eastern Standard Time",
  "BroadcastTemplates": [
    "Server restart in 15 minutes for maintenance",
    "Admin is online - please report any issues!",
    "Special event rates active!"
  ]
}
```

`ScheduleIntervalMinutes` only matters when `UseFixedSchedule` is false. That path is the console app (`Program.cs`), not the GUI scheduler.

## Startup task

Run PowerShell as administrator:

```powershell
$action = New-ScheduledTaskAction -Execute "C:\ARK.Server.Creation.Tool\Automata\bin\Debug\net9.0-windows\ArkAutomata.GUI.exe" -Argument "--minimize"
$trigger = New-ScheduledTaskTrigger -AtStartup
$principal = New-ScheduledTaskPrincipal -UserId "Lance" -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable

Register-ScheduledTask -TaskName "ARK Automata GUI" -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force
```

Copy the Automata folder to the other MiniPCs after a local build. The exe looks for `config.json` beside itself, and for `ASCTGlobalConfig.json` / DepotDownloader via the relative paths in config.

## If something fails

- RCON will not connect if the server is down, or if `GameUserSettings.ini` does not have the expected `RCONPort` (27817, 27827, 27837, 27847).
- A missing `ark.ico` means no tray icon.
- Two copies cannot run at once. The second one shows "already running" and exits.
