using Microsoft.Extensions.Logging.Abstractions;
using SpacePilot.Core.Enums;
using SpacePilot.Core.Models;
using SpacePilot.Infrastructure.Persistence;
using SpacePilot.Infrastructure.Security;
using Xunit;

namespace SpacePilot.UnitTests;

public class SecurityAndPersistenceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseContext _dbContext;
    private readonly DpapiCredentialProtectionService _dpapi;
    private readonly SqliteSettingsRepository _settingsRepo;
    private readonly SqliteJobRepository _jobRepo;

    public SecurityAndPersistenceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"sec_test_{Guid.NewGuid():N}.db");
        _dbContext = new DatabaseContext(_dbPath, NullLogger<DatabaseContext>.Instance);
        _dpapi = new DpapiCredentialProtectionService();
        _settingsRepo = new SqliteSettingsRepository(_dbContext, _dpapi, NullLogger<SqliteSettingsRepository>.Instance);
        _jobRepo = new SqliteJobRepository(_dbContext, NullLogger<SqliteJobRepository>.Instance);
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
    }

    [Fact]
    public void Dpapi_EncryptsAndDecryptsSecretSuccessfully()
    {
        string plainSecret = "SECRET_ACCESS_KEY_DO00_abcdef123456789";
        string encrypted = _dpapi.ProtectSecret(plainSecret);

        Assert.NotEmpty(encrypted);
        Assert.NotEqual(plainSecret, encrypted);

        string decrypted = _dpapi.UnprotectSecret(encrypted);
        Assert.Equal(plainSecret, decrypted);
    }

    [Fact]
    public async Task SettingsRepo_PersistsProfileWithEncryptedSecret()
    {
        var profile = new StorageProfile
        {
            Name = "DigitalOcean Production",
            ProviderType = StorageProviderType.DigitalOceanSpaces,
            ServiceEndpoint = "https://blr1.digitaloceanspaces.com",
            Region = "blr1",
            AccessKeyId = "DO00XYZ123",
            SecretAccessKey = "SuperSecretDOKey_999",
            BucketName = "docs456"
        };

        await _settingsRepo.SaveProfileAsync(profile);

        var loadedProfiles = await _settingsRepo.GetProfilesAsync();
        var match = loadedProfiles.FirstOrDefault(p => p.Name == "DigitalOcean Production");

        Assert.NotNull(match);
        Assert.Equal("DO00XYZ123", match.AccessKeyId);
        Assert.Equal("SuperSecretDOKey_999", match.SecretAccessKey);
    }

    [Fact]
    public async Task JobRepo_PersistsJobAndItemsWithStateUpdates()
    {
        var job = new CompressionJob
        {
            Title = "Test Persistence Job",
            SourceType = "Local",
            SourcePathOrBucket = "D:\\Test",
            Status = JobStatus.Running,
            TotalFiles = 2,
            OriginalBytes = 20000
        };

        var items = new List<JobItem>
        {
            new() { JobId = job.Id, SourceKeyOrPath = "file1.jpg", FileType = FileCategory.Image, Status = JobItemStatus.Queued, OriginalSizeBytes = 10000 },
            new() { JobId = job.Id, SourceKeyOrPath = "file2.jpg", FileType = FileCategory.Image, Status = JobItemStatus.Queued, OriginalSizeBytes = 10000 }
        };

        await _jobRepo.SaveJobAsync(job, items);

        var loadedJobs = await _jobRepo.GetAllJobsAsync();
        Assert.Contains(loadedJobs, j => j.Id == job.Id);

        var loadedItems = await _jobRepo.GetJobItemsAsync(job.Id);
        Assert.Equal(2, loadedItems.Count);

        // Update item 1 to compressed
        var item1 = loadedItems[0];
        item1.Status = JobItemStatus.Compressed;
        item1.ProcessedSizeBytes = 6000;
        await _jobRepo.UpdateJobItemAsync(item1);

        var reloadedItems = await _jobRepo.GetJobItemsAsync(job.Id);
        Assert.Equal(JobItemStatus.Compressed, reloadedItems[0].Status);
        Assert.Equal(6000, reloadedItems[0].ProcessedSizeBytes);
        Assert.Equal(4000, reloadedItems[0].SavedSizeBytes);
    }
}
