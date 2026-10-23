using System.Data.Common;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using SpacePilot.Core.Enums;
using SpacePilot.Core.Interfaces;
using SpacePilot.Core.Models;

namespace SpacePilot.Infrastructure.Persistence;

public class SqliteJobRepository : IJobRepository
{
    private readonly DatabaseContext _dbContext;
    private readonly ILogger<SqliteJobRepository> _logger;

    public SqliteJobRepository(DatabaseContext dbContext, ILogger<SqliteJobRepository> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task SaveJobAsync(CompressionJob job, IEnumerable<JobItem> items, CancellationToken ct = default)
    {
        await _dbContext.InitializeAsync(ct);
        using var conn = _dbContext.CreateConnection();
        await conn.OpenAsync(ct);
        using var tx = conn.BeginTransaction();

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
                INSERT INTO CompressionJobs (
                    Id, Title, SourceType, SourcePathOrBucket, TargetPrefixOrDirectory,
                    Status, UploadMode, TotalFiles, ProcessedFiles, SkippedFiles, FailedFiles,
                    OriginalBytes, ProcessedBytes, StartedAt, CompletedAt, ErrorMessage
                ) VALUES (
                    $Id, $Title, $SourceType, $SourcePathOrBucket, $TargetPrefixOrDirectory,
                    $Status, $UploadMode, $TotalFiles, $ProcessedFiles, $SkippedFiles, $FailedFiles,
                    $OriginalBytes, $ProcessedBytes, $StartedAt, $CompletedAt, $ErrorMessage
                )
                ON CONFLICT(Id) DO UPDATE SET
                    Status = $Status,
                    ProcessedFiles = $ProcessedFiles,
                    SkippedFiles = $SkippedFiles,
                    FailedFiles = $FailedFiles,
                    OriginalBytes = $OriginalBytes,
                    ProcessedBytes = $ProcessedBytes,
                    CompletedAt = $CompletedAt,
                    ErrorMessage = $ErrorMessage;";

            cmd.Parameters.AddWithValue("$Id", job.Id.ToString());
            cmd.Parameters.AddWithValue("$Title", job.Title);
            cmd.Parameters.AddWithValue("$SourceType", job.SourceType);
            cmd.Parameters.AddWithValue("$SourcePathOrBucket", job.SourcePathOrBucket);
            cmd.Parameters.AddWithValue("$TargetPrefixOrDirectory", (object?)job.TargetPrefixOrDirectory ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$Status", (int)job.Status);
            cmd.Parameters.AddWithValue("$UploadMode", (int)job.UploadMode);
            cmd.Parameters.AddWithValue("$TotalFiles", job.TotalFiles);
            cmd.Parameters.AddWithValue("$ProcessedFiles", job.ProcessedFiles);
            cmd.Parameters.AddWithValue("$SkippedFiles", job.SkippedFiles);
            cmd.Parameters.AddWithValue("$FailedFiles", job.FailedFiles);
            cmd.Parameters.AddWithValue("$OriginalBytes", job.OriginalBytes);
            cmd.Parameters.AddWithValue("$ProcessedBytes", job.ProcessedBytes);
            cmd.Parameters.AddWithValue("$StartedAt", job.StartedAt.ToString("o"));
            cmd.Parameters.AddWithValue("$CompletedAt", job.CompletedAt.HasValue ? job.CompletedAt.Value.ToString("o") : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$ErrorMessage", (object?)job.ErrorMessage ?? DBNull.Value);

            await cmd.ExecuteNonQueryAsync(ct);
        }

        var itemList = items.ToList();
        if (itemList.Count > 0)
        {
            using var itemCmd = conn.CreateCommand();
            itemCmd.Transaction = tx;
            itemCmd.CommandText = @"
                INSERT INTO JobItems (
                    JobId, SourceKeyOrPath, DestinationKeyOrPath, FileType,
                    Status, OriginalSizeBytes, ProcessedSizeBytes, ErrorDetails, CompletedAt, IsLossy
                ) VALUES (
                    $JobId, $SourceKeyOrPath, $DestinationKeyOrPath, $FileType,
                    $Status, $OriginalSizeBytes, $ProcessedSizeBytes, $ErrorDetails, $CompletedAt, $IsLossy
                );";

            var pJobId = itemCmd.CreateParameter(); pJobId.ParameterName = "$JobId"; itemCmd.Parameters.Add(pJobId);
            var pSource = itemCmd.CreateParameter(); pSource.ParameterName = "$SourceKeyOrPath"; itemCmd.Parameters.Add(pSource);
            var pDest = itemCmd.CreateParameter(); pDest.ParameterName = "$DestinationKeyOrPath"; itemCmd.Parameters.Add(pDest);
            var pType = itemCmd.CreateParameter(); pType.ParameterName = "$FileType"; itemCmd.Parameters.Add(pType);
            var pStatus = itemCmd.CreateParameter(); pStatus.ParameterName = "$Status"; itemCmd.Parameters.Add(pStatus);
            var pOrig = itemCmd.CreateParameter(); pOrig.ParameterName = "$OriginalSizeBytes"; itemCmd.Parameters.Add(pOrig);
            var pProc = itemCmd.CreateParameter(); pProc.ParameterName = "$ProcessedSizeBytes"; itemCmd.Parameters.Add(pProc);
            var pErr = itemCmd.CreateParameter(); pErr.ParameterName = "$ErrorDetails"; itemCmd.Parameters.Add(pErr);
            var pComp = itemCmd.CreateParameter(); pComp.ParameterName = "$CompletedAt"; itemCmd.Parameters.Add(pComp);
            var pLossy = itemCmd.CreateParameter(); pLossy.ParameterName = "$IsLossy"; itemCmd.Parameters.Add(pLossy);

            foreach (var item in itemList)
            {
                pJobId.Value = job.Id.ToString();
                pSource.Value = item.SourceKeyOrPath;
                pDest.Value = (object?)item.DestinationKeyOrPath ?? DBNull.Value;
                pType.Value = (int)item.FileType;
                pStatus.Value = (int)item.Status;
                pOrig.Value = item.OriginalSizeBytes;
                pProc.Value = item.ProcessedSizeBytes;
                pErr.Value = (object?)item.ErrorDetails ?? DBNull.Value;
                pComp.Value = item.CompletedAt.HasValue ? item.CompletedAt.Value.ToString("o") : (object)DBNull.Value;
                pLossy.Value = item.IsLossy ? 1 : 0;

                await itemCmd.ExecuteNonQueryAsync(ct);
            }
        }

        await tx.CommitAsync(ct);
    }

    public async Task UpdateJobStatusAsync(Guid jobId, JobStatus status, string? errorMessage = null, CancellationToken ct = default)
    {
        using var conn = _dbContext.CreateConnection();
        await conn.OpenAsync(ct);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE CompressionJobs 
            SET Status = $Status, 
                ErrorMessage = COALESCE($ErrorMessage, ErrorMessage),
                CompletedAt = CASE WHEN $Status IN (3, 4, 5) THEN $Now ELSE CompletedAt END
            WHERE Id = $Id;";
        cmd.Parameters.AddWithValue("$Status", (int)status);
        cmd.Parameters.AddWithValue("$ErrorMessage", (object?)errorMessage ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$Now", DateTime.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("$Id", jobId.ToString());

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task UpdateJobProgressAsync(Guid jobId, int processed, int skipped, int failed, long origBytes, long procBytes, CancellationToken ct = default)
    {
        using var conn = _dbContext.CreateConnection();
        await conn.OpenAsync(ct);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE CompressionJobs 
            SET ProcessedFiles = $Processed,
                SkippedFiles = $Skipped,
                FailedFiles = $Failed,
                OriginalBytes = $OrigBytes,
                ProcessedBytes = $ProcBytes
            WHERE Id = $Id;";
        cmd.Parameters.AddWithValue("$Processed", processed);
        cmd.Parameters.AddWithValue("$Skipped", skipped);
        cmd.Parameters.AddWithValue("$Failed", failed);
        cmd.Parameters.AddWithValue("$OrigBytes", origBytes);
        cmd.Parameters.AddWithValue("$ProcBytes", procBytes);
        cmd.Parameters.AddWithValue("$Id", jobId.ToString());

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task UpdateJobItemAsync(JobItem item, CancellationToken ct = default)
    {
        using var conn = _dbContext.CreateConnection();
        await conn.OpenAsync(ct);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE JobItems 
            SET Status = $Status,
                ProcessedSizeBytes = $ProcessedSizeBytes,
                DestinationKeyOrPath = $Dest,
                ErrorDetails = $ErrorDetails,
                CompletedAt = $CompletedAt,
                IsLossy = $IsLossy
            WHERE Id = $Id;";
        cmd.Parameters.AddWithValue("$Status", (int)item.Status);
        cmd.Parameters.AddWithValue("$ProcessedSizeBytes", item.ProcessedSizeBytes);
        cmd.Parameters.AddWithValue("$Dest", (object?)item.DestinationKeyOrPath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ErrorDetails", (object?)item.ErrorDetails ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$CompletedAt", item.CompletedAt.HasValue ? item.CompletedAt.Value.ToString("o") : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$IsLossy", item.IsLossy ? 1 : 0);
        cmd.Parameters.AddWithValue("$Id", item.Id);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<List<CompressionJob>> GetAllJobsAsync(CancellationToken ct = default)
    {
        await _dbContext.InitializeAsync(ct);
        using var conn = _dbContext.CreateConnection();
        await conn.OpenAsync(ct);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, Title, SourceType, SourcePathOrBucket, TargetPrefixOrDirectory,
                   Status, UploadMode, TotalFiles, ProcessedFiles, SkippedFiles, FailedFiles,
                   OriginalBytes, ProcessedBytes, StartedAt, CompletedAt, ErrorMessage
            FROM CompressionJobs
            ORDER BY StartedAt DESC;";

        var list = new List<CompressionJob>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            string? compStr = reader.IsDBNull(14) ? null : reader.GetString(14);
            DateTime? compDt = !string.IsNullOrEmpty(compStr) && DateTime.TryParse(compStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var cdt) ? cdt : null;

            list.Add(new CompressionJob
            {
                Id = Guid.Parse(reader.GetString(0)),
                Title = reader.GetString(1),
                SourceType = reader.GetString(2),
                SourcePathOrBucket = reader.GetString(3),
                TargetPrefixOrDirectory = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                Status = (JobStatus)reader.GetInt32(5),
                UploadMode = (UploadMode)reader.GetInt32(6),
                TotalFiles = reader.GetInt32(7),
                ProcessedFiles = reader.GetInt32(8),
                SkippedFiles = reader.GetInt32(9),
                FailedFiles = reader.GetInt32(10),
                OriginalBytes = reader.GetInt64(11),
                ProcessedBytes = reader.GetInt64(12),
                StartedAt = DateTime.Parse(reader.GetString(13), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                CompletedAt = compDt,
                ErrorMessage = reader.IsDBNull(15) ? null : reader.GetString(15)
            });
        }

        return list;
    }

    public async Task<List<JobItem>> GetJobItemsAsync(Guid jobId, CancellationToken ct = default)
    {
        using var conn = _dbContext.CreateConnection();
        await conn.OpenAsync(ct);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, JobId, SourceKeyOrPath, DestinationKeyOrPath, FileType,
                   Status, OriginalSizeBytes, ProcessedSizeBytes, ErrorDetails, CompletedAt, IsLossy
            FROM JobItems
            WHERE JobId = $JobId
            ORDER BY Id ASC;";
        cmd.Parameters.AddWithValue("$JobId", jobId.ToString());

        var list = new List<JobItem>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            string? compStr = reader.IsDBNull(9) ? null : reader.GetString(9);
            DateTime? compDt = !string.IsNullOrEmpty(compStr) && DateTime.TryParse(compStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var cdt) ? cdt : null;

            list.Add(new JobItem
            {
                Id = reader.GetInt64(0),
                JobId = Guid.Parse(reader.GetString(1)),
                SourceKeyOrPath = reader.GetString(2),
                DestinationKeyOrPath = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                FileType = (FileCategory)reader.GetInt32(4),
                Status = (JobItemStatus)reader.GetInt32(5),
                OriginalSizeBytes = reader.GetInt64(6),
                ProcessedSizeBytes = reader.GetInt64(7),
                ErrorDetails = reader.IsDBNull(8) ? null : reader.GetString(8),
                CompletedAt = compDt,
                IsLossy = reader.GetInt32(10) == 1
            });
        }

        return list;
    }

    public async Task AddActivityLogAsync(ActivityLog log, CancellationToken ct = default)
    {
        await _dbContext.InitializeAsync(ct);
        using var conn = _dbContext.CreateConnection();
        await conn.OpenAsync(ct);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO ActivityLogs (
                Timestamp, SourceBucketOrPath, ObjectKeyOrFileName, FileType,
                OriginalSizeBytes, ProcessedSizeBytes, Action, Status, ErrorDetails
            ) VALUES (
                $Timestamp, $Source, $Key, $FileType,
                $Orig, $Proc, $Action, $Status, $Error
            );";
        cmd.Parameters.AddWithValue("$Timestamp", log.Timestamp.ToString("o"));
        cmd.Parameters.AddWithValue("$Source", log.SourceBucketOrPath);
        cmd.Parameters.AddWithValue("$Key", log.ObjectKeyOrFileName);
        cmd.Parameters.AddWithValue("$FileType", (int)log.FileType);
        cmd.Parameters.AddWithValue("$Orig", log.OriginalSizeBytes);
        cmd.Parameters.AddWithValue("$Proc", log.ProcessedSizeBytes);
        cmd.Parameters.AddWithValue("$Action", log.Action);
        cmd.Parameters.AddWithValue("$Status", log.Status);
        cmd.Parameters.AddWithValue("$Error", (object?)log.ErrorDetails ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<List<ActivityLog>> GetActivityLogsAsync(int take = 500, CancellationToken ct = default)
    {
        await _dbContext.InitializeAsync(ct);
        using var conn = _dbContext.CreateConnection();
        await conn.OpenAsync(ct);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, Timestamp, SourceBucketOrPath, ObjectKeyOrFileName, FileType,
                   OriginalSizeBytes, ProcessedSizeBytes, Action, Status, ErrorDetails
            FROM ActivityLogs
            ORDER BY Id DESC
            LIMIT $Limit;";
        cmd.Parameters.AddWithValue("$Limit", take);

        var list = new List<ActivityLog>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new ActivityLog
            {
                Id = reader.GetInt64(0),
                Timestamp = DateTime.Parse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                SourceBucketOrPath = reader.GetString(2),
                ObjectKeyOrFileName = reader.GetString(3),
                FileType = (FileCategory)reader.GetInt32(4),
                OriginalSizeBytes = reader.GetInt64(5),
                ProcessedSizeBytes = reader.GetInt64(6),
                Action = reader.GetString(7),
                Status = reader.GetString(8),
                ErrorDetails = reader.IsDBNull(9) ? null : reader.GetString(9)
            });
        }

        return list;
    }
}
