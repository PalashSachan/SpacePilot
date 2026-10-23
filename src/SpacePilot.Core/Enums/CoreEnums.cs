namespace SpacePilot.Core.Enums;

public enum StorageProviderType
{
    DigitalOceanSpaces,
    AmazonS3,
    CustomS3
}

public enum CompressionMode
{
    LosslessFirst,
    VisuallyOptimized,
    MaximumSavings
}

public enum ResizeMode
{
    None,
    MaxWidthHeight,
    Percentage,
    LongestEdge,
    ExactFit
}

public enum TargetImageFormat
{
    Original,
    Jpeg,
    Png,
    WebP
}

public enum FileCategory
{
    Image,
    Pdf,
    Other
}

public enum JobStatus
{
    Queued,
    Running,
    Paused,
    Completed,
    Failed,
    Cancelled
}

public enum JobItemStatus
{
    Queued,
    Processing,
    SkippedBelowThreshold,
    SkippedUnsupported,
    SkippedAlreadyProcessed,
    CheckedNoSavings,
    PreviewReady,
    Compressed,
    UploadedVerified,
    Failed,
    Cancelled
}

public enum UploadMode
{
    DryRun,
    StagingPrefix,
    ReplaceOriginal
}

public enum MetadataPolicy
{
    PreserveAll,
    StripNonEssential,
    StripAll
}

public enum CollisionPolicy
{
    Skip,
    Overwrite,
    RenameWithSuffix
}

public enum AppTheme
{
    Dark,
    Light,
    System
}
