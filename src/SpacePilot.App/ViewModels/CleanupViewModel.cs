using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SpacePilot.Core.Interfaces;
using SpacePilot.Core.Models;
using SpacePilot.Core.Utilities;

namespace SpacePilot.App.ViewModels;

public partial class CleanupViewModel : ObservableObject
{
    private readonly IStorageIndexer _indexer;
    private readonly IS3Service _s3Service;
    private readonly ISettingsService _settingsService;

    [ObservableProperty]
    private string _activeBucket = "docs456";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private string _potentialWastedStorage = "0 B";

    // Safe Deletion Confirmation Dialog State
    [ObservableProperty]
    private bool _showDeleteDialog;

    [ObservableProperty]
    private string _deleteConfirmationInput = string.Empty;

    [ObservableProperty]
    private int _itemsToDeleteCount;

    [ObservableProperty]
    private string _bytesToDeleteFormatted = "0 B";

    private List<IndexedObject> _pendingDeleteObjects = new();

    public ObservableCollection<FolderSummary> LargestFolders { get; } = new();
    public ObservableCollection<IndexedObject> LargestFiles { get; } = new();
    public ObservableCollection<DuplicateCandidate> Duplicates { get; } = new();

    public CleanupViewModel(
        IStorageIndexer indexer,
        IS3Service s3Service,
        ISettingsService settingsService)
    {
        _indexer = indexer;
        _s3Service = s3Service;
        _settingsService = settingsService;
    }

    [RelayCommand]
    public async Task RunAnalysisAsync()
    {
        IsLoading = true;
        StatusMessage = "Analyzing storage usage and finding largest items and duplicate candidates...";

        try
        {
            var profiles = await _settingsService.GetProfilesAsync();
            var activeProfile = profiles.FirstOrDefault();
            if (activeProfile != null)
            {
                ActiveBucket = activeProfile.BucketName;
            }

            // 1. Largest folders
            var folders = await _indexer.GetLargestFoldersAsync(ActiveBucket, 20);
            LargestFolders.Clear();
            foreach (var f in folders) LargestFolders.Add(f);

            // 2. Largest files
            var files = await _indexer.GetLargestFilesAsync(ActiveBucket, 50);
            LargestFiles.Clear();
            foreach (var fi in files) LargestFiles.Add(fi);

            // 3. Duplicates
            var dups = await _indexer.FindDuplicateCandidatesAsync(ActiveBucket, 2);
            Duplicates.Clear();
            long wasted = 0;
            foreach (var d in dups)
            {
                Duplicates.Add(d);
                wasted += d.WastedSizeBytes;
            }

            PotentialWastedStorage = ByteSizeFormatter.Format(wasted);
            StatusMessage = $"Analysis complete: {LargestFolders.Count} largest folders, {LargestFiles.Count} largest files, {Duplicates.Count} duplicate groups.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Analysis failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public void PrepareDelete(IEnumerable<IndexedObject>? objects)
    {
        if (objects == null) return;
        _pendingDeleteObjects = objects.ToList();
        if (_pendingDeleteObjects.Count == 0) return;

        ItemsToDeleteCount = _pendingDeleteObjects.Count;
        long totalBytes = _pendingDeleteObjects.Sum(o => o.SizeBytes);
        BytesToDeleteFormatted = ByteSizeFormatter.Format(totalBytes);
        DeleteConfirmationInput = string.Empty;
        ShowDeleteDialog = true;
    }

    [RelayCommand]
    public async Task ConfirmDeleteAsync()
    {
        if (!DeleteConfirmationInput.Trim().Equals(ActiveBucket.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show($"Confirmation failed: Please type '{ActiveBucket}' exactly to confirm permanent deletion.", "Confirmation Required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ShowDeleteDialog = false;
        StatusMessage = $"Deleting {ItemsToDeleteCount} objects from '{ActiveBucket}'...";

        try
        {
            var profiles = await _settingsService.GetProfilesAsync();
            var profile = profiles.FirstOrDefault(p => p.BucketName.Equals(ActiveBucket, StringComparison.OrdinalIgnoreCase))
                          ?? profiles.FirstOrDefault();

            if (profile != null)
            {
                var keys = _pendingDeleteObjects.Select(o => o.Key).ToList();
                int deleted = await _s3Service.DeleteObjectsAsync(profile, keys);
                await _indexer.RemoveObjectsAsync(ActiveBucket, keys);

                StatusMessage = $"Successfully deleted {deleted} objects ({BytesToDeleteFormatted} reclaimed).";
                await RunAnalysisAsync();
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Delete error: {ex.Message}";
        }
    }

    [RelayCommand]
    public void CancelDelete()
    {
        ShowDeleteDialog = false;
        _pendingDeleteObjects.Clear();
    }
}
