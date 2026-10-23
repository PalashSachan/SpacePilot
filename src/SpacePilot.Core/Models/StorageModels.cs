using SpacePilot.Core.Enums;
using SpacePilot.Core.Utilities;

namespace SpacePilot.Core.Models;

public class StorageProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "DigitalOcean Spaces (BLR1)";
    public StorageProviderType ProviderType { get; set; } = StorageProviderType.DigitalOceanSpaces;
    public string ServiceEndpoint { get; set; } = "https://blr1.digitaloceanspaces.com";
    public string Region { get; set; } = "blr1";
    public string AccessKeyId { get; set; } = string.Empty;
    public string SecretAccessKey { get; set; } = string.Empty; // Protected using DPAPI
    public string BucketName { get; set; } = "docs456";
    public bool ForcePathStyle { get; set; } = false;
    public bool UseSsl { get; set; } = true;
    public int TimeoutSeconds { get; set; } = 30;
    public int RetryCount { get; set; } = 3;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastConnectedAt { get; set; }

    public StorageProfile Clone()
    {
        return (StorageProfile)MemberwiseClone();
    }
}

public class IndexedObject
{
    public long Id { get; set; }
    public string BucketName { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Prefix { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string ETag { get; set; } = string.Empty;
    public DateTime? LastModified { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public string StorageClass { get; set; } = "STANDARD";
    public bool IsFolder { get; set; }
    public FileCategory Category { get; set; } = FileCategory.Other;
    public DateTime ScannedAt { get; set; } = DateTime.UtcNow;
}

public class FolderSummary
{
    public string Prefix { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long RecursiveSizeBytes { get; set; }
    public long ImmediateSizeBytes { get; set; }
    public int RecursiveObjectCount { get; set; }
    public int ImmediateObjectCount { get; set; }
    public int SubfolderCount { get; set; }
    public string LargestObjectName { get; set; } = string.Empty;
    public long LargestObjectSizeBytes { get; set; }
    public double PercentageOfBucket { get; set; }
    public DateTime? LastModified { get; set; }
}

public class DuplicateCandidate
{
    public long SizeBytes { get; set; }
    public string ETagOrHash { get; set; } = string.Empty;
    public int FileCount { get; set; }
    public long WastedSizeBytes => (FileCount - 1) * SizeBytes;
    public List<IndexedObject> Objects { get; set; } = new();
}

public class ApplicationSettings
{
    public long MinimumImageSizeKb { get; set; } = 200; // 200 KB default
    public long MinimumPdfSizeKb { get; set; } = 300;   // 300 KB default
    public double MinimumSavingsPercentage { get; set; } = 15.0; // 15% minimum savings
    public int ParallelWorkers { get; set; } = 4;
    public AppTheme Theme { get; set; } = AppTheme.Dark;
    public string DefaultStagingPrefix { get; set; } = "__spacepilot_compressed/";
    public string DefaultTrashPrefix { get; set; } = "__spacepilot_trash/";
    public bool EnableVersioningWarnings { get; set; } = true;
    public bool AutoCheckUpdates { get; set; } = false;
    public Guid? ActiveProfileId { get; set; }
}

public class CompressionProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Lossless First";
    public CompressionMode Mode { get; set; } = CompressionMode.LosslessFirst;
    public int Quality { get; set; } = 85; // 1-100
    public TargetImageFormat TargetFormat { get; set; } = TargetImageFormat.Original;
    public ResizeMode ResizeMode { get; set; } = ResizeMode.None;
    public int MaxWidth { get; set; } = 1920;
    public int MaxHeight { get; set; } = 1080;
    public double ResizePercentage { get; set; } = 80.0;
    public int LongestEdge { get; set; } = 1600;
    public bool PreserveAspectRatio { get; set; } = true;
    public MetadataPolicy MetadataPolicy { get; set; } = MetadataPolicy.PreserveAll;
    public bool AutoOrientExif { get; set; } = true;
    public long? TargetFileSizeBytes { get; set; }

    // PDF options
    public bool PdfRecompressImages { get; set; } = false; // Off by default (safe lossless)
    public int PdfImageDpi { get; set; } = 150;
    public int PdfImageQuality { get; set; } = 80;

    public static CompressionProfile CreateLossless() => new()
    {
        Name = "Lossless First",
        Mode = CompressionMode.LosslessFirst,
        Quality = 100,
        ResizeMode = ResizeMode.None,
        MetadataPolicy = MetadataPolicy.PreserveAll,
        PdfRecompressImages = false
    };

    public static CompressionProfile CreateVisuallyOptimized() => new()
    {
        Name = "Visually Optimized",
        Mode = CompressionMode.VisuallyOptimized,
        Quality = 82,
        ResizeMode = ResizeMode.None,
        MetadataPolicy = MetadataPolicy.StripNonEssential,
        PdfRecompressImages = false
    };

    public static CompressionProfile CreateMaximumSavings() => new()
    {
        Name = "Maximum Savings",
        Mode = CompressionMode.MaximumSavings,
        Quality = 65,
        ResizeMode = ResizeMode.LongestEdge,
        LongestEdge = 1920,
        MetadataPolicy = MetadataPolicy.StripAll,
        PdfRecompressImages = true,
        PdfImageDpi = 120,
        PdfImageQuality = 65
    };
}

public class CompressionJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string SourceType { get; set; } = "Local"; // Local or S3
    public string SourcePathOrBucket { get; set; } = string.Empty;
    public string TargetPrefixOrDirectory { get; set; } = string.Empty;
    public JobStatus Status { get; set; } = JobStatus.Queued;
    public UploadMode UploadMode { get; set; } = UploadMode.DryRun;
    public int TotalFiles { get; set; }
    public int ProcessedFiles { get; set; }
    public int SkippedFiles { get; set; }
    public int FailedFiles { get; set; }
    public long OriginalBytes { get; set; }
    public long ProcessedBytes { get; set; }
    public long SavedBytes => Math.Max(0, OriginalBytes - ProcessedBytes);
    public double SavingsPercentage => ByteSizeFormatter.CalculateSavingsPercentage(OriginalBytes, ProcessedBytes);
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
}

public class JobItem
{
    public long Id { get; set; }
    public Guid JobId { get; set; }
    public string SourceKeyOrPath { get; set; } = string.Empty;
    public string DestinationKeyOrPath { get; set; } = string.Empty;
    public FileCategory FileType { get; set; } = FileCategory.Other;
    public JobItemStatus Status { get; set; } = JobItemStatus.Queued;
    public long OriginalSizeBytes { get; set; }
    public long ProcessedSizeBytes { get; set; }
    public long SavedSizeBytes => Math.Max(0, OriginalSizeBytes - ProcessedSizeBytes);
    public double SavedPercentage => ByteSizeFormatter.CalculateSavingsPercentage(OriginalSizeBytes, ProcessedSizeBytes);
    public string? ErrorDetails { get; set; }
    public DateTime? CompletedAt { get; set; }
    public bool IsLossy { get; set; }
}

public class ProcessedFile
{
    public long Id { get; set; }
    public string SourceIdentifier { get; set; } = string.Empty; // S3 Key or Local FullPath
    public string SourceHashOrETag { get; set; } = string.Empty;
    public long OriginalSizeBytes { get; set; }
    public long ProcessedSizeBytes { get; set; }
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
    public string ProfileName { get; set; } = string.Empty;
}

public class ActivityLog
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string SourceBucketOrPath { get; set; } = string.Empty;
    public string ObjectKeyOrFileName { get; set; } = string.Empty;
    public FileCategory FileType { get; set; } = FileCategory.Other;
    public long OriginalSizeBytes { get; set; }
    public long ProcessedSizeBytes { get; set; }
    public long SavedBytes => Math.Max(0, OriginalSizeBytes - ProcessedSizeBytes);
    public double SavedPercentage => ByteSizeFormatter.CalculateSavingsPercentage(OriginalSizeBytes, ProcessedSizeBytes);
    public string Action { get; set; } = string.Empty; // Scanned, Compressed, Uploaded, Deleted, Staged
    public string Status { get; set; } = string.Empty; // Success, Failed, Skipped, CheckedNoSavings
    public string? ErrorDetails { get; set; }
}

public class CompressionResult
{
    public bool Success { get; set; }
    public string SourcePath { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public long OriginalSizeBytes { get; set; }
    public long ProcessedSizeBytes { get; set; }
    public long SavedSizeBytes => Math.Max(0, OriginalSizeBytes - ProcessedSizeBytes);
    public double SavingsPercentage => ByteSizeFormatter.CalculateSavingsPercentage(OriginalSizeBytes, ProcessedSizeBytes);
    public bool MeetsThreshold { get; set; }
    public bool IsLossy { get; set; }
    public int? OriginalWidth { get; set; }
    public int? OriginalHeight { get; set; }
    public int? ProcessedWidth { get; set; }
    public int? ProcessedHeight { get; set; }
    public int? PageCount { get; set; }
    public string? ErrorMessage { get; set; }
    public byte[]? PreviewBytes { get; set; }
}

public class ValidationResult
{
    public bool IsValid { get; set; }
    public string? ErrorMessage { get; set; }
    public int PageCount { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string Format { get; set; } = string.Empty;
}
