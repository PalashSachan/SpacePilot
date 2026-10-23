using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SpacePilot.Core.Enums;
using SpacePilot.Core.Interfaces;
using SpacePilot.Core.Models;
using SpacePilot.Core.Utilities;

namespace SpacePilot.Application.Jobs;

public class JobManager : IJobManager
{
    private readonly IJobRepository _jobRepository;
    private readonly IUnifiedCompressionEngine _compressionEngine;
    private readonly IS3Service _s3Service;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<JobManager> _logger;

    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _ctsMap = new();
    private readonly ConcurrentDictionary<Guid, ManualResetEventSlim> _pauseEvents = new();

    public event EventHandler<JobProgressUpdate>? ProgressUpdated;

    public JobManager(
        IJobRepository jobRepository,
        IUnifiedCompressionEngine compressionEngine,
        IS3Service s3Service,
        ISettingsService settingsService,
        ILogger<JobManager> logger)
    {
        _jobRepository = jobRepository;
        _compressionEngine = compressionEngine;
        _s3Service = s3Service;
        _settingsService = settingsService;
        _logger = logger;
    }

    public async Task<CompressionJob> CreateJobAsync(CompressionJob job, IEnumerable<JobItem> items, CancellationToken ct = default)
    {
        var itemList = items.ToList();
        job.TotalFiles = itemList.Count;
        job.OriginalBytes = itemList.Sum(i => i.OriginalSizeBytes);
        job.Status = JobStatus.Queued;

        await _jobRepository.SaveJobAsync(job, itemList, ct);
        return job;
    }

    public async Task StartJobAsync(Guid jobId, CancellationToken ct = default)
    {
        var jobs = await _jobRepository.GetAllJobsAsync(ct);
        var job = jobs.FirstOrDefault(j => j.Id == jobId);
        if (job == null) throw new InvalidOperationException($"Job '{jobId}' not found.");

        var items = await _jobRepository.GetJobItemsAsync(jobId, ct);
        var settings = await _settingsService.LoadSettingsAsync(ct);

        var cts = new CancellationTokenSource();
        _ctsMap[jobId] = cts;
        var pauseEvent = new ManualResetEventSlim(true);
        _pauseEvents[jobId] = pauseEvent;

        // Run processing in background task
        _ = Task.Run(async () =>
        {
            await ExecuteJobAsync(job, items, settings, cts.Token, pauseEvent);
        }, CancellationToken.None);
    }

    public void PauseJob(Guid jobId)
    {
        if (_pauseEvents.TryGetValue(jobId, out var pe))
        {
            pe.Reset();
            _ = _jobRepository.UpdateJobStatusAsync(jobId, JobStatus.Paused);
            _logger.LogInformation("Job '{JobId}' paused.", jobId);
        }
    }

    public void ResumeJob(Guid jobId)
    {
        if (_pauseEvents.TryGetValue(jobId, out var pe))
        {
            pe.Set();
            _ = _jobRepository.UpdateJobStatusAsync(jobId, JobStatus.Running);
            _logger.LogInformation("Job '{JobId}' resumed.", jobId);
        }
    }

    public void CancelJob(Guid jobId)
    {
        if (_ctsMap.TryGetValue(jobId, out var cts))
        {
            cts.Cancel();
            _logger.LogInformation("Job '{JobId}' cancelled.", jobId);
        }

        if (_pauseEvents.TryGetValue(jobId, out var pe))
        {
            pe.Set(); // unblock waiting threads so they can cancel
        }

        _ = _jobRepository.UpdateJobStatusAsync(jobId, JobStatus.Cancelled);
    }

    public async Task RetryFailedItemsAsync(Guid jobId, CancellationToken ct = default)
    {
        var items = await _jobRepository.GetJobItemsAsync(jobId, ct);
        var failedItems = items.Where(i => i.Status == JobItemStatus.Failed).ToList();
        foreach (var item in failedItems)
        {
            item.Status = JobItemStatus.Queued;
            item.ErrorDetails = null;
            await _jobRepository.UpdateJobItemAsync(item, ct);
        }

        await StartJobAsync(jobId, ct);
    }

    public Task<List<CompressionJob>> GetAllJobsAsync(CancellationToken ct = default) => _jobRepository.GetAllJobsAsync(ct);

