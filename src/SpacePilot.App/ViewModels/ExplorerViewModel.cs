using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SpacePilot.Core.Interfaces;
using SpacePilot.Core.Models;
using SpacePilot.Core.Utilities;

namespace SpacePilot.App.ViewModels;

public record BreadcrumbItem(string Name, string Prefix);

public partial class ExplorerViewModel : ObservableObject
{
    private readonly IS3Service _s3Service;
    private readonly IStorageIndexer _indexer;
    private readonly ISettingsService _settingsService;
    private CancellationTokenSource? _scanCts;

    [ObservableProperty]
    private StorageProfile? _selectedProfile;

    [ObservableProperty]
    private string _currentPrefix = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private int _scannedPages;

    [ObservableProperty]
    private int _scannedObjects;

    [ObservableProperty]
    private string _scannedBytesFormatted = "0 B";

    [ObservableProperty]
    private string _searchTerm = string.Empty;

    [ObservableProperty]
    private string _selectedFilter = "All";

    [ObservableProperty]
    private IndexedObject? _selectedObject;

    [ObservableProperty]
    private FolderSummary? _selectedFolder;

    [ObservableProperty]
    private bool _showMetadataDrawer;

    [ObservableProperty]
    private IDictionary<string, string> _currentObjectMetadata = new Dictionary<string, string>();

    public ObservableCollection<StorageProfile> Profiles { get; } = new();
    public ObservableCollection<string> Buckets { get; } = new();
    public ObservableCollection<BreadcrumbItem> Breadcrumbs { get; } = new();
    public ObservableCollection<FolderSummary> Folders { get; } = new();
    public ObservableCollection<IndexedObject> Files { get; } = new();

    public ExplorerViewModel(
        IS3Service s3Service,
        IStorageIndexer indexer,
        ISettingsService settingsService)
    {
        _s3Service = s3Service;
        _indexer = indexer;
        _settingsService = settingsService;
    }

    public async Task InitializeAsync()
    {
        var profiles = await _settingsService.GetProfilesAsync();
        Profiles.Clear();
        foreach (var p in profiles) Profiles.Add(p);

        SelectedProfile = Profiles.FirstOrDefault();
        if (SelectedProfile != null)
        {
            await LoadBucketsAsync();
            await NavigateToPrefixAsync(string.Empty);
        }
    }

    public async Task LoadBucketsAsync()
    {
        if (SelectedProfile == null) return;
        Buckets.Clear();
        var buckets = await _s3Service.ListBucketsAsync(SelectedProfile);
        foreach (var b in buckets) Buckets.Add(b);

        if (!Buckets.Contains(SelectedProfile.BucketName) && !string.IsNullOrEmpty(SelectedProfile.BucketName))
        {
            Buckets.Add(SelectedProfile.BucketName);
        }
    }

    [RelayCommand]
    public async Task NavigateToPrefixAsync(string prefix)
    {
        CurrentPrefix = PathUtilities.NormalizeS3Prefix(prefix);
        UpdateBreadcrumbs();

        if (SelectedProfile == null) return;

        StatusMessage = $"Loading items under '{CurrentPrefix}'...";

        // Load Folders & Files from SQLite index
        var folders = await _indexer.GetFolderSummariesAsync(SelectedProfile.BucketName, CurrentPrefix);
        Folders.Clear();
        foreach (var f in folders) Folders.Add(f);

        var files = await _indexer.GetObjectsInFolderAsync(SelectedProfile.BucketName, CurrentPrefix);
        Files.Clear();
        foreach (var fi in files)
        {
            if (MatchesFilter(fi))
            {
                Files.Add(fi);
            }
        }

        StatusMessage = $"Showing {Folders.Count} folders, {Files.Count} files.";
    }

    [RelayCommand]
    public async Task OpenFolderAsync(FolderSummary folder)
    {
        if (folder != null)
        {
            await NavigateToPrefixAsync(folder.Prefix);
        }
    }

    [RelayCommand]
    public async Task NavigateUpAsync()
    {
        string parent = PathUtilities.GetParentPrefix(CurrentPrefix);
        await NavigateToPrefixAsync(parent);
    }

