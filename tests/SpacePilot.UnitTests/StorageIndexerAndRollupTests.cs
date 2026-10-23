using Microsoft.Extensions.Logging.Abstractions;
using SpacePilot.Core.Enums;
using SpacePilot.Core.Models;
using SpacePilot.Infrastructure.Persistence;
using Xunit;

namespace SpacePilot.UnitTests;

public class StorageIndexerAndRollupTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseContext _dbContext;
    private readonly SqliteStorageIndexer _indexer;

    public StorageIndexerAndRollupTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"indexer_test_{Guid.NewGuid():N}.db");
        _dbContext = new DatabaseContext(_dbPath, NullLogger<DatabaseContext>.Instance);
        _indexer = new SqliteStorageIndexer(_dbContext, NullLogger<SqliteStorageIndexer>.Instance);
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
    }

    [Fact]
    public async Task Indexer_CalculatesRecursiveFolderSizesCorrectly()
    {
        await _indexer.InitializeAsync();
        string bucket = "test-bucket";

        // Seed nested folder structure:
        // images/photo1.jpg (1000 bytes)
        // images/photo2.jpg (2000 bytes)
        // images/sub/photo3.jpg (3000 bytes)
        // documents/report.pdf (5000 bytes)
        var objects = new List<IndexedObject>
        {
            new() { BucketName = bucket, Key = "images/photo1.jpg", Prefix = "images/", FileName = "photo1.jpg", Extension = ".jpg", SizeBytes = 1000, Category = FileCategory.Image },
            new() { BucketName = bucket, Key = "images/photo2.jpg", Prefix = "images/", FileName = "photo2.jpg", Extension = ".jpg", SizeBytes = 2000, Category = FileCategory.Image },
            new() { BucketName = bucket, Key = "images/sub/photo3.jpg", Prefix = "images/sub/", FileName = "photo3.jpg", Extension = ".jpg", SizeBytes = 3000, Category = FileCategory.Image },
            new() { BucketName = bucket, Key = "documents/report.pdf", Prefix = "documents/", FileName = "report.pdf", Extension = ".pdf", SizeBytes = 5000, Category = FileCategory.Pdf }
        };

        await _indexer.InsertObjectsBatchAsync(objects);

        // Verify total bucket size
        long totalBytes = await _indexer.GetTotalIndexedSizeBytesAsync(bucket);
        Assert.Equal(11000, totalBytes);

        int totalCount = await _indexer.GetTotalIndexedObjectsCountAsync(bucket);
        Assert.Equal(4, totalCount);

        // Verify root folder summaries: should return "images/" and "documents/"
        var rootFolders = await _indexer.GetFolderSummariesAsync(bucket, "");
        Assert.Equal(2, rootFolders.Count);

        var imagesSummary = rootFolders.First(f => f.Prefix == "images/");
        // images/ contains photo1 (1000), photo2 (2000) and images/sub/photo3 (3000) = 6000 recursive bytes!
        Assert.Equal(6000, imagesSummary.RecursiveSizeBytes);
        Assert.Equal(3, imagesSummary.RecursiveObjectCount);
        Assert.Equal(3000, imagesSummary.LargestObjectSizeBytes);

        var docsSummary = rootFolders.First(f => f.Prefix == "documents/");
        Assert.Equal(5000, docsSummary.RecursiveSizeBytes);
        Assert.Equal(1, docsSummary.RecursiveObjectCount);
    }

    [Fact]
    public async Task Indexer_FindsDuplicateCandidates()
    {
        await _indexer.InitializeAsync();
        string bucket = "dup-bucket";

        // Seed 3 files with identical size (4096) and identical ETag ("abc123hash")
        var objects = new List<IndexedObject>
        {
            new() { BucketName = bucket, Key = "folder1/file_a.png", Prefix = "folder1/", FileName = "file_a.png", SizeBytes = 4096, ETag = "abc123hash" },
            new() { BucketName = bucket, Key = "folder2/file_b.png", Prefix = "folder2/", FileName = "file_b.png", SizeBytes = 4096, ETag = "abc123hash" },
            new() { BucketName = bucket, Key = "backup/file_c.png", Prefix = "backup/", FileName = "file_c.png", SizeBytes = 4096, ETag = "abc123hash" },
            new() { BucketName = bucket, Key = "unique/file_u.png", Prefix = "unique/", FileName = "file_u.png", SizeBytes = 8192, ETag = "uniquehash" }
        };

        await _indexer.InsertObjectsBatchAsync(objects);

        var duplicates = await _indexer.FindDuplicateCandidatesAsync(bucket, 2);
        Assert.Single(duplicates);

        var dupGroup = duplicates[0];
        Assert.Equal(4096, dupGroup.SizeBytes);
        Assert.Equal(3, dupGroup.FileCount);
        // Wasted space = (3 - 1) * 4096 = 8192 bytes
        Assert.Equal(8192, dupGroup.WastedSizeBytes);
    }

    [Fact]
    public async Task Indexer_SearchesAndFiltersObjects()
    {
        await _indexer.InitializeAsync();
        string bucket = "search-bucket";

        var objects = new List<IndexedObject>
        {
            new() { BucketName = bucket, Key = "finance/invoice_2026.pdf", Prefix = "finance/", FileName = "invoice_2026.pdf", Extension = ".pdf", SizeBytes = 50000 },
            new() { BucketName = bucket, Key = "finance/receipt_2026.jpg", Prefix = "finance/", FileName = "receipt_2026.jpg", Extension = ".jpg", SizeBytes = 12000 },
            new() { BucketName = bucket, Key = "marketing/banner.webp", Prefix = "marketing/", FileName = "banner.webp", Extension = ".webp", SizeBytes = 90000 }
        };

        await _indexer.InsertObjectsBatchAsync(objects);

        var results = await _indexer.SearchObjectsAsync(bucket, "invoice", ".pdf", 10000);
        Assert.Single(results);
        Assert.Equal("invoice_2026.pdf", results[0].FileName);
    }
}
