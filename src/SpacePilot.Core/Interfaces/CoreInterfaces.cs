using SpacePilot.Core.Enums;
using SpacePilot.Core.Models;

namespace SpacePilot.Core.Interfaces;

public record ScanProgress(int Pages, int ObjectsIndexed, long TotalBytes, string CurrentPrefix, string? StatusMessage);

public record JobProgressUpdate(Guid JobId, int Processed, int Total, long OriginalBytes, long ProcessedBytes, long SavedBytes, string CurrentItem, JobStatus Status);

public interface IS3Service
{
    Task<bool> TestConnectionAsync(StorageProfile profile, CancellationToken ct = default);
    Task<List<string>> ListBucketsAsync(StorageProfile profile, CancellationToken ct = default);
    Task ScanBucketAsync(
        StorageProfile profile,
        string prefix,
        IStorageIndexer indexer,
        IProgress<ScanProgress>? progress = null,
        CancellationToken ct = default);
    Task DownloadObjectAsync(
        StorageProfile profile,
        string key,
        string destinationFilePath,
        IProgress<double>? progress = null,
        CancellationToken ct = default);
    Task UploadObjectAsync(
        StorageProfile profile,
        string sourceFilePath,
        string key,
        string? contentType = null,
        IDictionary<string, string>? metadata = null,
        CancellationToken ct = default);
    Task CopyObjectAsync(
        StorageProfile profile,
        string sourceKey,
        string destinationKey,
        CancellationToken ct = default);
    Task<int> DeleteObjectsAsync(
        StorageProfile profile,
        IEnumerable<string> keys,
        CancellationToken ct = default);
    Task<IDictionary<string, string>> GetObjectMetadataAsync(
        StorageProfile profile,
        string key,
        CancellationToken ct = default);
    Task<bool> CheckVersioningEnabledAsync(
        StorageProfile profile,
        CancellationToken ct = default);
}

public interface IImageCompressionEngine
{
    Task<CompressionResult> CompressImageAsync(
        string sourcePath,
        string outputPath,
        CompressionProfile profile,
        CancellationToken ct = default);
    Task<ValidationResult> ValidateImageAsync(string filePath, CancellationToken ct = default);
}

public interface IPdfOptimizationEngine
{
    Task<CompressionResult> OptimizePdfAsync(
        string sourcePath,
        string outputPath,
        CompressionProfile profile,
        CancellationToken ct = default);
    Task<ValidationResult> ValidatePdfAsync(string filePath, CancellationToken ct = default);
}

public interface IUnifiedCompressionEngine
{
    Task<CompressionResult> ProcessFileAsync(
        string sourcePath,
        string outputPath,
        CompressionProfile profile,
        long minImageSizeBytes,
        long minPdfSizeBytes,
        double minSavingsPercentage,
        CancellationToken ct = default);
}

public interface IStorageIndexer
{
    Task InitializeAsync(CancellationToken ct = default);
    Task InsertObjectsBatchAsync(IEnumerable<IndexedObject> objects, CancellationToken ct = default);
    Task<List<FolderSummary>> GetFolderSummariesAsync(string bucketName, string parentPrefix, CancellationToken ct = default);
    Task<List<IndexedObject>> GetObjectsInFolderAsync(string bucketName, string prefix, CancellationToken ct = default);
    Task<List<IndexedObject>> GetLargestFilesAsync(string bucketName, int count = 100, CancellationToken ct = default);
    Task<List<FolderSummary>> GetLargestFoldersAsync(string bucketName, int count = 50, CancellationToken ct = default);
    Task<List<DuplicateCandidate>> FindDuplicateCandidatesAsync(string bucketName, int minDuplicateCount = 2, CancellationToken ct = default);
    Task<List<IndexedObject>> SearchObjectsAsync(string bucketName, string searchTerm, string? extension = null, long? minSizeBytes = null, CancellationToken ct = default);
    Task<long> GetTotalIndexedSizeBytesAsync(string bucketName, CancellationToken ct = default);
    Task<int> GetTotalIndexedObjectsCountAsync(string bucketName, CancellationToken ct = default);
    Task RemoveObjectsAsync(string bucketName, IEnumerable<string> keys, CancellationToken ct = default);
    Task ClearBucketIndexAsync(string bucketName, CancellationToken ct = default);
}

public interface IJobManager
{
    Task<CompressionJob> CreateJobAsync(CompressionJob job, IEnumerable<JobItem> items, CancellationToken ct = default);
    Task StartJobAsync(Guid jobId, CancellationToken ct = default);
    void PauseJob(Guid jobId);
    void ResumeJob(Guid jobId);
    void CancelJob(Guid jobId);
    Task RetryFailedItemsAsync(Guid jobId, CancellationToken ct = default);
    Task<List<CompressionJob>> GetAllJobsAsync(CancellationToken ct = default);
    Task<List<JobItem>> GetJobItemsAsync(Guid jobId, CancellationToken ct = default);
    Task<List<ActivityLog>> GetActivityLogsAsync(int take = 500, CancellationToken ct = default);
    Task AddActivityLogAsync(ActivityLog log, CancellationToken ct = default);
    event EventHandler<JobProgressUpdate>? ProgressUpdated;
}

public interface IJobRepository
{
    Task SaveJobAsync(CompressionJob job, IEnumerable<JobItem> items, CancellationToken ct = default);
    Task UpdateJobStatusAsync(Guid jobId, JobStatus status, string? errorMessage = null, CancellationToken ct = default);
    Task UpdateJobProgressAsync(Guid jobId, int processed, int skipped, int failed, long origBytes, long procBytes, CancellationToken ct = default);
    Task UpdateJobItemAsync(JobItem item, CancellationToken ct = default);
    Task<List<CompressionJob>> GetAllJobsAsync(CancellationToken ct = default);
    Task<List<JobItem>> GetJobItemsAsync(Guid jobId, CancellationToken ct = default);
    Task AddActivityLogAsync(ActivityLog log, CancellationToken ct = default);
    Task<List<ActivityLog>> GetActivityLogsAsync(int take = 500, CancellationToken ct = default);
}

public interface ICredentialProtectionService
{
    string ProtectSecret(string plainSecret);
    string UnprotectSecret(string encryptedSecret);
}

public interface ISettingsService
{
    Task<ApplicationSettings> LoadSettingsAsync(CancellationToken ct = default);
    Task SaveSettingsAsync(ApplicationSettings settings, CancellationToken ct = default);
    Task<List<StorageProfile>> GetProfilesAsync(CancellationToken ct = default);
    Task SaveProfileAsync(StorageProfile profile, CancellationToken ct = default);
    Task DeleteProfileAsync(Guid profileId, CancellationToken ct = default);
}

public interface IDiagnosticService
{
    Task<string> ExportDiagnosticReportAsync(CancellationToken ct = default);
    string GetLogsDirectoryPath();
    void ClearOldLogs(int daysToKeep = 14);
}

public interface ILocalFolderService
{
    Task<List<string>> DiscoverFilesAsync(string folderPath, bool recursive, IEnumerable<string>? extensions = null, CancellationToken ct = default);
    Task<CompressionJob> PrepareLocalCompressionJobAsync(
        IEnumerable<string> filePaths,
        CompressionProfile profile,
        string? customDestinationFolder = null,
        CollisionPolicy collisionPolicy = CollisionPolicy.Skip,
        CancellationToken ct = default);
}
