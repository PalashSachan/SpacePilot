using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SpacePilot.Core.Interfaces;
using SpacePilot.Core.Models;
using SpacePilot.Core.Utilities;

namespace SpacePilot.App.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly IStorageIndexer _indexer;
    private readonly ISettingsService _settingsService;
    private readonly IJobManager _jobManager;

    [ObservableProperty]
    private string _activeBucket = "docs456";

    [ObservableProperty]
    private string _profileName = "DigitalOcean Spaces (BLR1)";

    [ObservableProperty]
    private int _totalObjectsCount;

    [ObservableProperty]
    private string _totalStorageFormatted = "0 B";

    [ObservableProperty]
    private string _spaceSavedFormatted = "0 B";

    [ObservableProperty]
    private string _potentialSavingsFormatted = "0 B";

    [ObservableProperty]
    private int _activeJobsCount;

    [ObservableProperty]
    private int _completedJobsCount;

    [ObservableProperty]
    private bool _isLoading;

    public ObservableCollection<FolderSummary> LargestFolders { get; } = new();
    public ObservableCollection<IndexedObject> LargestFiles { get; } = new();
    public ObservableCollection<CompressionJob> RecentJobs { get; } = new();

    public Action<string>? NavigateAction { get; set; }

    public DashboardViewModel(
        IStorageIndexer indexer,
        ISettingsService settingsService,
        IJobManager jobManager,
        Action<string>? navigateAction = null)
    {
        _indexer = indexer;
        _settingsService = settingsService;
        _jobManager = jobManager;
        NavigateAction = navigateAction;
    }

    [RelayCommand]
    public async Task RefreshDashboardAsync()
    {
        IsLoading = true;
        try
        {
            var profiles = await _settingsService.GetProfilesAsync();
            var activeProfile = profiles.FirstOrDefault();
            if (activeProfile != null)
            {
                ProfileName = activeProfile.Name;
                ActiveBucket = activeProfile.BucketName;
            }

            // Storage stats from SQLite index
            TotalObjectsCount = await _indexer.GetTotalIndexedObjectsCountAsync(ActiveBucket);
            long totalBytes = await _indexer.GetTotalIndexedSizeBytesAsync(ActiveBucket);
            TotalStorageFormatted = ByteSizeFormatter.Format(totalBytes);

            // Job stats
            var jobs = await _jobManager.GetAllJobsAsync();
            ActiveJobsCount = jobs.Count(j => j.Status == Core.Enums.JobStatus.Running || j.Status == Core.Enums.JobStatus.Queued);
            CompletedJobsCount = jobs.Count(j => j.Status == Core.Enums.JobStatus.Completed);

            long totalSaved = jobs.Sum(j => j.SavedBytes);
            SpaceSavedFormatted = ByteSizeFormatter.Format(totalSaved);

            long potentialSaved = jobs.Where(j => j.UploadMode == Core.Enums.UploadMode.DryRun).Sum(j => j.SavedBytes);
            PotentialSavingsFormatted = ByteSizeFormatter.Format(potentialSaved);

            RecentJobs.Clear();
            foreach (var j in jobs.Take(5))
            {
                RecentJobs.Add(j);
            }

            // Largest Folders
            var folders = await _indexer.GetLargestFoldersAsync(ActiveBucket, 5);
            LargestFolders.Clear();
            foreach (var f in folders) LargestFolders.Add(f);

            // Largest Files
            var files = await _indexer.GetLargestFilesAsync(ActiveBucket, 5);
            LargestFiles.Clear();
            foreach (var fi in files) LargestFiles.Add(fi);
        }
        catch { }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public void Navigate(string destination) => NavigateAction?.Invoke(destination);
}
