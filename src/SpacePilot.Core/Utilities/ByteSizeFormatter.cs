using System.Globalization;

namespace SpacePilot.Core.Utilities;

public static class ByteSizeFormatter
{
    private static readonly string[] SizeSuffixes = { "B", "KB", "MB", "GB", "TB", "PB" };

    public static string Format(long bytes)
    {
        if (bytes == 0) return "0 B";
        if (bytes < 0) return "-" + Format(-bytes);

        int mag = (int)Math.Max(0, Math.Min(Math.Floor(Math.Log(bytes, 1024)), SizeSuffixes.Length - 1));
        double adjustedSize = bytes / Math.Pow(1024, mag);

        string format = mag == 0 ? "0" : (adjustedSize >= 100 ? "0.0" : "0.00");
        return string.Format(CultureInfo.InvariantCulture, "{0:" + format + "} {1}", adjustedSize, SizeSuffixes[mag]);
    }

    public static string FormatSavings(long originalBytes, long processedBytes)
    {
        long saved = originalBytes - processedBytes;
        double pct = originalBytes > 0 ? (double)saved / originalBytes * 100.0 : 0.0;
        return string.Format(CultureInfo.InvariantCulture, "{0} ({1:0.0}%)", Format(saved), pct);
    }

    public static double CalculateSavingsPercentage(long originalBytes, long processedBytes)
    {
        if (originalBytes <= 0) return 0.0;
        double saved = originalBytes - processedBytes;
        return Math.Max(0.0, (saved / originalBytes) * 100.0);
    }
}
