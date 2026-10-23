using System.Runtime.InteropServices;
using System.Text;
using SpacePilot.Core.Interfaces;
using SpacePilot.Infrastructure.Logging;

namespace SpacePilot.Infrastructure.Diagnostics;

public class DiagnosticService : IDiagnosticService
{
    private readonly ISettingsService _settingsService;

    public DiagnosticService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public string GetLogsDirectoryPath()
    {
        return LoggingService.LogsDirectory;
    }

    public void ClearOldLogs(int daysToKeep = 14)
    {
        string dir = GetLogsDirectoryPath();
        if (!Directory.Exists(dir)) return;

        var cutoff = DateTime.UtcNow.AddDays(-daysToKeep);
        foreach (var file in Directory.GetFiles(dir, "spacepilot-*.log"))
        {
            try
            {
                var fi = new FileInfo(file);
                if (fi.LastWriteTimeUtc < cutoff)
                {
                    fi.Delete();
                }
            }
            catch { }
        }
    }

    public async Task<string> ExportDiagnosticReportAsync(CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# SpacePilot — Diagnostic Report");
        sb.AppendLine($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine();

        // 1. System Environment
        sb.AppendLine("## System Information");
        sb.AppendLine($"- OS: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
        sb.AppendLine($"- Framework: {RuntimeInformation.FrameworkDescription}");
        sb.AppendLine($"- Process Architecture: {RuntimeInformation.ProcessArchitecture}");
        sb.AppendLine($"- Machine Name: {Environment.MachineName}");
        sb.AppendLine($"- Processor Count: {Environment.ProcessorCount}");
        sb.AppendLine($"- Working Set: {Environment.WorkingSet / (1024 * 1024)} MB");
        sb.AppendLine();

        // 2. Database Information
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string dbPath = Path.Combine(appData, "SpacePilot", "spacepilot.db");
        sb.AppendLine("## Database Information");
        if (File.Exists(dbPath))
        {
            var dbFi = new FileInfo(dbPath);
            sb.AppendLine($"- Database File: `{dbPath}`");
            sb.AppendLine($"- Database Size: {dbFi.Length / 1024.0 / 1024.0:F2} MB");
            sb.AppendLine($"- Last Modified: {dbFi.LastWriteTimeUtc:yyyy-MM-dd HH:mm:ss} UTC");
        }
        else
        {
            sb.AppendLine("- Database File: Not created yet.");
        }
        sb.AppendLine();

        // 3. Storage Profiles (Redacted)
        sb.AppendLine("## Configured Storage Profiles (Credentials Redacted)");
        var profiles = await _settingsService.GetProfilesAsync(ct);
        foreach (var p in profiles)
        {
            sb.AppendLine($"### Profile: {p.Name}");
            sb.AppendLine($"- Provider: {p.ProviderType}");
            sb.AppendLine($"- Endpoint: `{p.ServiceEndpoint}`");
            sb.AppendLine($"- Region: `{p.Region}`");
            sb.AppendLine($"- Bucket: `{p.BucketName}`");
            sb.AppendLine($"- Access Key ID: `{MaskString(p.AccessKeyId)}`");
            sb.AppendLine($"- Secret Key: [REDACTED]");
            sb.AppendLine($"- Force Path Style: {p.ForcePathStyle}");
            sb.AppendLine($"- Timeout: {p.TimeoutSeconds}s, Retries: {p.RetryCount}");
            sb.AppendLine($"- Last Connected: {p.LastConnectedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Never"}");
            sb.AppendLine();
        }

        // 4. Recent Logs
        sb.AppendLine("## Recent Log Entries");
        var recentLogs = LoggingService.GetRecentLogs().TakeLast(50);
        foreach (var log in recentLogs)
        {
            sb.AppendLine($"[{log.Timestamp:HH:mm:ss}] [{log.Level}] {log.Message}");
            if (!string.IsNullOrEmpty(log.Exception))
            {
                sb.AppendLine($"    {log.Exception}");
            }
        }

        return sb.ToString();
    }

    private static string MaskString(string str)
    {
        if (string.IsNullOrEmpty(str)) return "[EMPTY]";
        if (str.Length <= 4) return "****";
        return str.Substring(0, 3) + "****" + str.Substring(str.Length - 2);
    }
}
