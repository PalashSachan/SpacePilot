using System.Collections.Concurrent;
using Serilog;
using Serilog.Events;

namespace SpacePilot.Infrastructure.Logging;

public class LogMessageEventArgs : EventArgs
{
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public LogEventLevel Level { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? Exception { get; init; }
}

public static class LoggingService
{
    private static readonly ConcurrentQueue<LogMessageEventArgs> RecentLogs = new();
    private const int MaxInMemoryLogs = 1000;

    public static event EventHandler<LogMessageEventArgs>? OnLogMessage;

    public static string LogsDirectory { get; private set; } = string.Empty;

    public static void Initialize()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        LogsDirectory = Path.Combine(appData, "SpacePilot", "Logs");
        Directory.CreateDirectory(LogsDirectory);

        string logFilePath = Path.Combine(LogsDirectory, "spacepilot-.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                logFilePath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Log.Information("SpacePilot logging initialized. Logs directory: {LogsDir}", LogsDirectory);
    }

    public static void EmitLogEvent(LogEventLevel level, string message, Exception? ex = null)
    {
        var evt = new LogMessageEventArgs
        {
            Timestamp = DateTime.UtcNow,
            Level = level,
            Message = message,
            Exception = ex?.ToString()
        };

        RecentLogs.Enqueue(evt);
        while (RecentLogs.Count > MaxInMemoryLogs && RecentLogs.TryDequeue(out _)) { }

        OnLogMessage?.Invoke(null, evt);

        switch (level)
        {
            case LogEventLevel.Verbose:
            case LogEventLevel.Debug:
                Log.Debug(ex, "{Message}", message);
                break;
            case LogEventLevel.Information:
                Log.Information(ex, "{Message}", message);
                break;
            case LogEventLevel.Warning:
                Log.Warning(ex, "{Message}", message);
                break;
            case LogEventLevel.Error:
            case LogEventLevel.Fatal:
                Log.Error(ex, "{Message}", message);
                break;
        }
    }

    public static IReadOnlyList<LogMessageEventArgs> GetRecentLogs() => RecentLogs.ToArray();
}
