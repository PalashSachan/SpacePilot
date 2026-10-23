using System.Data.Common;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using SpacePilot.Core.Enums;
using SpacePilot.Core.Interfaces;
using SpacePilot.Core.Models;
using SpacePilot.Core.Utilities;

namespace SpacePilot.Infrastructure.Persistence;

public class SqliteStorageIndexer : IStorageIndexer
{
    private readonly DatabaseContext _dbContext;
    private readonly ILogger<SqliteStorageIndexer> _logger;

    public SqliteStorageIndexer(DatabaseContext dbContext, ILogger<SqliteStorageIndexer> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await _dbContext.InitializeAsync(ct);
    }

    public async Task InsertObjectsBatchAsync(IEnumerable<IndexedObject> objects, CancellationToken ct = default)
    {
        var objectList = objects.ToList();
        if (objectList.Count == 0) return;

        using var conn = _dbContext.CreateConnection();
        await conn.OpenAsync(ct);
        using var tx = conn.BeginTransaction();

        const string sql = @"
            INSERT INTO IndexedObjects (
                BucketName, Key, Prefix, FileName, Extension, SizeBytes,
                ETag, LastModified, ContentType, StorageClass, IsFolder, Category, ScannedAt
            ) VALUES (
                $BucketName, $Key, $Prefix, $FileName, $Extension, $SizeBytes,
                $ETag, $LastModified, $ContentType, $StorageClass, $IsFolder, $Category, $ScannedAt
            );";

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;

        var pBucket = cmd.CreateParameter(); pBucket.ParameterName = "$BucketName"; cmd.Parameters.Add(pBucket);
        var pKey = cmd.CreateParameter(); pKey.ParameterName = "$Key"; cmd.Parameters.Add(pKey);
        var pPrefix = cmd.CreateParameter(); pPrefix.ParameterName = "$Prefix"; cmd.Parameters.Add(pPrefix);
        var pFileName = cmd.CreateParameter(); pFileName.ParameterName = "$FileName"; cmd.Parameters.Add(pFileName);
        var pExtension = cmd.CreateParameter(); pExtension.ParameterName = "$Extension"; cmd.Parameters.Add(pExtension);
        var pSize = cmd.CreateParameter(); pSize.ParameterName = "$SizeBytes"; cmd.Parameters.Add(pSize);
        var pETag = cmd.CreateParameter(); pETag.ParameterName = "$ETag"; cmd.Parameters.Add(pETag);
        var pLastModified = cmd.CreateParameter(); pLastModified.ParameterName = "$LastModified"; cmd.Parameters.Add(pLastModified);
        var pContentType = cmd.CreateParameter(); pContentType.ParameterName = "$ContentType"; cmd.Parameters.Add(pContentType);
        var pStorageClass = cmd.CreateParameter(); pStorageClass.ParameterName = "$StorageClass"; cmd.Parameters.Add(pStorageClass);
        var pIsFolder = cmd.CreateParameter(); pIsFolder.ParameterName = "$IsFolder"; cmd.Parameters.Add(pIsFolder);
        var pCategory = cmd.CreateParameter(); pCategory.ParameterName = "$Category"; cmd.Parameters.Add(pCategory);
        var pScannedAt = cmd.CreateParameter(); pScannedAt.ParameterName = "$ScannedAt"; cmd.Parameters.Add(pScannedAt);

        foreach (var obj in objectList)
        {
            pBucket.Value = obj.BucketName;
            pKey.Value = obj.Key;
            pPrefix.Value = obj.Prefix;
            pFileName.Value = obj.FileName;
            pExtension.Value = obj.Extension;
            pSize.Value = obj.SizeBytes;
            pETag.Value = (object?)obj.ETag ?? DBNull.Value;
            pLastModified.Value = obj.LastModified.HasValue ? obj.LastModified.Value.ToString("o") : (object)DBNull.Value;
            pContentType.Value = (object?)obj.ContentType ?? DBNull.Value;
            pStorageClass.Value = obj.StorageClass;
            pIsFolder.Value = obj.IsFolder ? 1 : 0;
            pCategory.Value = (int)obj.Category;
            pScannedAt.Value = obj.ScannedAt.ToString("o");

            await cmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
    }

    public async Task<List<FolderSummary>> GetFolderSummariesAsync(string bucketName, string parentPrefix, CancellationToken ct = default)
    {
        parentPrefix = PathUtilities.NormalizeS3Prefix(parentPrefix);
        long totalBucketBytes = await GetTotalIndexedSizeBytesAsync(bucketName, ct);

        using var conn = _dbContext.CreateConnection();
        await conn.OpenAsync(ct);

        // Find all direct child subfolders under parentPrefix
        // A child key starts with parentPrefix, and contains at least one slash after parentPrefix length
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Key FROM IndexedObjects 
            WHERE BucketName = $BucketName AND Key LIKE $PrefixPattern";
        cmd.Parameters.AddWithValue("$BucketName", bucketName);
        cmd.Parameters.AddWithValue("$PrefixPattern", parentPrefix + "%");

        var subfolderPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            int parentLen = parentPrefix.Length;
            while (await reader.ReadAsync(ct))
            {
                string key = reader.GetString(0);
                if (key.Length > parentLen)
                {
                    string sub = key.Substring(parentLen);
                    int slashIdx = sub.IndexOf('/');
                    if (slashIdx >= 0)
                    {
                        string folderPrefix = parentPrefix + sub.Substring(0, slashIdx + 1);
                        subfolderPrefixes.Add(folderPrefix);
                    }
                }
            }
        }

        var results = new List<FolderSummary>();

        foreach (var folder in subfolderPrefixes)
        {
            using var statCmd = conn.CreateCommand();
            statCmd.CommandText = @"
                SELECT 
                    COUNT(*), 
                    COALESCE(SUM(SizeBytes), 0),
                    COALESCE(MAX(SizeBytes), 0),
                    MAX(LastModified)
                FROM IndexedObjects
                WHERE BucketName = $BucketName AND Key LIKE $FolderPattern AND IsFolder = 0;";
            statCmd.Parameters.AddWithValue("$BucketName", bucketName);
            statCmd.Parameters.AddWithValue("$FolderPattern", folder + "%");

            using var statReader = await statCmd.ExecuteReaderAsync(ct);
            if (await statReader.ReadAsync(ct))
            {
                int count = statReader.GetInt32(0);
                long size = statReader.GetInt64(1);
                long maxSize = statReader.GetInt64(2);
                string? maxModStr = statReader.IsDBNull(3) ? null : statReader.GetString(3);
                DateTime? lastMod = !string.IsNullOrEmpty(maxModStr) && DateTime.TryParse(maxModStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt) ? dt : null;

                double pct = totalBucketBytes > 0 ? ((double)size / totalBucketBytes) * 100.0 : 0.0;

                results.Add(new FolderSummary
                {
                    Prefix = folder,
                    FullPath = folder,
                    Name = PathUtilities.GetS3Name(folder),
                    RecursiveObjectCount = count,
                    RecursiveSizeBytes = size,
                    LargestObjectSizeBytes = maxSize,
                    PercentageOfBucket = pct,
                    LastModified = lastMod
                });
            }
        }

        return results.OrderByDescending(f => f.RecursiveSizeBytes).ToList();
    }

