namespace ArkAutomata;

class Program
{
    static async Task<int> Main(string[] args)
    {
        using var mutex = new Mutex(true, @"Global\ArkAutomata_SingleInstance", out bool isNew);
        if (!isNew)
        {
            Console.WriteLine("[ERROR] Another instance of ArkAutomata is already running. Exiting.");
            return 1;
        }

        string baseDir = FindBaseDirectory();
        string configPath = Path.Combine(baseDir, "config.json");

        if (!File.Exists(configPath))
        {
            Console.WriteLine($"[ERROR] config.json not found. Searched in: {baseDir}");
            Console.WriteLine("Make sure config.json is in the same directory as the executable or current working directory.");
            return 1;
        }

        var logger = new AutomataLogger(Path.Combine(baseDir, "autologs"));
        logger.Info("============================================================");
        logger.Info("  ArkAutomata — Autonomous ARK Server Update Manager");
        logger.Info("============================================================");
        logger.Info($"Base directory : {baseDir}");
        logger.Info($"Config file    : {configPath}");

        try
        {
            var config = AutomataConfig.Load(configPath);
            logger.Info($"Schedule       : every {config.ScheduleIntervalMinutes} minutes");

            string asctConfigPath = Path.GetFullPath(
                Path.Combine(baseDir, config.ASCTConfigRelativePath));
            string depotDownloaderPath = Path.GetFullPath(
                Path.Combine(baseDir, config.DepotDownloaderRelativePath));

            logger.Info($"ASCT config    : {asctConfigPath}");
            logger.Info($"DepotDownloader: {depotDownloaderPath}");

            if (!File.Exists(asctConfigPath))
            {
                logger.Error($"ASCT configuration file not found: {asctConfigPath}");
                return 1;
            }
            if (!File.Exists(depotDownloaderPath))
            {
                logger.Error($"DepotDownloader executable not found: {depotDownloaderPath}");
                return 1;
            }

            var orchestrator = new Orchestrator(config, asctConfigPath, depotDownloaderPath, logger);

            bool flagOnce = args.Any(a => a.Equals("--once", StringComparison.OrdinalIgnoreCase));
            bool flagNow  = args.Any(a => a.Equals("--now",  StringComparison.OrdinalIgnoreCase));

            if (flagOnce)
            {
                logger.Info("Mode: --once (single cycle, then exit)");
                bool ok = await orchestrator.RunUpdateCycleAsync();
                return ok ? 0 : 1;
            }

            logger.Info("Mode: Scheduled (continuous)");

            if (flagNow)
            {
                logger.Info("--now flag detected — running first cycle immediately.");
            }
            else
            {
                var nextRun = DateTime.Now.AddMinutes(config.ScheduleIntervalMinutes);
                logger.Info($"Waiting {config.ScheduleIntervalMinutes} minutes before first cycle (next run ~{nextRun:HH:mm:ss}).");
                logger.Info("Tip: Use the --now flag to run the first cycle immediately.");
                await Task.Delay(TimeSpan.FromMinutes(config.ScheduleIntervalMinutes));
            }

            while (true)
            {
                await orchestrator.RunUpdateCycleAsync();

                var next = DateTime.Now.AddMinutes(config.ScheduleIntervalMinutes);
                logger.Info($"Next update cycle at ~{next:HH:mm:ss} ({config.ScheduleIntervalMinutes} minutes from now).");
                await Task.Delay(TimeSpan.FromMinutes(config.ScheduleIntervalMinutes));
            }
        }
        catch (Exception ex)
        {
            logger.Error($"Fatal error — ArkAutomata is shutting down:\n{ex}");
            return 1;
        }
    }

    // cwd, then the exe folder, then parents so a build under bin/Debug still finds config.json.
    private static string FindBaseDirectory()
    {
        string cwd = Environment.CurrentDirectory;
        if (File.Exists(Path.Combine(cwd, "config.json")))
            return cwd;

        string exeDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        if (File.Exists(Path.Combine(exeDir, "config.json")))
            return exeDir;

        var dir = Directory.GetParent(exeDir);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "config.json")))
                return dir.FullName;
            dir = dir.Parent;
        }

        return cwd;
    }
}
