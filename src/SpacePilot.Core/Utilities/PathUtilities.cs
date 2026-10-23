using SpacePilot.Core.Enums;

namespace SpacePilot.Core.Utilities;

public static class PathUtilities
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".tif", ".tiff", ".gif"
    };

    private static readonly HashSet<string> PdfExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf"
    };

    public static FileCategory CategorizeFile(string pathOrKey)
    {
        if (string.IsNullOrWhiteSpace(pathOrKey))
            return FileCategory.Other;

        string ext = Path.GetExtension(pathOrKey);
        if (ImageExtensions.Contains(ext))
            return FileCategory.Image;
        if (PdfExtensions.Contains(ext))
            return FileCategory.Pdf;

        return FileCategory.Other;
    }

    public static bool IsSupportedMedia(string pathOrKey)
    {
        var cat = CategorizeFile(pathOrKey);
        return cat == FileCategory.Image || cat == FileCategory.Pdf;
    }

    public static string NormalizeS3Prefix(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix) || prefix == "/")
            return string.Empty;

        prefix = prefix.Replace('\\', '/').TrimStart('/');
        if (!prefix.EndsWith('/'))
            prefix += "/";

        return prefix;
    }

    public static string GetParentPrefix(string prefix)
    {
        prefix = NormalizeS3Prefix(prefix);
        if (string.IsNullOrEmpty(prefix))
            return string.Empty;

        string trimmed = prefix.TrimEnd('/');
        int lastSlash = trimmed.LastIndexOf('/');
        if (lastSlash < 0)
            return string.Empty;

        return trimmed.Substring(0, lastSlash + 1);
    }

    public static string GetS3Name(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return string.Empty;
        string trimmed = key.TrimEnd('/');
        int lastSlash = trimmed.LastIndexOf('/');
        return lastSlash >= 0 ? trimmed.Substring(lastSlash + 1) : trimmed;
    }

    public static string BuildStagingKey(string stagingPrefix, string originalKey)
    {
        stagingPrefix = NormalizeS3Prefix(stagingPrefix);
        string cleanedOriginal = originalKey.TrimStart('/');
        return stagingPrefix + cleanedOriginal;
    }

    public static string BuildBackupKey(string originalKey, DateTime timestamp)
    {
        string dir = Path.GetDirectoryName(originalKey)?.Replace('\\', '/') ?? string.Empty;
        string nameWithoutExt = Path.GetFileNameWithoutExtension(originalKey);
        string ext = Path.GetExtension(originalKey);
        string backupName = $"{nameWithoutExt}.backup_{timestamp:yyyyMMdd_HHmmss}{ext}";

        return string.IsNullOrEmpty(dir) ? backupName : $"{dir}/{backupName}";
    }

    public static string GetLocalCompressedSiblingPath(string originalFilePath)
    {
        string? dir = Path.GetDirectoryName(originalFilePath);
        string fileName = Path.GetFileName(originalFilePath);

        if (string.IsNullOrEmpty(dir))
            dir = Directory.GetCurrentDirectory();

        string compressedDir = Path.Combine(dir, "compressed");
        return Path.Combine(compressedDir, fileName);
    }

    public static bool IsInsideCompressedSubfolder(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath)) return false;
        string normalized = fullPath.Replace('/', '\\');
        return normalized.Contains("\\compressed\\", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith("\\compressed", StringComparison.OrdinalIgnoreCase);
    }
}
