using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SpacePilot.Core.Enums;
using SpacePilot.Core.Interfaces;
using SpacePilot.Core.Models;
using SpacePilot.Core.Utilities;
using ResizeMode = SpacePilot.Core.Enums.ResizeMode;

namespace SpacePilot.App.ViewModels;

public class QueueItem : ObservableObject
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName => Path.GetFileName(FilePath);
    public string Extension => Path.GetExtension(FilePath);
    public long OriginalSizeBytes { get; set; }
    public FileCategory Category { get; set; }

    private long _processedSizeBytes;
    public long ProcessedSizeBytes
    {
        get => _processedSizeBytes;
        set
        {
            if (SetProperty(ref _processedSizeBytes, value))
            {
                OnPropertyChanged(nameof(SavedSizeBytes));
                OnPropertyChanged(nameof(SavingsPercentage));
            }
        }
    }

    public long SavedSizeBytes => Math.Max(0, OriginalSizeBytes - ProcessedSizeBytes);
    public double SavingsPercentage => ByteSizeFormatter.CalculateSavingsPercentage(OriginalSizeBytes, ProcessedSizeBytes);

    private JobItemStatus _status = JobItemStatus.Queued;
    public JobItemStatus Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    private string? _statusDetails;
    public string? StatusDetails
    {
        get => _statusDetails;
        set => SetProperty(ref _statusDetails, value);
    }

    private bool _isLossy;
    public bool IsLossy
    {
        get => _isLossy;
        set => SetProperty(ref _isLossy, value);
    }
}

public partial class CompressorViewModel : ObservableObject
{
    private readonly IUnifiedCompressionEngine _engine;
    private readonly IJobManager _jobManager;
    private readonly ISettingsService _settingsService;

    [ObservableProperty]
    private CompressionMode _selectedMode = CompressionMode.LosslessFirst;

    [ObservableProperty]
    private int _quality = 85;

    [ObservableProperty]
    private TargetImageFormat _targetFormat = TargetImageFormat.Original;

    [ObservableProperty]
    private ResizeMode _resizeMode = ResizeMode.None;

    [ObservableProperty]
    private int _maxWidth = 1920;

    [ObservableProperty]
    private int _maxHeight = 1080;

    [ObservableProperty]
    private double _resizePercentage = 80.0;

    [ObservableProperty]
    private int _longestEdge = 1600;

    [ObservableProperty]
    private bool _preserveAspectRatio = true;

    [ObservableProperty]
    private MetadataPolicy _metadataPolicy = MetadataPolicy.PreserveAll;

    [ObservableProperty]
    private bool _autoOrientExif = true;

    [ObservableProperty]
    private bool _isProcessing;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private QueueItem? _selectedItem;

    // Side-by-side Preview Properties
    [ObservableProperty]
    private BitmapImage? _originalImagePreview;

    [ObservableProperty]
    private BitmapImage? _processedImagePreview;

    [ObservableProperty]
    private string _previewOriginalDimensions = "-";

    [ObservableProperty]
    private string _previewProcessedDimensions = "-";

    [ObservableProperty]
    private string _previewOriginalSize = "-";

    [ObservableProperty]
    private string _previewProcessedSize = "-";

    [ObservableProperty]
    private string _previewSavings = "-";

    [ObservableProperty]
    private bool _previewIsLossy;

    [ObservableProperty]
    private bool _hasPreview;

    public ObservableCollection<QueueItem> Queue { get; } = new();

    public CompressorViewModel(
        IUnifiedCompressionEngine engine,
        IJobManager jobManager,
        ISettingsService settingsService)
    {
        _engine = engine;
        _jobManager = jobManager;
        _settingsService = settingsService;
    }

    partial void OnSelectedModeChanged(CompressionMode value)
    {
        switch (value)
        {
            case CompressionMode.LosslessFirst:
                Quality = 100;
                ResizeMode = ResizeMode.None;
                MetadataPolicy = MetadataPolicy.PreserveAll;
                break;
            case CompressionMode.VisuallyOptimized:
                Quality = 82;
                ResizeMode = ResizeMode.None;
                MetadataPolicy = MetadataPolicy.StripNonEssential;
                break;
            case CompressionMode.MaximumSavings:
                Quality = 65;
                ResizeMode = ResizeMode.LongestEdge;
                LongestEdge = 1920;
                MetadataPolicy = MetadataPolicy.StripAll;
                break;
        }
    }

    [RelayCommand]
    public void AddFiles(IEnumerable<string>? paths)
    {
        if (paths == null) return;
        foreach (var path in paths)
        {
            if (File.Exists(path) && PathUtilities.IsSupportedMedia(path))
            {
                var fi = new FileInfo(path);
                Queue.Add(new QueueItem
                {
                    FilePath = path,
                    OriginalSizeBytes = fi.Length,
                    Category = PathUtilities.CategorizeFile(path)
                });
            }
        }
        StatusMessage = $"{Queue.Count} files in queue.";
    }