    public async Task<List<IndexedObject>> GetObjectsInFolderAsync(string bucketName, string prefix, CancellationToken ct = default)
    {
        prefix = PathUtilities.NormalizeS3Prefix(prefix);
        using var conn = _dbContext.CreateConnection();
        await conn.OpenAsync(ct);

        using var cmd = conn.CreateCommand();
        // Exact prefix match for immediate items (Prefix column stored as folder)
        cmd.CommandText = @"
            SELECT Id, BucketName, Key, Prefix, FileName, Extension, SizeBytes, ETag, LastModified, ContentType, StorageClass, IsFolder, Category, ScannedAt
            FROM IndexedObjects
            WHERE BucketName = $BucketName AND Prefix = $Prefix AND IsFolder = 0
            ORDER BY FileName ASC;";
        cmd.Parameters.AddWithValue("$BucketName", bucketName);
        cmd.Parameters.AddWithValue("$Prefix", prefix);

        var list = new List<IndexedObject>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(ReadIndexedObject(reader));
        }

        return list;
    }

    public async Task<List<IndexedObject>> GetLargestFilesAsync(string bucketName, int count = 100, CancellationToken ct = default)
    {
        using var conn = _dbContext.CreateConnection();
        await conn.OpenAsync(ct);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, BucketName, Key, Prefix, FileName, Extension, SizeBytes, ETag, LastModified, ContentType, StorageClass, IsFolder, Category, ScannedAt
            FROM IndexedObjects
            WHERE BucketName = $BucketName AND IsFolder = 0
            ORDER BY SizeBytes DESC
            LIMIT $Limit;";
        cmd.Parameters.AddWithValue("$BucketName", bucketName);
        cmd.Parameters.AddWithValue("$Limit", count);

        var list = new List<IndexedObject>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(ReadIndexedObject(reader));
        }

        return list;
    }

    public async Task<List<FolderSummary>> GetLargestFoldersAsync(string bucketName, int count = 50, CancellationToken ct = default)
    {
        long totalBucketBytes = await GetTotalIndexedSizeBytesAsync(bucketName, ct);

        using var conn = _dbContext.CreateConnection();
        await conn.OpenAsync(ct);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Prefix, COUNT(*), SUM(SizeBytes), MAX(SizeBytes), MAX(LastModified)
            FROM IndexedObjects
            WHERE BucketName = $BucketName AND IsFolder = 0 AND Prefix != ''
            GROUP BY Prefix
            ORDER BY SUM(SizeBytes) DESC
            LIMIT $Limit;";
        cmd.Parameters.AddWithValue("$BucketName", bucketName);
        cmd.Parameters.AddWithValue("$Limit", count);

        var list = new List<FolderSummary>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            string pfx = reader.GetString(0);
            int objCount = reader.GetInt32(1);
            long totalBytes = reader.GetInt64(2);
            long maxByte = reader.GetInt64(3);
            string? modStr = reader.IsDBNull(4) ? null : reader.GetString(4);
            DateTime? lastMod = !string.IsNullOrEmpty(modStr) && DateTime.TryParse(modStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt) ? dt : null;

            double pct = totalBucketBytes > 0 ? ((double)totalBytes / totalBucketBytes) * 100.0 : 0.0;

            list.Add(new FolderSummary
            {
                Prefix = pfx,
                FullPath = pfx,
                Name = PathUtilities.GetS3Name(pfx),
                RecursiveObjectCount = objCount,
                RecursiveSizeBytes = totalBytes,
                LargestObjectSizeBytes = maxByte,
                PercentageOfBucket = pct,
                LastModified = lastMod
            });
        }

        return list;
    }

    public async Task<List<DuplicateCandidate>> FindDuplicateCandidatesAsync(string bucketName, int minDuplicateCount = 2, CancellationToken ct = default)
    {
        using var conn = _dbContext.CreateConnection();
        await conn.OpenAsync(ct);

        // Find potential duplicates grouped by SizeBytes and ETag
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT SizeBytes, ETag, COUNT(*)
            FROM IndexedObjects
            WHERE BucketName = $BucketName AND IsFolder = 0 AND SizeBytes > 0 AND ETag IS NOT NULL AND ETag != ''
            GROUP BY SizeBytes, ETag
            HAVING COUNT(*) >= $MinCount
            ORDER BY (COUNT(*) - 1) * SizeBytes DESC
            LIMIT 200;";
        cmd.Parameters.AddWithValue("$BucketName", bucketName);
        cmd.Parameters.AddWithValue("$MinCount", minDuplicateCount);

        var groups = new List<(long Size, string ETag, int Count)>();
        using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                groups.Add((reader.GetInt64(0), reader.GetString(1), reader.GetInt32(2)));
            }
        }

        var duplicates = new List<DuplicateCandidate>();

        foreach (var grp in groups)
        {
            using var fileCmd = conn.CreateCommand();
            fileCmd.CommandText = @"
                SELECT Id, BucketName, Key, Prefix, FileName, Extension, SizeBytes, ETag, LastModified, ContentType, StorageClass, IsFolder, Category, ScannedAt
                FROM IndexedObjects
                WHERE BucketName = $BucketName AND SizeBytes = $Size AND ETag = $ETag
                ORDER BY Key ASC;";
            fileCmd.Parameters.AddWithValue("$BucketName", bucketName);
            fileCmd.Parameters.AddWithValue("$Size", grp.Size);
            fileCmd.Parameters.AddWithValue("$ETag", grp.ETag);

            var objList = new List<IndexedObject>();
            using (var reader = await fileCmd.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                {
                    objList.Add(ReadIndexedObject(reader));
                }
            }

            duplicates.Add(new DuplicateCandidate
            {
                SizeBytes = grp.Size,
                ETagOrHash = grp.ETag,
                FileCount = grp.Count,
                Objects = objList
            });
        }

        return duplicates;
    }

    public async Task<List<IndexedObject>> SearchObjectsAsync(string bucketName, string searchTerm, string? extension = null, long? minSizeBytes = null, CancellationToken ct = default)
    {
        using var conn = _dbContext.CreateConnection();
        await conn.OpenAsync(ct);

        var whereClauses = new List<string> { "BucketName = $BucketName", "IsFolder = 0" };
        using var cmd = conn.CreateCommand();
        cmd.Parameters.AddWithValue("$BucketName", bucketName);

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            whereClauses.Add("(FileName LIKE $SearchTerm OR Key LIKE $SearchTerm)");
            cmd.Parameters.AddWithValue("$SearchTerm", "%" + searchTerm + "%");
        }

        if (!string.IsNullOrWhiteSpace(extension))
        {
            whereClauses.Add("Extension = $Extension");
            cmd.Parameters.AddWithValue("$Extension", extension.StartsWith('.') ? extension : "." + extension);
        }

        if (minSizeBytes.HasValue)
        {
            whereClauses.Add("SizeBytes >= $MinSize");
            cmd.Parameters.AddWithValue("$MinSize", minSizeBytes.Value);
        }

        cmd.CommandText = $@"
            SELECT Id, BucketName, Key, Prefix, FileName, Extension, SizeBytes, ETag, LastModified, ContentType, StorageClass, IsFolder, Category, ScannedAt
            FROM IndexedObjects
            WHERE {string.Join(" AND ", whereClauses)}
            ORDER BY SizeBytes DESC
            LIMIT 500;";

        var list = new List<IndexedObject>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(ReadIndexedObject(reader));
        }

        return list;
    }

    public async Task<long> GetTotalIndexedSizeBytesAsync(string bucketName, CancellationToken ct = default)
    {
        using var conn = _dbContext.CreateConnection();
        await conn.OpenAsync(ct);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(SUM(SizeBytes), 0) FROM IndexedObjects WHERE BucketName = $BucketName AND IsFolder = 0;";
        cmd.Parameters.AddWithValue("$BucketName", bucketName);

        var val = await cmd.ExecuteScalarAsync(ct);
        return val is long l ? l : Convert.ToInt64(val);
    }

    public async Task<int> GetTotalIndexedObjectsCountAsync(string bucketName, CancellationToken ct = default)
    {
        using var conn = _dbContext.CreateConnection();
        await conn.OpenAsync(ct);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM IndexedObjects WHERE BucketName = $BucketName AND IsFolder = 0;";
        cmd.Parameters.AddWithValue("$BucketName", bucketName);

        var val = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(val);
    }

    public async Task RemoveObjectsAsync(string bucketName, IEnumerable<string> keys, CancellationToken ct = default)
    {
        var keyList = keys.ToList();
        if (keyList.Count == 0) return;

        using var conn = _dbContext.CreateConnection();
        await conn.OpenAsync(ct);
        using var tx = conn.BeginTransaction();

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "DELETE FROM IndexedObjects WHERE BucketName = $BucketName AND Key = $Key;";
        cmd.Parameters.AddWithValue("$BucketName", bucketName);
        var pKey = cmd.CreateParameter();
        pKey.ParameterName = "$Key";
        cmd.Parameters.Add(pKey);

        foreach (var key in keyList)
        {
            pKey.Value = key;
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
    }

    public async Task ClearBucketIndexAsync(string bucketName, CancellationToken ct = default)
    {
        using var conn = _dbContext.CreateConnection();
        await conn.OpenAsync(ct);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM IndexedObjects WHERE BucketName = $BucketName;";
        cmd.Parameters.AddWithValue("$BucketName", bucketName);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static IndexedObject ReadIndexedObject(DbDataReader reader)
    {
        string? modStr = reader.IsDBNull(8) ? null : reader.GetString(8);
        DateTime? lastMod = !string.IsNullOrEmpty(modStr) && DateTime.TryParse(modStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt) ? dt : null;
        DateTime scannedAt = DateTime.Parse(reader.GetString(13), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

        return new IndexedObject
        {
            Id = reader.GetInt64(0),
            BucketName = reader.GetString(1),
            Key = reader.GetString(2),
            Prefix = reader.GetString(3),
            FileName = reader.GetString(4),
            Extension = reader.GetString(5),
            SizeBytes = reader.GetInt64(6),
            ETag = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
            LastModified = lastMod,
            ContentType = reader.IsDBNull(9) ? string.Empty : reader.GetString(9),
            StorageClass = reader.IsDBNull(10) ? "STANDARD" : reader.GetString(10),
            IsFolder = reader.GetInt32(11) == 1,
            Category = (FileCategory)reader.GetInt32(12),
            ScannedAt = scannedAt
        };
    }
}
