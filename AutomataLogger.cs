using System;
using System.IO;

namespace ArkAutomata;

/// <summary>
/// Thread-safe logger that writes to both console and daily-rotated log files in autologs/.
/// </summary>
public sealed class AutomataLogger
{
    private readonly string _logDir;
    private readonly object _lock = new();

    public AutomataLogger(string logDir)
    {
        _logDir = logDir;
        Directory.CreateDirectory(_logDir);
    }

    private string LogFilePath =>
        Path.Combine(_logDir, $"automata_{DateTime.Now:yyyy-MM-dd}.log");

    public void Info(string message) => Log("INFO", message);
    public void Warning(string message) => Log("WARN", message);
    public void Error(string message) => Log("ERROR", message);

    private void Log(string level, string message)
    {
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        string line = $"[{timestamp}] [{level,-5}] {message}";

        Console.WriteLine(line);

        lock (_lock)
        {
            try
            {
                File.AppendAllText(LogFilePath, line + Environment.NewLine);
            }
            catch
            {
                // If log file write fails, don't crash the application.
                // Console output above still captures the message.
            }
        }
    }
}
