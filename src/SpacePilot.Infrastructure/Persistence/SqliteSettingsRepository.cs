using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using SpacePilot.Core.Enums;
using SpacePilot.Core.Interfaces;
using SpacePilot.Core.Models;

namespace SpacePilot.Infrastructure.Persistence;

public class SqliteSettingsRepository : ISettingsService
{
    private readonly DatabaseContext _dbContext;
    private readonly ICredentialProtectionService _credentialService;
    private readonly ILogger<SqliteSettingsRepository> _logger;

    public SqliteSettingsRepository(
        DatabaseContext dbContext,
        ICredentialProtectionService credentialService,
        ILogger<SqliteSettingsRepository> logger)
    {
        _dbContext = dbContext;
        _credentialService = credentialService;
        _logger = logger;
    }

    public async Task<ApplicationSettings> LoadSettingsAsync(CancellationToken ct = default)
    {
        await _dbContext.InitializeAsync(ct);
        using var conn = _dbContext.CreateConnection();
        await conn.OpenAsync(ct);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT SettingsJson FROM ApplicationSettings WHERE Id = 1;";

        var json = await cmd.ExecuteScalarAsync(ct) as string;
        if (string.IsNullOrWhiteSpace(json))
        {
            var defaultSettings = new ApplicationSettings();
            await SaveSettingsAsync(defaultSettings, ct);
            return defaultSettings;
        }

        try
        {
            return JsonSerializer.Deserialize<ApplicationSettings>(json) ?? new ApplicationSettings();
        }
        catch
        {
            return new ApplicationSettings();
        }
    }

