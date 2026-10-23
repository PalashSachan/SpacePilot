using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using SpacePilot.Core.Enums;
using SpacePilot.Core.Models;

namespace SpacePilot.Infrastructure.S3;

public static class S3ClientFactory
{
    public static IAmazonS3 CreateClient(StorageProfile profile)
    {
        var credentials = new BasicAWSCredentials(profile.AccessKeyId, profile.SecretAccessKey);

        var config = new AmazonS3Config
        {
            Timeout = TimeSpan.FromSeconds(Math.Max(5, profile.TimeoutSeconds)),
            MaxErrorRetry = Math.Max(1, profile.RetryCount),
            UseHttp = !profile.UseSsl,
            ForcePathStyle = profile.ForcePathStyle
        };

        if (!string.IsNullOrWhiteSpace(profile.ServiceEndpoint))
        {
            config.ServiceURL = profile.ServiceEndpoint.TrimEnd('/');
        }

        if (!string.IsNullOrWhiteSpace(profile.Region))
        {
            try
            {
                config.RegionEndpoint = RegionEndpoint.GetBySystemName(profile.Region);
            }
            catch
            {
                // Fallback for custom endpoints (e.g., blr1, nyc3)
                config.AuthenticationRegion = profile.Region;
            }
        }

        return new AmazonS3Client(credentials, config);
    }
}