    [RelayCommand]
    public async Task StartScanAsync()
    {
        if (SelectedProfile == null) return;

        IsScanning = true;
        ScannedPages = 0;
        ScannedObjects = 0;
        ScannedBytesFormatted = "0 B";
        _scanCts = new CancellationTokenSource();

        var progress = new Progress<ScanProgress>(p =>
        {
            ScannedPages = p.Pages;
            ScannedObjects = p.ObjectsIndexed;
            ScannedBytesFormatted = ByteSizeFormatter.Format(p.TotalBytes);
            StatusMessage = p.StatusMessage ?? "Scanning...";
        });

        try
        {
            await _s3Service.ScanBucketAsync(SelectedProfile, CurrentPrefix, _indexer, progress, _scanCts.Token);
            StatusMessage = $"Scan completed: {ScannedObjects:N0} objects indexed.";
            await NavigateToPrefixAsync(CurrentPrefix);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Scan cancelled by user.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Scan error: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
            _scanCts = null;
        }
    }

    [RelayCommand]
    public void CancelScan()
    {
        _scanCts?.Cancel();
    }

    [RelayCommand]
    public async Task InspectMetadataAsync(IndexedObject? obj)
    {
        if (obj == null || SelectedProfile == null) return;
        SelectedObject = obj;
        ShowMetadataDrawer = true;

        CurrentObjectMetadata = await _s3Service.GetObjectMetadataAsync(SelectedProfile, obj.Key);
    }

    [RelayCommand]
    public async Task DownloadSelectedAsync(IndexedObject? obj)
    {
        if (obj == null || SelectedProfile == null) return;

        string tempPath = Path.Combine(Path.GetTempPath(), "SpacePilotDownloads", obj.FileName);
        StatusMessage = $"Downloading '{obj.FileName}'...";

        try
        {
            await _s3Service.DownloadObjectAsync(SelectedProfile, obj.Key, tempPath);
            StatusMessage = $"Downloaded '{obj.FileName}' successfully.";

            // Open in default Windows viewer
            if (File.Exists(tempPath))
            {
                Process.Start(new ProcessStartInfo(tempPath) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Download failed: {ex.Message}";
        }
    }

    [RelayCommand]
    public void CopyKey(IndexedObject? obj)
    {
        if (obj != null)
        {
            Clipboard.SetText(obj.Key);
            StatusMessage = $"Copied key to clipboard: {obj.Key}";
        }
    }

    [RelayCommand]
    public void CopyUrl(IndexedObject? obj)
    {
        if (obj != null && SelectedProfile != null)
        {
            string endpoint = SelectedProfile.ServiceEndpoint.TrimEnd('/');
            string url = $"{endpoint}/{SelectedProfile.BucketName}/{obj.Key}";
            Clipboard.SetText(url);
            StatusMessage = $"Copied URL to clipboard: {url}";
        }
    }

    [RelayCommand]
    public async Task DeleteObjectAsync(IndexedObject? obj)
    {
        if (obj == null || SelectedProfile == null) return;

        var result = MessageBox.Show(
            $"Are you sure you want to delete '{obj.Key}' ({ByteSizeFormatter.Format(obj.SizeBytes)}) from bucket '{SelectedProfile.BucketName}'?\n\nThis action cannot be undone.",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            try
            {
                await _s3Service.DeleteObjectsAsync(SelectedProfile, new[] { obj.Key });
                await _indexer.RemoveObjectsAsync(SelectedProfile.BucketName, new[] { obj.Key });
                Files.Remove(obj);
                StatusMessage = $"Deleted '{obj.Key}'.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Delete failed: {ex.Message}";
            }
        }
    }

    private void UpdateBreadcrumbs()
    {
        Breadcrumbs.Clear();
        Breadcrumbs.Add(new BreadcrumbItem(SelectedProfile?.BucketName ?? "Root", string.Empty));

        if (!string.IsNullOrEmpty(CurrentPrefix))
        {
            string[] parts = CurrentPrefix.TrimEnd('/').Split('/');
            string accumulated = string.Empty;
            foreach (var part in parts)
            {
                accumulated += part + "/";
                Breadcrumbs.Add(new BreadcrumbItem(part, accumulated));
            }
        }
    }

    private bool MatchesFilter(IndexedObject obj)
    {
        if (!string.IsNullOrWhiteSpace(SearchTerm))
        {
            if (!obj.FileName.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase) &&
                !obj.Key.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (SelectedFilter == "Images" && obj.Category != Core.Enums.FileCategory.Image) return false;
        if (SelectedFilter == "PDFs" && obj.Category != Core.Enums.FileCategory.Pdf) return false;

        return true;
    }
}
