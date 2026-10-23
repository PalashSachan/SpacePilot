using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using SpacePilot.Core.Interfaces;
using SpacePilot.Core.Models;
using SpacePilot.Core.Utilities;

namespace SpacePilot.Infrastructure.S3;

public class S3StorageService : IS3Service
{
    private readonly ILogger<S3StorageService> _logger;

    public S3StorageService(ILogger<S3StorageService> logger)
    {
        _logger = logger;
    }

    public async Task<bool> TestConnectionAsync(StorageProfile profile, CancellationToken ct = default)
    {
        try
        {
            using var client = S3ClientFactory.CreateClient(profile);
            var req = new ListObjectsV2Request
            {
                BucketName = profile.BucketName,
                MaxKeys = 1
            };
            await client.ListObjectsV2Async(req, ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "S3 connection test failed for profile '{ProfileName}'", profile.Name);
            return false;
        }
    }

    public async Task<List<string>> ListBucketsAsync(StorageProfile profile, CancellationToken ct = default)
    {
        try
        {
            using var client = S3ClientFactory.CreateClient(profile);
            var response = await client.ListBucketsAsync(ct);
            return response.Buckets.Select(b => b.BucketName).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to list buckets for profile '{ProfileName}'", profile.Name);
            return new List<string> { profile.BucketName };
        }
    }

    public async Task ScanBucketAsync(
        StorageProfile profile,
        string prefix,
        IStorageIndexer indexer,
        IProgress<ScanProgress>? progress = null,
        CancellationToken ct = default)
    {
        prefix = PathUtilities.NormalizeS3Prefix(prefix);
        _logger.LogInformation("Starting bucket scan on bucket '{Bucket}', prefix '{Prefix}'", profile.BucketName, prefix);

        using var client = S3ClientFactory.CreateClient(profile);

        string? continuationToken = null;
        int pages = 0;
        int totalIndexed = 0;
        long totalBytes = 0;
        const int batchSize = 1000;
        var batch = new List<IndexedObject>(batchSize);

        do
        {
            ct.ThrowIfCancellationRequested();

            var request = new ListObjectsV2Request
            {
                BucketName = profile.BucketName,
                Prefix = string.IsNullOrEmpty(prefix) ? null : prefix,
                ContinuationToken = continuationToken,
                MaxKeys = 1000
            };

            ListObjectsV2Response response = await ExecuteWithRetryAsync(() => client.ListObjectsV2Async(request, ct), profile.RetryCount);

            pages++;
            foreach (var s3Obj in response.S3Objects)
            {
                string key = s3Obj.Key;
                bool isFolder = key.EndsWith('/');
                string fileName = PathUtilities.GetS3Name(key);
                string ext = isFolder ? string.Empty : Path.GetExtension(fileName);
                string dirPrefix = PathUtilities.GetParentPrefix(key);

                var indexedObj = new IndexedObject
                {
                    BucketName = profile.BucketName,
                    Key = key,
                    Prefix = dirPrefix,
                    FileName = fileName,
                    Extension = ext,
                    SizeBytes = s3Obj.Size ?? 0,
                    ETag = s3Obj.ETag?.Trim('\"') ?? string.Empty,
                    LastModified = s3Obj.LastModified,
                    StorageClass = s3Obj.StorageClass?.Value ?? "STANDARD",
                    IsFolder = isFolder,
                    Category = PathUtilities.CategorizeFile(key),
                    ScannedAt = DateTime.UtcNow
                };

                batch.Add(indexedObj);
                totalBytes += s3Obj.Size ?? 0;
                totalIndexed++;

                if (batch.Count >= batchSize)
                {
                    await indexer.InsertObjectsBatchAsync(batch, ct);
                    batch.Clear();
                }
            }

            continuationToken = response.IsTruncated == true ? response.NextContinuationToken : null;

            progress?.Report(new ScanProgress(pages, totalIndexed, totalBytes, prefix, $"Indexed {totalIndexed:N0} objects across {pages} pages..."));

        } while (!string.IsNullOrEmpty(continuationToken));

        if (batch.Count > 0)
        {
            await indexer.InsertObjectsBatchAsync(batch, ct);
            batch.Clear();
        }

        _logger.LogInformation("Completed bucket scan. Indexed {Total} objects ({Bytes} bytes) across {Pages} pages.",
            totalIndexed, totalBytes, pages);

        progress?.Report(new ScanProgress(pages, totalIndexed, totalBytes, prefix, "Scan completed successfully."));
    }

