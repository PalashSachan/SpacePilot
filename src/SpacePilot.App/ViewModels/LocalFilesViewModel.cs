using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SpacePilot.Core.Enums;
using SpacePilot.Core.Interfaces;
using SpacePilot.Core.Models;
using SpacePilot.Core.Utilities;

namespace SpacePilot.App.ViewModels;

public partial class LocalFilesViewModel : ObservableObject
{
    private readonly ILocalFolderService _localFolderService;
    private readonly IJobManager _jobManager;

    [ObservableProperty]
    private string _selectedFolderPath = string.Empty;

    [ObservableProperty]
    private bool _includeSubfolders = true;

    [ObservableProperty]
    private bool _useCustomDestination;

    [ObservableProperty]
    private string _customDestinationPath = string.Empty;

    [ObservableProperty]
    private CollisionPolicy _collisionPolicy = CollisionPolicy.Skip;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private bool _isProcessing;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private int _discoveredCount;

    [ObservableProperty]
    private string _totalSizeFormatted = "0 B";

    public ObservableCollection<string> DiscoveredFiles { get; } = new();

    public LocalFilesViewModel(
        ILocalFolderService localFolderService,
        IJobManager jobManager)
    {
        _localFolderService = localFolderService;
        _jobManager = jobManager;
    }

    [RelayCommand]
    public async Task ScanFolderAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedFolderPath) || !Directory.Exists(SelectedFolderPath))
        {
            StatusMessage = "Please select a valid folder path.";
            return;
        }

        IsScanning = true;
        StatusMessage = "Scanning folder for images and PDFs...";
        DiscoveredFiles.Clear();

        try
        {
            var files = await _localFolderService.DiscoverFilesAsync(SelectedFolderPath, IncludeSubfolders);
            long totalBytes = 0;
            foreach (var f in files)
            {
                DiscoveredFiles.Add(f);
                try { totalBytes += new FileInfo(f).Length; } catch { }
            }

            DiscoveredCount = files.Count;
            TotalSizeFormatted = ByteSizeFormatter.Format(totalBytes);
            StatusMessage = $"Found {DiscoveredCount} eligible media files ({TotalSizeFormatted}).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Scan error: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    public async Task StartCompressionAsync()
    {
        if (DiscoveredFiles.Count == 0)
        {
            StatusMessage = "No files found to compress. Please scan a folder first.";
            return;
        }

        IsProcessing = true;
        StatusMessage = "Preparing compression job...";

        try
        {
            var profile = CompressionProfile.CreateVisuallyOptimized();
            string? destDir = UseCustomDestination && !string.IsNullOrWhiteSpace(CustomDestinationPath)
                ? CustomDestinationPath
                : null;

            var job = await _localFolderService.PrepareLocalCompressionJobAsync(
                DiscoveredFiles,
                profile,
                destDir,
                CollisionPolicy);

            var items = DiscoveredFiles.Select(f => new JobItem
            {
                JobId = job.Id,
                SourceKeyOrPath = f,
                DestinationKeyOrPath = destDir != null ? Path.Combine(destDir, Path.GetFileName(f)) : PathUtilities.GetLocalCompressedSiblingPath(f),
                FileType = PathUtilities.CategorizeFile(f),
                Status = JobItemStatus.Queued,
                OriginalSizeBytes = File.Exists(f) ? new FileInfo(f).Length : 0,
                IsLossy = false
            }).ToList();

            await _jobManager.CreateJobAsync(job, items);
            await _jobManager.StartJobAsync(job.Id);

            StatusMessage = $"Job '{job.Title}' queued and started! Results will be saved to '{job.TargetPrefixOrDirectory}'.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to start job: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
        }
    }
}