    public async Task SaveSettingsAsync(ApplicationSettings settings, CancellationToken ct = default)
    {
        await _dbContext.InitializeAsync(ct);
        using var conn = _dbContext.CreateConnection();
        await conn.OpenAsync(ct);

        string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO ApplicationSettings (Id, SettingsJson, UpdatedAt)
            VALUES (1, $Json, $UpdatedAt)
            ON CONFLICT(Id) DO UPDATE SET SettingsJson = $Json, UpdatedAt = $UpdatedAt;";
        cmd.Parameters.AddWithValue("$Json", json);
        cmd.Parameters.AddWithValue("$UpdatedAt", DateTime.UtcNow.ToString("o"));

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<List<StorageProfile>> GetProfilesAsync(CancellationToken ct = default)
    {
        await _dbContext.InitializeAsync(ct);
        using var conn = _dbContext.CreateConnection();
        await conn.OpenAsync(ct);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, Name, ProviderType, ServiceEndpoint, Region, AccessKeyId, SecretAccessKeyEncrypted,
                   BucketName, ForcePathStyle, UseSsl, TimeoutSeconds, RetryCount, CreatedAt, LastConnectedAt
            FROM StorageProfiles
            ORDER BY Name ASC;";

        var list = new List<StorageProfile>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            string encSecret = reader.GetString(6);
            string plainSecret = _credentialService.UnprotectSecret(encSecret);

            string? lastConnStr = reader.IsDBNull(13) ? null : reader.GetString(13);
            DateTime? lastConn = !string.IsNullOrEmpty(lastConnStr) && DateTime.TryParse(lastConnStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt) ? dt : null;

            list.Add(new StorageProfile
            {
                Id = Guid.Parse(reader.GetString(0)),
                Name = reader.GetString(1),
                ProviderType = (StorageProviderType)reader.GetInt32(2),
                ServiceEndpoint = reader.GetString(3),
                Region = reader.GetString(4),
                AccessKeyId = reader.GetString(5),
                SecretAccessKey = plainSecret,
                BucketName = reader.GetString(7),
                ForcePathStyle = reader.GetInt32(8) == 1,
                UseSsl = reader.GetInt32(9) == 1,
                TimeoutSeconds = reader.GetInt32(10),
                RetryCount = reader.GetInt32(11),
                CreatedAt = DateTime.Parse(reader.GetString(12), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                LastConnectedAt = lastConn
            });
        }

        if (list.Count == 0)
        {
            // Seed a default profile for DigitalOcean Spaces
            var defaultProfile = new StorageProfile
            {
                Name = "DigitalOcean Spaces (BLR1)",
                ProviderType = StorageProviderType.DigitalOceanSpaces,
                ServiceEndpoint = "https://blr1.digitaloceanspaces.com",
                Region = "blr1",
                BucketName = "docs456",
                ForcePathStyle = false,
                UseSsl = true
            };
            await SaveProfileAsync(defaultProfile, ct);
            list.Add(defaultProfile);
        }

        return list;
    }

    public async Task SaveProfileAsync(StorageProfile profile, CancellationToken ct = default)
    {
        await _dbContext.InitializeAsync(ct);
        using var conn = _dbContext.CreateConnection();
        await conn.OpenAsync(ct);

        string encSecret = _credentialService.ProtectSecret(profile.SecretAccessKey);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO StorageProfiles (
                Id, Name, ProviderType, ServiceEndpoint, Region, AccessKeyId, SecretAccessKeyEncrypted,
                BucketName, ForcePathStyle, UseSsl, TimeoutSeconds, RetryCount, CreatedAt, LastConnectedAt
            ) VALUES (
                $Id, $Name, $ProviderType, $ServiceEndpoint, $Region, $AccessKeyId, $EncSecret,
                $BucketName, $ForcePathStyle, $UseSsl, $TimeoutSeconds, $RetryCount, $CreatedAt, $LastConnectedAt
            )
            ON CONFLICT(Id) DO UPDATE SET
                Name = $Name,
                ProviderType = $ProviderType,
                ServiceEndpoint = $ServiceEndpoint,
                Region = $Region,
                AccessKeyId = $AccessKeyId,
                SecretAccessKeyEncrypted = $EncSecret,
                BucketName = $BucketName,
                ForcePathStyle = $ForcePathStyle,
                UseSsl = $UseSsl,
                TimeoutSeconds = $TimeoutSeconds,
                RetryCount = $RetryCount,
                LastConnectedAt = $LastConnectedAt;";

        cmd.Parameters.AddWithValue("$Id", profile.Id.ToString());
        cmd.Parameters.AddWithValue("$Name", profile.Name);
        cmd.Parameters.AddWithValue("$ProviderType", (int)profile.ProviderType);
        cmd.Parameters.AddWithValue("$ServiceEndpoint", profile.ServiceEndpoint);
        cmd.Parameters.AddWithValue("$Region", profile.Region);
        cmd.Parameters.AddWithValue("$AccessKeyId", profile.AccessKeyId);
        cmd.Parameters.AddWithValue("$EncSecret", encSecret);
        cmd.Parameters.AddWithValue("$BucketName", profile.BucketName);
        cmd.Parameters.AddWithValue("$ForcePathStyle", profile.ForcePathStyle ? 1 : 0);
        cmd.Parameters.AddWithValue("$UseSsl", profile.UseSsl ? 1 : 0);
        cmd.Parameters.AddWithValue("$TimeoutSeconds", profile.TimeoutSeconds);
        cmd.Parameters.AddWithValue("$RetryCount", profile.RetryCount);
        cmd.Parameters.AddWithValue("$CreatedAt", profile.CreatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("$LastConnectedAt", profile.LastConnectedAt.HasValue ? profile.LastConnectedAt.Value.ToString("o") : (object)DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteProfileAsync(Guid profileId, CancellationToken ct = default)
    {
        await _dbContext.InitializeAsync(ct);
        using var conn = _dbContext.CreateConnection();
        await conn.OpenAsync(ct);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM StorageProfiles WHERE Id = $Id;";
        cmd.Parameters.AddWithValue("$Id", profileId.ToString());

        await cmd.ExecuteNonQueryAsync(ct);
    }
}
