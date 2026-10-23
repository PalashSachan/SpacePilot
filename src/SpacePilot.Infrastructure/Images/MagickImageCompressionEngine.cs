using ImageMagick;
using Microsoft.Extensions.Logging;
using SpacePilot.Core.Enums;
using SpacePilot.Core.Interfaces;
using SpacePilot.Core.Models;

namespace SpacePilot.Infrastructure.Images;

public class MagickImageCompressionEngine : IImageCompressionEngine
{
    private readonly ILogger<MagickImageCompressionEngine> _logger;

    public MagickImageCompressionEngine(ILogger<MagickImageCompressionEngine> logger)
    {
        _logger = logger;
    }

    public async Task<CompressionResult> CompressImageAsync(
        string sourcePath,
        string outputPath,
        CompressionProfile profile,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var result = new CompressionResult
            {
                SourcePath = sourcePath,
                OutputPath = outputPath
            };

            if (!File.Exists(sourcePath))
            {
                result.Success = false;
                result.ErrorMessage = $"Source file not found: {sourcePath}";
                return result;
            }

            var origInfo = new FileInfo(sourcePath);
            result.OriginalSizeBytes = origInfo.Length;

            try
            {
                using var image = new MagickImage(sourcePath);

                result.OriginalWidth = (int)image.Width;
                result.OriginalHeight = (int)image.Height;

                // 1. Auto-orient if requested (reads EXIF orientation and normalizes pixels)
                if (profile.AutoOrientExif)
                {
                    image.AutoOrient();
                }

                // 2. Apply resizing
                ApplyResizing(image, profile);

                // 3. Target format determination
                MagickFormat targetFormat = DetermineTargetFormat(sourcePath, profile.TargetFormat);

                // Handle transparency if converting transparent image to JPEG
                if ((targetFormat == MagickFormat.Jpeg || targetFormat == MagickFormat.Jpg) && image.HasAlpha)
                {
                    image.BackgroundColor = MagickColors.White;
                    image.HasAlpha = false;
                    result.IsLossy = true;
                }

                // 4. Quality & Compression mode settings
                bool isLossy = false;
                switch (profile.Mode)
                {
                    case CompressionMode.LosslessFirst:
                        image.Quality = 100;
                        if (targetFormat == MagickFormat.Png)
                        {
                            image.Settings.SetDefine(MagickFormat.Png, "compression-level", "9");
                            image.Settings.SetDefine(MagickFormat.Png, "compression-filter", "5");
                        }
                        else if (targetFormat == MagickFormat.WebP)
                        {
                            image.Settings.SetDefine(MagickFormat.WebP, "lossless", "true");
                        }
                        break;

                    case CompressionMode.VisuallyOptimized:
                    case CompressionMode.MaximumSavings:
                        isLossy = true;
                        image.Quality = (uint)Math.Clamp(profile.Quality, 1, 100);
                        if (targetFormat == MagickFormat.Jpeg || targetFormat == MagickFormat.Jpg)
                        {
                            image.Settings.Interlace = Interlace.Plane; // Progressive JPEG
                        }
                        break;
                }

                if (profile.ResizeMode != ResizeMode.None)
                {
                    isLossy = true;
                }

                result.IsLossy = isLossy;

                // 5. Metadata handling
                switch (profile.MetadataPolicy)
                {
                    case MetadataPolicy.StripAll:
                        image.Strip();
                        break;
                    case MetadataPolicy.StripNonEssential:
                        // Keep ICC color profile if present, strip other tags
                        var profileIcc = image.GetColorProfile();
                        image.Strip();
                        if (profileIcc != null)
                        {
                            image.SetProfile(profileIcc);
                        }
                        break;
                    case MetadataPolicy.PreserveAll:
                        // Leave untouched
                        break;
                }

                // Ensure target directory exists
                string? outDir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(outDir))
                    Directory.CreateDirectory(outDir);

                // 6. Target file size bounded search if specified
                if (profile.TargetFileSizeBytes.HasValue && profile.TargetFileSizeBytes.Value > 0 &&
                    (targetFormat == MagickFormat.Jpeg || targetFormat == MagickFormat.Jpg || targetFormat == MagickFormat.WebP))
                {
                    ExecuteTargetSizeSearch(image, targetFormat, outputPath, profile.TargetFileSizeBytes.Value);
                }
                else
                {
                    image.Format = targetFormat;
                    image.Write(outputPath);
                }

                // 7. Validate generated file
                var outInfo = new FileInfo(outputPath);
                result.ProcessedSizeBytes = outInfo.Length;
                result.ProcessedWidth = (int)image.Width;
                result.ProcessedHeight = (int)image.Height;

                using var verifyImage = new MagickImage(outputPath);
                result.Success = verifyImage.Width > 0 && verifyImage.Height > 0;

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error compressing image '{SourcePath}'", sourcePath);
                result.Success = false;
                result.ErrorMessage = ex.Message;
                return result;
            }
        }, ct);
    }

    public async Task<ValidationResult> ValidateImageAsync(string filePath, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            if (!File.Exists(filePath))
            {
                return new ValidationResult { IsValid = false, ErrorMessage = "File does not exist." };
            }

            try
            {
                var info = new MagickImageInfo(filePath);
                return new ValidationResult
                {
                    IsValid = true,
                    Width = (int)info.Width,
                    Height = (int)info.Height,
                    Format = info.Format.ToString()
                };
            }
            catch (Exception ex)
            {
                return new ValidationResult { IsValid = false, ErrorMessage = ex.Message };
            }
        }, ct);
    }

    private static void ApplyResizing(MagickImage image, CompressionProfile profile)
    {
        switch (profile.ResizeMode)
        {
            case ResizeMode.None:
                break;

            case ResizeMode.MaxWidthHeight:
                var maxGeo = new MagickGeometry((uint)profile.MaxWidth, (uint)profile.MaxHeight)
                {
                    IgnoreAspectRatio = !profile.PreserveAspectRatio
                };
                image.Resize(maxGeo);
                break;

            case ResizeMode.Percentage:
                double pct = Math.Clamp(profile.ResizePercentage, 1.0, 1000.0);
                image.Resize(new Percentage(pct));
                break;

            case ResizeMode.LongestEdge:
                uint edge = (uint)Math.Max(10, profile.LongestEdge);
                if (image.Width >= image.Height)
                {
                    image.Resize(edge, 0);
                }
                else
                {
                    image.Resize(0, edge);
                }
                break;

            case ResizeMode.ExactFit:
                var exactGeo = new MagickGeometry((uint)profile.MaxWidth, (uint)profile.MaxHeight)
                {
                    IgnoreAspectRatio = !profile.PreserveAspectRatio
                };
                image.Resize(exactGeo);
                break;
        }
    }

    private static MagickFormat DetermineTargetFormat(string sourcePath, TargetImageFormat targetFormat)
    {
        return targetFormat switch
        {
            TargetImageFormat.Jpeg => MagickFormat.Jpeg,
            TargetImageFormat.Png => MagickFormat.Png,
            TargetImageFormat.WebP => MagickFormat.WebP,
            _ => MagickFormatInfo.Create(sourcePath)?.Format ?? MagickFormat.Unknown
        };
    }

    private static void ExecuteTargetSizeSearch(MagickImage image, MagickFormat format, string outputPath, long targetBytes)
    {
        // Binary search on quality to find the best quality that fits in targetBytes
        uint minQ = 10;
        uint maxQ = 95;
        uint bestQ = minQ;

        image.Format = format;

        for (int i = 0; i < 6; i++)
        {
            uint midQ = (minQ + maxQ) / 2;
            image.Quality = midQ;
            using var ms = new MemoryStream();
            image.Write(ms);

            if (ms.Length <= targetBytes)
            {
                bestQ = midQ;
                minQ = midQ + 1; // Try higher quality
            }
            else
            {
                if (midQ <= minQ) break;
                maxQ = midQ - 1; // Reduce quality
            }
        }

        image.Quality = bestQ;
        image.Write(outputPath);
    }
}
