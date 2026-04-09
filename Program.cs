namespace ArkAutomata;

class Program
{
    static async Task<int> Main(string[] args)
    {
        // ── Single-instance guard ──────────────────────────────────────
        using var mutex = new Mutex(true, @"Global\ArkAutomata_SingleInstance", out bool isNew);
        if (!isNew)
        {
            Console.WriteLine("[ERROR] Another instance of ArkAutomata is already running. Exiting.");
            return 1;
        }

        // ── Locate base directory (where config.json lives) ───────────
        string baseDir = FindBaseDirectory();
        string configPath = Path.Combine(baseDir, "config.json");

        if (!File.Exists(configPath))
        {
            Console.WriteLine($"[ERROR] config.json not found. Searched in: {baseDir}");
            Console.WriteLine("Make sure config.json is in the same directory as the executable or current working directory.");
            return 1;
        }

        // ── Initialise logger ─────────────────────────────────────────
        var logger = new AutomataLogger(Path.Combine(baseDir, "autologs"));
        logger.Info("============================================================");
        logger.Info("  ArkAutomata — Autonomous ARK Server Update Manager");
        logger.Info("============================================================");
        logger.Info($"Base directory : {baseDir}");
        logger.Info($"Config file    : {configPath}");

        try
        {
            // ── Load configuration ────────────────────────────────────
            var config = AutomataConfig.Load(configPath);
            logger.Info($"Schedule       : every {config.ScheduleIntervalMinutes} minutes");

            // ── Resolve dependent paths ───────────────────────────────
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

            // ── Create orchestrator ───────────────────────────────────
            var orchestrator = new Orchestrator(config, asctConfigPath, depotDownloaderPath, logger);

            // ── Parse command-line flags ──────────────────────────────
            bool flagOnce = args.Any(a => a.Equals("--once", StringComparison.OrdinalIgnoreCase));
            bool flagNow  = args.Any(a => a.Equals("--now",  StringComparison.OrdinalIgnoreCase));

            // --once : Run a single cycle immediately, then exit.
            if (flagOnce)
            {
                logger.Info("Mode: --once (single cycle, then exit)");
                bool ok = await orchestrator.RunUpdateCycleAsync();
                return ok ? 0 : 1;
            }

            // Scheduled mode (default)
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

            // ── Main scheduling loop ─────────────────────────────────
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

    // ── Locate the directory containing config.json ────────────────────

    /// <summary>
    /// Searches for config.json in order:
    ///   1. Current working directory
    ///   2. Directory containing the running executable
    ///   3. Walk upward from the executable directory
    /// Falls back to the current working directory.
    /// </summary>
    private static string FindBaseDirectory()
    {
        // 1. Current working directory
        string cwd = Environment.CurrentDirectory;
        if (File.Exists(Path.Combine(cwd, "config.json")))
            return cwd;

        // 2. Executable's own directory
        string exeDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        if (File.Exists(Path.Combine(exeDir, "config.json")))
            return exeDir;

        // 3. Walk upward from executable directory (handles bin/Debug/net9.0 during dev)
        var dir = Directory.GetParent(exeDir);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "config.json")))
                return dir.FullName;
            dir = dir.Parent;
        }

        // Fallback
        return cwd;
    }
}
