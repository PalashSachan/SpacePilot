using Microsoft.Extensions.Logging;
using SpacePilot.Core.Enums;
using SpacePilot.Core.Interfaces;
using SpacePilot.Core.Models;
using SpacePilot.Core.Utilities;

namespace SpacePilot.Application.Services;

public class LocalFolderService : ILocalFolderService
{
    private readonly ILogger<LocalFolderService> _logger;

    public LocalFolderService(ILogger<LocalFolderService> logger)
    {
        _logger = logger;
    }

    public async Task<List<string>> DiscoverFilesAsync(
        string folderPath,
        bool recursive,
        IEnumerable<string>? extensions = null,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var files = new List<string>();
            if (!Directory.Exists(folderPath)) return files;

            var extSet = extensions != null
                ? new HashSet<string>(extensions.Select(e => e.StartsWith('.') ? e : "." + e), StringComparer.OrdinalIgnoreCase)
                : null;

            var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            try
            {
                foreach (string file in Directory.EnumerateFiles(folderPath, "*.*", searchOption))
                {
                    ct.ThrowIfCancellationRequested();

                    // CRITICAL: Skip any file located inside a "compressed" folder
                    if (PathUtilities.IsInsideCompressedSubfolder(file))
                        continue;

                    // Skip hidden or system files
                    try
                    {
                        var attrs = File.GetAttributes(file);
                        if (attrs.HasFlag(FileAttributes.Hidden) || attrs.HasFlag(FileAttributes.System))
                            continue;
                    }
                    catch { }

                    string ext = Path.GetExtension(file);
                    if (extSet != null && !extSet.Contains(ext))
                        continue;

                    if (PathUtilities.IsSupportedMedia(file))
                    {
                        files.Add(file);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error enumerating files in '{Folder}'", folderPath);
            }

            return files;
        }, ct);
    }

    public async Task<CompressionJob> PrepareLocalCompressionJobAsync(
        IEnumerable<string> filePaths,
        CompressionProfile profile,
        string? customDestinationFolder = null,
        CollisionPolicy collisionPolicy = CollisionPolicy.Skip,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var fileList = filePaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var job = new CompressionJob
            {
                Title = $"Local Compression ({fileList.Count} files)",
                SourceType = "Local",
                SourcePathOrBucket = fileList.Count > 0 ? (Path.GetDirectoryName(fileList[0]) ?? "Local") : "Local",
                TargetPrefixOrDirectory = string.IsNullOrWhiteSpace(customDestinationFolder) ? "compressed/" : customDestinationFolder,
                Status = JobStatus.Queued,
                UploadMode = UploadMode.DryRun, // Local jobs don't upload to S3
                TotalFiles = fileList.Count
            };

            var items = new List<JobItem>();

            foreach (var file in fileList)
            {
                if (!File.Exists(file)) continue;

                var fi = new FileInfo(file);
                string destPath = DetermineDestinationPath(file, customDestinationFolder, collisionPolicy);

                items.Add(new JobItem
                {
                    JobId = job.Id,
                    SourceKeyOrPath = file,
                    DestinationKeyOrPath = destPath,
                    FileType = PathUtilities.CategorizeFile(file),
                    Status = JobItemStatus.Queued,
                    OriginalSizeBytes = fi.Length,
                    ProcessedSizeBytes = 0,
                    IsLossy = profile.Mode != CompressionMode.LosslessFirst || profile.ResizeMode != ResizeMode.None
                });

                job.OriginalBytes += fi.Length;
            }

            return job;
        }, ct);
    }

    private static string DetermineDestinationPath(string sourceFile, string? customDestinationFolder, CollisionPolicy collisionPolicy)
    {
        string targetPath;
        if (!string.IsNullOrWhiteSpace(customDestinationFolder))
        {
            string fileName = Path.GetFileName(sourceFile);
            targetPath = Path.Combine(customDestinationFolder, fileName);
        }
        else
        {
            // Default rule: sibling "compressed" subfolder
            targetPath = PathUtilities.GetLocalCompressedSiblingPath(sourceFile);
        }

        if (File.Exists(targetPath) && collisionPolicy == CollisionPolicy.RenameWithSuffix)
        {
            string dir = Path.GetDirectoryName(targetPath) ?? string.Empty;
            string nameWithoutExt = Path.GetFileNameWithoutExtension(targetPath);
            string ext = Path.GetExtension(targetPath);
            int counter = 1;
            while (File.Exists(targetPath))
            {
                targetPath = Path.Combine(dir, $"{nameWithoutExt}_{counter}{ext}");
                counter++;
            }
        }

        return targetPath;
    }
}