    [RelayCommand]
    public void ClearQueue()
    {
        Queue.Clear();
        HasPreview = false;
        OriginalImagePreview = null;
        ProcessedImagePreview = null;
        StatusMessage = "Queue cleared.";
    }

    [RelayCommand]
    public async Task TestSingleFileAsync()
    {
        if (SelectedItem == null)
        {
            StatusMessage = "Please select a file from the queue to test.";
            return;
        }

        string tempOut = Path.Combine(Path.GetTempPath(), $"test_preview_{Guid.NewGuid():N}{Path.GetExtension(SelectedItem.FilePath)}");
        StatusMessage = $"Testing compression on '{SelectedItem.FileName}'...";

        var profile = BuildProfile();

        try
        {
            var settings = await _settingsService.LoadSettingsAsync();
            long minImg = settings.MinimumImageSizeKb * 1024;
            long minPdf = settings.MinimumPdfSizeKb * 1024;

            var result = await _engine.ProcessFileAsync(
                SelectedItem.FilePath,
                tempOut,
                profile,
                minImg,
                minPdf,
                settings.MinimumSavingsPercentage);

            if (!result.Success)
            {
                StatusMessage = $"Test failed: {result.ErrorMessage}";
                return;
            }

            // Update preview details
            PreviewOriginalSize = ByteSizeFormatter.Format(result.OriginalSizeBytes);
            PreviewProcessedSize = ByteSizeFormatter.Format(result.ProcessedSizeBytes);
            PreviewSavings = ByteSizeFormatter.FormatSavings(result.OriginalSizeBytes, result.ProcessedSizeBytes);
            PreviewOriginalDimensions = result.OriginalWidth > 0 ? $"{result.OriginalWidth} x {result.OriginalHeight}" : "-";
            PreviewProcessedDimensions = result.ProcessedWidth > 0 ? $"{result.ProcessedWidth} x {result.ProcessedHeight}" : "-";
            PreviewIsLossy = result.IsLossy;

            // Load images for preview if image format
            if (SelectedItem.Category == FileCategory.Image)
            {
                OriginalImagePreview = LoadBitmapSafely(SelectedItem.FilePath);
                ProcessedImagePreview = LoadBitmapSafely(tempOut);
            }
            else
            {
                OriginalImagePreview = null;
                ProcessedImagePreview = null;
            }

            HasPreview = true;
            SelectedItem.ProcessedSizeBytes = result.ProcessedSizeBytes;
            SelectedItem.Status = result.MeetsThreshold ? JobItemStatus.Compressed : JobItemStatus.CheckedNoSavings;
            SelectedItem.StatusDetails = result.ErrorMessage ?? "Test completed successfully.";

            StatusMessage = $"Test completed! Saved {ByteSizeFormatter.Format(result.SavedSizeBytes)} ({result.SavingsPercentage:0.0}%).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Test error: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task StartBulkCompressionAsync(bool isDryRun = false)
    {
        if (Queue.Count == 0)
        {
            StatusMessage = "Queue is empty. Please add files first.";
            return;
        }

        IsProcessing = true;
        StatusMessage = "Starting bulk compression job...";

        var profile = BuildProfile();
        var job = new CompressionJob
        {
            Title = $"Bulk Studio Job ({Queue.Count} files)",
            SourceType = "Local",
            SourcePathOrBucket = "Bulk Studio",
            TargetPrefixOrDirectory = "compressed/",
            Status = JobStatus.Queued,
            UploadMode = isDryRun ? UploadMode.DryRun : UploadMode.ReplaceOriginal,
            TotalFiles = Queue.Count,
            OriginalBytes = Queue.Sum(q => q.OriginalSizeBytes)
        };

        var items = Queue.Select(q => new JobItem
        {
            JobId = job.Id,
            SourceKeyOrPath = q.FilePath,
            DestinationKeyOrPath = PathUtilities.GetLocalCompressedSiblingPath(q.FilePath),
            FileType = q.Category,
            Status = JobItemStatus.Queued,
            OriginalSizeBytes = q.OriginalSizeBytes,
            IsLossy = profile.Mode != CompressionMode.LosslessFirst || profile.ResizeMode != ResizeMode.None
        }).ToList();

        try
        {
            await _jobManager.CreateJobAsync(job, items);
            await _jobManager.StartJobAsync(job.Id);
            StatusMessage = $"Job '{job.Title}' started in background. View progress in Jobs tab.";
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

    private CompressionProfile BuildProfile()
    {
        return new CompressionProfile
        {
            Mode = SelectedMode,
            Quality = Quality,
            TargetFormat = TargetFormat,
            ResizeMode = ResizeMode,
            MaxWidth = MaxWidth,
            MaxHeight = MaxHeight,
            ResizePercentage = ResizePercentage,
            LongestEdge = LongestEdge,
            PreserveAspectRatio = PreserveAspectRatio,
            MetadataPolicy = MetadataPolicy,
            AutoOrientExif = AutoOrientExif
        };
    }

    private static BitmapImage? LoadBitmapSafely(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }
}