    public Task<List<JobItem>> GetJobItemsAsync(Guid jobId, CancellationToken ct = default) => _jobRepository.GetJobItemsAsync(jobId, ct);

    public Task<List<ActivityLog>> GetActivityLogsAsync(int take = 500, CancellationToken ct = default) => _jobRepository.GetActivityLogsAsync(take, ct);

    public Task AddActivityLogAsync(ActivityLog log, CancellationToken ct = default) => _jobRepository.AddActivityLogAsync(log, ct);

    private async Task ExecuteJobAsync(
        CompressionJob job,
        List<JobItem> items,
        ApplicationSettings settings,
        CancellationToken ct,
        ManualResetEventSlim pauseEvent)
    {
        await _jobRepository.UpdateJobStatusAsync(job.Id, JobStatus.Running, null, CancellationToken.None);
        _logger.LogInformation("Starting execution of Job '{Title}' ({JobId}) with {Count} items.", job.Title, job.Id, items.Count);

        int workers = Math.Clamp(settings.ParallelWorkers, 1, 16);
        using var semaphore = new SemaphoreSlim(workers, workers);

        int processed = job.ProcessedFiles;
        int skipped = job.SkippedFiles;
        int failed = job.FailedFiles;
        long origBytes = job.OriginalBytes;
        long procBytes = job.ProcessedBytes;

        long minImageBytes = settings.MinimumImageSizeKb * 1024;
        long minPdfBytes = settings.MinimumPdfSizeKb * 1024;
        double minSavingsPct = settings.MinimumSavingsPercentage;

        StorageProfile? s3Profile = null;
        if (job.SourceType.Equals("S3", StringComparison.OrdinalIgnoreCase))
        {
            var profiles = await _settingsService.GetProfilesAsync(CancellationToken.None);
            s3Profile = profiles.FirstOrDefault(p => p.BucketName.Equals(job.SourcePathOrBucket, StringComparison.OrdinalIgnoreCase))
                        ?? profiles.FirstOrDefault();
        }

        var compressionProfile = CompressionProfile.CreateVisuallyOptimized();

        var tasks = new List<Task>();

        foreach (var item in items)
        {
            if (ct.IsCancellationRequested) break;
            if (item.Status == JobItemStatus.Compressed || item.Status == JobItemStatus.UploadedVerified)
                continue;

            await semaphore.WaitAsync(ct);

            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    pauseEvent.Wait(ct);
                    ct.ThrowIfCancellationRequested();

                    item.Status = JobItemStatus.Processing;
                    await _jobRepository.UpdateJobItemAsync(item, CancellationToken.None);

                    if (job.SourceType.Equals("Local", StringComparison.OrdinalIgnoreCase))
                    {
                        await ProcessLocalItemAsync(item, compressionProfile, minImageBytes, minPdfBytes, minSavingsPct, ct);
                    }
                    else if (s3Profile != null)
                    {
                        await ProcessS3ItemAsync(job, item, s3Profile, compressionProfile, minImageBytes, minPdfBytes, minSavingsPct, ct);
                    }

                    if (item.Status == JobItemStatus.Compressed || item.Status == JobItemStatus.UploadedVerified)
                    {
                        Interlocked.Increment(ref processed);
                        Interlocked.Add(ref procBytes, item.ProcessedSizeBytes);
                    }
                    else if (item.Status == JobItemStatus.SkippedBelowThreshold ||
                             item.Status == JobItemStatus.SkippedUnsupported ||
                             item.Status == JobItemStatus.SkippedAlreadyProcessed ||
                             item.Status == JobItemStatus.CheckedNoSavings)
                    {
                        Interlocked.Increment(ref skipped);
                        Interlocked.Add(ref procBytes, item.OriginalSizeBytes);
                    }
                    else
                    {
                        Interlocked.Increment(ref failed);
                        Interlocked.Add(ref procBytes, item.OriginalSizeBytes);
                    }

                    await _jobRepository.UpdateJobItemAsync(item, CancellationToken.None);
                    await _jobRepository.UpdateJobProgressAsync(job.Id, processed, skipped, failed, origBytes, procBytes, CancellationToken.None);

                    ProgressUpdated?.Invoke(this, new JobProgressUpdate(
                        job.Id,
                        processed + skipped + failed,
                        items.Count,
                        origBytes,
                        procBytes,
                        Math.Max(0, origBytes - procBytes),
                        item.SourceKeyOrPath,
                        JobStatus.Running
                    ));
                }
                catch (OperationCanceledException)
                {
                    item.Status = JobItemStatus.Cancelled;
                    await _jobRepository.UpdateJobItemAsync(item, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    item.Status = JobItemStatus.Failed;
                    item.ErrorDetails = ex.Message;
                    Interlocked.Increment(ref failed);
                    await _jobRepository.UpdateJobItemAsync(item, CancellationToken.None);
                }
                finally
                {
                    semaphore.Release();
                }
            }, ct));
        }

        try
        {
            await Task.WhenAll(tasks);
            var finalStatus = ct.IsCancellationRequested ? JobStatus.Cancelled : (failed > 0 && processed == 0 ? JobStatus.Failed : JobStatus.Completed);
            await _jobRepository.UpdateJobStatusAsync(job.Id, finalStatus, null, CancellationToken.None);
            _logger.LogInformation("Job '{JobId}' completed with status: {Status}", job.Id, finalStatus);
        }
        catch (OperationCanceledException)
        {
            await _jobRepository.UpdateJobStatusAsync(job.Id, JobStatus.Cancelled, "Job was cancelled by user.", CancellationToken.None);
        }
        catch (Exception ex)
        {
            await _jobRepository.UpdateJobStatusAsync(job.Id, JobStatus.Failed, ex.Message, CancellationToken.None);
        }
        finally
        {
            _ctsMap.TryRemove(job.Id, out _);
            _pauseEvents.TryRemove(job.Id, out _);
        }
    }

    private async Task ProcessLocalItemAsync(
        JobItem item,
        CompressionProfile profile,
        long minImageBytes,
        long minPdfBytes,
        double minSavingsPct,
        CancellationToken ct)
    {
        string source = item.SourceKeyOrPath;
        string dest = item.DestinationKeyOrPath;

        string tempOut = Path.Combine(Path.GetTempPath(), $"spacepilot_{Guid.NewGuid():N}{Path.GetExtension(source)}");
        try
        {
            var result = await _compressionEngine.ProcessFileAsync(source, tempOut, profile, minImageBytes, minPdfBytes, minSavingsPct, ct);

            if (!result.Success)
            {
                item.Status = JobItemStatus.Failed;
                item.ErrorDetails = result.ErrorMessage;
                return;
            }

            if (!result.MeetsThreshold && result.ErrorMessage != null && result.ErrorMessage.StartsWith("Skipped"))
            {
                item.Status = JobItemStatus.SkippedBelowThreshold;
                item.ProcessedSizeBytes = item.OriginalSizeBytes;
                item.ErrorDetails = result.ErrorMessage;
                return;
            }

            if (!result.MeetsThreshold)
            {
                item.Status = JobItemStatus.CheckedNoSavings;
                item.ProcessedSizeBytes = item.OriginalSizeBytes;
                item.ErrorDetails = result.ErrorMessage ?? "Did not meet savings threshold.";
                return;
            }

            // Copy to final destination
            string? destDir = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(destDir))
                Directory.CreateDirectory(destDir);

            File.Copy(tempOut, dest, true);

            item.Status = JobItemStatus.Compressed;
            item.ProcessedSizeBytes = result.ProcessedSizeBytes;
            item.CompletedAt = DateTime.UtcNow;

            await _jobRepository.AddActivityLogAsync(new ActivityLog
            {
                SourceBucketOrPath = Path.GetDirectoryName(source) ?? "Local",
                ObjectKeyOrFileName = Path.GetFileName(source),
                FileType = item.FileType,
                OriginalSizeBytes = item.OriginalSizeBytes,
                ProcessedSizeBytes = item.ProcessedSizeBytes,
                Action = "Local Compressed",
                Status = "Success"
            }, ct);
        }
        finally
        {
            if (File.Exists(tempOut))
            {
                try { File.Delete(tempOut); } catch { }
            }
        }
    }

    private async Task ProcessS3ItemAsync(
        CompressionJob job,
        JobItem item,
        StorageProfile s3Profile,
        CompressionProfile profile,
        long minImageBytes,
        long minPdfBytes,
        double minSavingsPct,
        CancellationToken ct)
    {
        string key = item.SourceKeyOrPath;
        string ext = Path.GetExtension(key);
        string tempSource = Path.Combine(Path.GetTempPath(), $"s3_in_{Guid.NewGuid():N}{ext}");
        string tempOut = Path.Combine(Path.GetTempPath(), $"s3_out_{Guid.NewGuid():N}{ext}");

        try
        {
            // 1. Download
            await _s3Service.DownloadObjectAsync(s3Profile, key, tempSource, null, ct);

            // 2. Compress
            var result = await _compressionEngine.ProcessFileAsync(tempSource, tempOut, profile, minImageBytes, minPdfBytes, minSavingsPct, ct);

            if (!result.Success)
            {
                item.Status = JobItemStatus.Failed;
                item.ErrorDetails = result.ErrorMessage;
                return;
            }

            if (!result.MeetsThreshold && result.ErrorMessage != null && result.ErrorMessage.StartsWith("Skipped"))
            {
                item.Status = JobItemStatus.SkippedBelowThreshold;
                item.ProcessedSizeBytes = item.OriginalSizeBytes;
                item.ErrorDetails = result.ErrorMessage;
                return;
            }

            if (!result.MeetsThreshold)
            {
                item.Status = JobItemStatus.CheckedNoSavings;
                item.ProcessedSizeBytes = item.OriginalSizeBytes;
                item.ErrorDetails = result.ErrorMessage ?? "No savings achieved.";
                return;
            }

            item.ProcessedSizeBytes = result.ProcessedSizeBytes;

            // 3. Handle upload mode
            if (job.UploadMode == UploadMode.DryRun)
            {
                item.Status = JobItemStatus.Compressed; // Dry run finished successfully
                item.CompletedAt = DateTime.UtcNow;
            }
            else if (job.UploadMode == UploadMode.StagingPrefix)
            {
                string stagingPrefix = string.IsNullOrWhiteSpace(job.TargetPrefixOrDirectory) ? "__spacepilot_compressed/" : job.TargetPrefixOrDirectory;
                string stagingKey = PathUtilities.BuildStagingKey(stagingPrefix, key);

                var metadata = await _s3Service.GetObjectMetadataAsync(s3Profile, key, ct);
                string contentType = metadata.TryGetValue("ContentType", out var ctVal) ? ctVal : "application/octet-stream";

                await _s3Service.UploadObjectAsync(s3Profile, tempOut, stagingKey, contentType, metadata, ct);

                item.DestinationKeyOrPath = stagingKey;
                item.Status = JobItemStatus.UploadedVerified;
                item.CompletedAt = DateTime.UtcNow;
            }
            else if (job.UploadMode == UploadMode.ReplaceOriginal)
            {
                // Create backup first
                string backupKey = PathUtilities.BuildBackupKey(key, DateTime.UtcNow);
                await _s3Service.CopyObjectAsync(s3Profile, key, backupKey, ct);

                var metadata = await _s3Service.GetObjectMetadataAsync(s3Profile, key, ct);
                string contentType = metadata.TryGetValue("ContentType", out var ctVal) ? ctVal : "application/octet-stream";

                // Replace original
                await _s3Service.UploadObjectAsync(s3Profile, tempOut, key, contentType, metadata, ct);

                item.DestinationKeyOrPath = key;
                item.Status = JobItemStatus.UploadedVerified;
                item.CompletedAt = DateTime.UtcNow;
            }

            await _jobRepository.AddActivityLogAsync(new ActivityLog
            {
                SourceBucketOrPath = s3Profile.BucketName,
                ObjectKeyOrFileName = key,
                FileType = item.FileType,
                OriginalSizeBytes = item.OriginalSizeBytes,
                ProcessedSizeBytes = item.ProcessedSizeBytes,
                Action = $"S3 {job.UploadMode}",
                Status = "Success"
            }, ct);
        }
        finally
        {
            if (File.Exists(tempSource)) try { File.Delete(tempSource); } catch { }
            if (File.Exists(tempOut)) try { File.Delete(tempOut); } catch { }
        }
    }
}
