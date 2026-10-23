using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace SpacePilot.Infrastructure.Persistence;

public class DatabaseContext
{
    private readonly string _connectionString;
    private readonly ILogger<DatabaseContext> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _initialized;

    public DatabaseContext(string? dbPath, ILogger<DatabaseContext> logger)
    {
        _logger = logger;
        if (string.IsNullOrWhiteSpace(dbPath))
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string spacePilotDir = Path.Combine(appData, "SpacePilot");
            Directory.CreateDirectory(spacePilotDir);
            dbPath = Path.Combine(spacePilotDir, "spacepilot.db");
        }

        string? dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public SqliteConnection CreateConnection()
    {
        return new SqliteConnection(_connectionString);
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized) return;

        await _lock.WaitAsync(ct);
        try
        {
            if (_initialized) return;

            using var connection = CreateConnection();
            await connection.OpenAsync(ct);

            // Enable WAL mode for high-throughput concurrency
            using (var walCmd = connection.CreateCommand())
            {
                walCmd.CommandText = "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL;";
                await walCmd.ExecuteNonQueryAsync(ct);
            }

            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS IndexedObjects (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    BucketName TEXT NOT NULL,
                    Key TEXT NOT NULL,
                    Prefix TEXT NOT NULL,
                    FileName TEXT NOT NULL,
                    Extension TEXT NOT NULL,
                    SizeBytes INTEGER NOT NULL,
                    ETag TEXT,
                    LastModified TEXT,
                    ContentType TEXT,
                    StorageClass TEXT,
                    IsFolder INTEGER NOT NULL,
                    Category INTEGER NOT NULL,
                    ScannedAt TEXT NOT NULL
                );

                CREATE INDEX IF NOT EXISTS IX_IndexedObjects_Bucket_Prefix ON IndexedObjects(BucketName, Prefix);
                CREATE INDEX IF NOT EXISTS IX_IndexedObjects_Bucket_Key ON IndexedObjects(BucketName, Key);
                CREATE INDEX IF NOT EXISTS IX_IndexedObjects_Bucket_Size ON IndexedObjects(BucketName, SizeBytes);
                CREATE INDEX IF NOT EXISTS IX_IndexedObjects_Bucket_ETag ON IndexedObjects(BucketName, ETag);
                CREATE INDEX IF NOT EXISTS IX_IndexedObjects_Bucket_Category ON IndexedObjects(BucketName, Category);

                CREATE TABLE IF NOT EXISTS CompressionJobs (
                    Id TEXT PRIMARY KEY,
                    Title TEXT NOT NULL,
                    SourceType TEXT NOT NULL,
                    SourcePathOrBucket TEXT NOT NULL,
                    TargetPrefixOrDirectory TEXT,
                    Status INTEGER NOT NULL,
                    UploadMode INTEGER NOT NULL,
                    TotalFiles INTEGER NOT NULL,
                    ProcessedFiles INTEGER NOT NULL,
                    SkippedFiles INTEGER NOT NULL,
                    FailedFiles INTEGER NOT NULL,
                    OriginalBytes INTEGER NOT NULL,
                    ProcessedBytes INTEGER NOT NULL,
                    StartedAt TEXT NOT NULL,
                    CompletedAt TEXT,
                    ErrorMessage TEXT
                );

                CREATE TABLE IF NOT EXISTS JobItems (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    JobId TEXT NOT NULL,
                    SourceKeyOrPath TEXT NOT NULL,
                    DestinationKeyOrPath TEXT,
                    FileType INTEGER NOT NULL,
                    Status INTEGER NOT NULL,
                    OriginalSizeBytes INTEGER NOT NULL,
                    ProcessedSizeBytes INTEGER NOT NULL,
                    ErrorDetails TEXT,
                    CompletedAt TEXT,
                    IsLossy INTEGER NOT NULL DEFAULT 0,
                    FOREIGN KEY(JobId) REFERENCES CompressionJobs(Id) ON DELETE CASCADE
                );

                CREATE INDEX IF NOT EXISTS IX_JobItems_JobId ON JobItems(JobId);
                CREATE INDEX IF NOT EXISTS IX_JobItems_Status ON JobItems(Status);

                CREATE TABLE IF NOT EXISTS ProcessedFiles (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    SourceIdentifier TEXT UNIQUE NOT NULL,
                    SourceHashOrETag TEXT,
                    OriginalSizeBytes INTEGER NOT NULL,
                    ProcessedSizeBytes INTEGER NOT NULL,
                    ProcessedAt TEXT NOT NULL,
                    ProfileName TEXT
                );

                CREATE INDEX IF NOT EXISTS IX_ProcessedFiles_Source ON ProcessedFiles(SourceIdentifier);

                CREATE TABLE IF NOT EXISTS ActivityLogs (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Timestamp TEXT NOT NULL,
                    SourceBucketOrPath TEXT NOT NULL,
                    ObjectKeyOrFileName TEXT NOT NULL,
                    FileType INTEGER NOT NULL,
                    OriginalSizeBytes INTEGER NOT NULL,
                    ProcessedSizeBytes INTEGER NOT NULL,
                    Action TEXT NOT NULL,
                    Status TEXT NOT NULL,
                    ErrorDetails TEXT
                );

                CREATE INDEX IF NOT EXISTS IX_ActivityLogs_Timestamp ON ActivityLogs(Timestamp);

                CREATE TABLE IF NOT EXISTS StorageProfiles (
                    Id TEXT PRIMARY KEY,
                    Name TEXT NOT NULL,
                    ProviderType INTEGER NOT NULL,
                    ServiceEndpoint TEXT NOT NULL,
                    Region TEXT NOT NULL,
                    AccessKeyId TEXT NOT NULL,
                    SecretAccessKeyEncrypted TEXT NOT NULL,
                    BucketName TEXT NOT NULL,
                    ForcePathStyle INTEGER NOT NULL,
                    UseSsl INTEGER NOT NULL,
                    TimeoutSeconds INTEGER NOT NULL,
                    RetryCount INTEGER NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    LastConnectedAt TEXT
                );

                CREATE TABLE IF NOT EXISTS ApplicationSettings (
                    Id INTEGER PRIMARY KEY CHECK (Id = 1),
                    SettingsJson TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                );
            ";

            await cmd.ExecuteNonQueryAsync(ct);
            _initialized = true;
            _logger.LogInformation("SpacePilot SQLite database initialized successfully.");
        }
        finally
        {
            _lock.Release();
        }
    }
}