    public async Task DownloadObjectAsync(
        StorageProfile profile,
        string key,
        string destinationFilePath,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        string? dir = Path.GetDirectoryName(destinationFilePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        using var client = S3ClientFactory.CreateClient(profile);
        var request = new GetObjectRequest
        {
            BucketName = profile.BucketName,
            Key = key
        };

        using var response = await client.GetObjectAsync(request, ct);
        long totalLength = response.ContentLength;

        using var responseStream = response.ResponseStream;
        using var fileStream = new FileStream(destinationFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

        byte[] buffer = new byte[81920];
        long totalRead = 0;
        int read;

        while ((read = await responseStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
            totalRead += read;
            if (totalLength > 0 && progress != null)
            {
                progress.Report((double)totalRead / totalLength);
            }
        }
    }

    public async Task UploadObjectAsync(
        StorageProfile profile,
        string sourceFilePath,
        string key,
        string? contentType = null,
        IDictionary<string, string>? metadata = null,
        CancellationToken ct = default)
    {
        if (!File.Exists(sourceFilePath))
            throw new FileNotFoundException("Upload source file not found", sourceFilePath);

        using var client = S3ClientFactory.CreateClient(profile);
        var request = new PutObjectRequest
        {
            BucketName = profile.BucketName,
            Key = key,
            FilePath = sourceFilePath,
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType
        };

        if (metadata != null)
        {
            foreach (var kvp in metadata)
            {
                request.Metadata.Add(kvp.Key, kvp.Value);
            }
        }

        await ExecuteWithRetryAsync(() => client.PutObjectAsync(request, ct), profile.RetryCount);
    }

    public async Task CopyObjectAsync(
        StorageProfile profile,
        string sourceKey,
        string destinationKey,
        CancellationToken ct = default)
    {
        using var client = S3ClientFactory.CreateClient(profile);
        var request = new CopyObjectRequest
        {
            SourceBucket = profile.BucketName,
            SourceKey = sourceKey,
            DestinationBucket = profile.BucketName,
            DestinationKey = destinationKey
        };

        await ExecuteWithRetryAsync(() => client.CopyObjectAsync(request, ct), profile.RetryCount);
    }

    public async Task<int> DeleteObjectsAsync(
        StorageProfile profile,
        IEnumerable<string> keys,
        CancellationToken ct = default)
    {
        var keyList = keys.ToList();
        if (keyList.Count == 0) return 0;

        using var client = S3ClientFactory.CreateClient(profile);
        int deletedCount = 0;

        // AWS/S3 batch delete limit is 1000 items per request
        const int batchSize = 1000;
        for (int i = 0; i < keyList.Count; i += batchSize)
        {
            var chunk = keyList.Skip(i).Take(batchSize).ToList();
            var request = new DeleteObjectsRequest
            {
                BucketName = profile.BucketName,
                Objects = chunk.Select(k => new KeyVersion { Key = k }).ToList()
            };

            var response = await ExecuteWithRetryAsync(() => client.DeleteObjectsAsync(request, ct), profile.RetryCount);
            deletedCount += response.DeletedObjects?.Count ?? chunk.Count;
        }

        return deletedCount;
    }

    public async Task<IDictionary<string, string>> GetObjectMetadataAsync(
        StorageProfile profile,
        string key,
        CancellationToken ct = default)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var client = S3ClientFactory.CreateClient(profile);
            var request = new GetObjectMetadataRequest
            {
                BucketName = profile.BucketName,
                Key = key
            };
            var res = await client.GetObjectMetadataAsync(request, ct);

            if (!string.IsNullOrEmpty(res.Headers.ContentType)) dict["ContentType"] = res.Headers.ContentType;
            if (!string.IsNullOrEmpty(res.Headers.CacheControl)) dict["CacheControl"] = res.Headers.CacheControl;
            if (!string.IsNullOrEmpty(res.Headers.ContentDisposition)) dict["ContentDisposition"] = res.Headers.ContentDisposition;
            if (!string.IsNullOrEmpty(res.Headers.ContentEncoding)) dict["ContentEncoding"] = res.Headers.ContentEncoding;
            if (!string.IsNullOrEmpty(res.ETag)) dict["ETag"] = res.ETag.Trim('\"');

            foreach (string name in res.Metadata.Keys)
            {
                dict[name] = res.Metadata[name];
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch metadata for object '{Key}'", key);
        }

        return dict;
    }

    public async Task<bool> CheckVersioningEnabledAsync(
        StorageProfile profile,
        CancellationToken ct = default)
    {
        try
        {
            using var client = S3ClientFactory.CreateClient(profile);
            var res = await client.GetBucketVersioningAsync(new GetBucketVersioningRequest
            {
                BucketName = profile.BucketName
            }, ct);

            return res.VersioningConfig?.Status == VersionStatus.Enabled;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Bucket versioning check returned error (likely unsupported on this S3 provider)");
            return false;
        }
    }

    private static async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> action, int maxRetries)
    {
        int attempt = 0;
        while (true)
        {
            try
            {
                return await action();
            }
            catch (Exception ex) when (attempt < maxRetries && IsTransient(ex))
            {
                attempt++;
                int delayMs = (int)(Math.Pow(2, attempt) * 500) + Random.Shared.Next(100, 300);
                await Task.Delay(delayMs);
            }
        }
    }

    private static bool IsTransient(Exception ex)
    {
        return ex is HttpRequestException ||
               ex is TimeoutException ||
               (ex is AmazonS3Exception s3Ex && (
                   s3Ex.StatusCode == System.Net.HttpStatusCode.RequestTimeout ||
                   s3Ex.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable ||
                   s3Ex.StatusCode == System.Net.HttpStatusCode.GatewayTimeout ||
                   s3Ex.StatusCode == (System.Net.HttpStatusCode)429));
    }
}
