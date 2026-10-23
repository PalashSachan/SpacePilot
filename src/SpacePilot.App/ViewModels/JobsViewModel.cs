using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SpacePilot.Core.Enums;
using SpacePilot.Core.Interfaces;
using SpacePilot.Core.Models;
using SpacePilot.Infrastructure.Logging;

namespace SpacePilot.App.ViewModels;

public partial class JobsViewModel : ObservableObject
{
    private readonly IJobManager _jobManager;
    private readonly IDiagnosticService _diagnosticService;

    [ObservableProperty]
    private CompressionJob? _selectedJob;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _filterSearch = string.Empty;

    [ObservableProperty]
    private string _filterStatus = "All";

    [ObservableProperty]
    private string _filterType = "All";

    public ObservableCollection<CompressionJob> Jobs { get; } = new();
    public ObservableCollection<JobItem> JobItems { get; } = new();
    public ObservableCollection<ActivityLog> ActivityLogs { get; } = new();
    public ObservableCollection<string> LogLines { get; } = new();

    public JobsViewModel(
        IJobManager jobManager,
        IDiagnosticService diagnosticService)
    {
        _jobManager = jobManager;
        _diagnosticService = diagnosticService;

        _jobManager.ProgressUpdated += (s, e) =>
        {
            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
            {
                var existing = Jobs.FirstOrDefault(j => j.Id == e.JobId);
                if (existing != null)
                {
                    existing.ProcessedFiles = e.Processed;
                    existing.OriginalBytes = e.OriginalBytes;
                    existing.ProcessedBytes = e.ProcessedBytes;
                    existing.Status = e.Status;
                }
            });
        };

        LoggingService.OnLogMessage += (s, e) =>
        {
            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
            {
                LogLines.Insert(0, $"[{e.Timestamp:HH:mm:ss}] [{e.Level}] {e.Message}");
                if (LogLines.Count > 500) LogLines.RemoveAt(LogLines.Count - 1);
            });
        };

        // Load recent in-memory logs
        foreach (var log in LoggingService.GetRecentLogs().TakeLast(50))
        {
            LogLines.Insert(0, $"[{log.Timestamp:HH:mm:ss}] [{log.Level}] {log.Message}");
        }
    }

    [RelayCommand]
    public async Task RefreshJobsAsync()
    {
        IsLoading = true;
        try
        {
            var jobs = await _jobManager.GetAllJobsAsync();
            Jobs.Clear();
            foreach (var j in jobs) Jobs.Add(j);

            var logs = await _jobManager.GetActivityLogsAsync(200);
            ActivityLogs.Clear();
            foreach (var l in logs)
            {
                if (MatchesFilter(l))
                {
                    ActivityLogs.Add(l);
                }
            }

            if (SelectedJob != null)
            {
                await LoadJobItemsAsync(SelectedJob.Id);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task SelectJobAsync(CompressionJob? job)
    {
        SelectedJob = job;
        if (job != null)
        {
            await LoadJobItemsAsync(job.Id);
        }
    }

    private async Task LoadJobItemsAsync(Guid jobId)
    {
        var items = await _jobManager.GetJobItemsAsync(jobId);
        JobItems.Clear();
        foreach (var item in items) JobItems.Add(item);
    }

    [RelayCommand]
    public void PauseJob(CompressionJob? job)
    {
        if (job != null) _jobManager.PauseJob(job.Id);
    }

    [RelayCommand]
    public void ResumeJob(CompressionJob? job)
    {
        if (job != null) _jobManager.ResumeJob(job.Id);
    }

    [RelayCommand]
    public void CancelJob(CompressionJob? job)
    {
        if (job != null) _jobManager.CancelJob(job.Id);
    }

    [RelayCommand]
    public async Task RetryFailedAsync(CompressionJob? job)
    {
        if (job != null)
        {
            await _jobManager.RetryFailedItemsAsync(job.Id);
            await RefreshJobsAsync();
        }
    }

    [RelayCommand]
    public void OpenLogsFolder()
    {
        string dir = _diagnosticService.GetLogsDirectoryPath();
        if (Directory.Exists(dir))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
        }
    }

    [RelayCommand]
    public async Task ExportDiagnosticReportAsync()
    {
        string report = await _diagnosticService.ExportDiagnosticReportAsync();
        string tempFile = Path.Combine(Path.GetTempPath(), $"SpacePilot_Diagnostic_{DateTime.UtcNow:yyyyMMdd_HHmmss}.md");
        await File.WriteAllTextAsync(tempFile, report);

        Process.Start(new ProcessStartInfo(tempFile) { UseShellExecute = true });
    }

    [RelayCommand]
    public void ClearOldLogs()
    {
        _diagnosticService.ClearOldLogs(14);
        MessageBox.Show("Archived logs older than 14 days have been cleaned up.", "Logs Cleaned", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private bool MatchesFilter(ActivityLog log)
    {
        if (!string.IsNullOrWhiteSpace(FilterSearch))
        {
            if (!log.ObjectKeyOrFileName.Contains(FilterSearch, StringComparison.OrdinalIgnoreCase) &&
                !log.SourceBucketOrPath.Contains(FilterSearch, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (FilterStatus != "All" && !log.Status.Equals(FilterStatus, StringComparison.OrdinalIgnoreCase))
            return false;

        if (FilterType == "Images" && log.FileType != FileCategory.Image) return false;
        if (FilterType == "PDFs" && log.FileType != FileCategory.Pdf) return false;

        return true;
    }
}
