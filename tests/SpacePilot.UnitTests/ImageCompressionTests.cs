using ImageMagick;
using Microsoft.Extensions.Logging.Abstractions;
using SpacePilot.Application.Compression;
using SpacePilot.Core.Enums;
using SpacePilot.Core.Models;
using SpacePilot.Infrastructure.Images;
using SpacePilot.Infrastructure.Pdf;
using Xunit;

namespace SpacePilot.UnitTests;

public class ImageCompressionTests : IDisposable
{
    private readonly string _tempDir;
    private readonly MagickImageCompressionEngine _engine;
    private readonly UnifiedCompressionService _unifiedEngine;

    public ImageCompressionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SpacePilot_ImgTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _engine = new MagickImageCompressionEngine(NullLogger<MagickImageCompressionEngine>.Instance);
        var pdfEngine = new PdfSharpOptimizationEngine(NullLogger<PdfSharpOptimizationEngine>.Instance);
        _unifiedEngine = new UnifiedCompressionService(_engine, pdfEngine, NullLogger<UnifiedCompressionService>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    private string CreateTestImage(string filename, int width, int height, MagickFormat format, bool transparent = false)
    {
        string path = Path.Combine(_tempDir, filename);
        using var img = new MagickImage(transparent ? MagickColors.Transparent : MagickColors.LightSkyBlue, (uint)width, (uint)height);
        if (transparent)
        {
            using var draw = new MagickImage(MagickColors.Crimson, 50, 50);
            img.Composite(draw, 10, 10, CompositeOperator.Over);
        }
        else
        {
            // Draw some colored noise to give it realistic content
            using var draw = new MagickImage(MagickColors.Coral, (uint)(width / 2), (uint)(height / 2));
            img.Composite(draw, 20, 20, CompositeOperator.Over);
        }
        img.Format = format;
        img.Write(path);
        return path;
    }

    [Fact]
    public async Task CompressImage_Lossless_PreservesDimensionsAndValidates()
    {
        string source = CreateTestImage("test_lossless.png", 800, 600, MagickFormat.Png);
        string output = Path.Combine(_tempDir, "output_lossless.png");

        var profile = CompressionProfile.CreateLossless();
        var result = await _engine.CompressImageAsync(source, output, profile);

        Assert.True(result.Success);
        Assert.Equal(800, result.ProcessedWidth);
        Assert.Equal(600, result.ProcessedHeight);
        Assert.False(result.IsLossy);
        Assert.True(File.Exists(output));
    }

    [Fact]
    public async Task CompressImage_VisuallyOptimized_ResizesLongestEdge()
    {
        string source = CreateTestImage("test_large.jpg", 3000, 2000, MagickFormat.Jpeg);
        string output = Path.Combine(_tempDir, "output_resized.jpg");

        var profile = new CompressionProfile
        {
            Mode = CompressionMode.VisuallyOptimized,
            Quality = 80,
            ResizeMode = ResizeMode.LongestEdge,
            LongestEdge = 1500
        };

        var result = await _engine.CompressImageAsync(source, output, profile);

        Assert.True(result.Success);
        Assert.Equal(1500, result.ProcessedWidth);
        Assert.Equal(1000, result.ProcessedHeight);
        Assert.True(result.IsLossy);
    }

    [Fact]
    public async Task CompressImage_TransparentPngToJpeg_RemovesAlphaAndMarksLossy()
    {
        string source = CreateTestImage("test_alpha.png", 400, 400, MagickFormat.Png, transparent: true);
        string output = Path.Combine(_tempDir, "output_alpha.jpg");

        var profile = new CompressionProfile
        {
            Mode = CompressionMode.VisuallyOptimized,
            Quality = 85,
            TargetFormat = TargetImageFormat.Jpeg
        };

        var result = await _engine.CompressImageAsync(source, output, profile);

        Assert.True(result.Success);
        Assert.True(result.IsLossy);

        // Verify output JPEG has no alpha channel
        using var outImg = new MagickImage(output);
        Assert.False(outImg.HasAlpha);
    }

    [Fact]
    public async Task UnifiedEngine_SkipsImageBelowThreshold()
    {
        // 50x50 small image is well below 200 KB
        string source = CreateTestImage("small.jpg", 50, 50, MagickFormat.Jpeg);
        string output = Path.Combine(_tempDir, "small_out.jpg");

        long minThresholdBytes = 200 * 1024; // 200 KB
        var profile = CompressionProfile.CreateVisuallyOptimized();

        var result = await _unifiedEngine.ProcessFileAsync(source, output, profile, minThresholdBytes, 300 * 1024, 15.0);

        Assert.True(result.Success);
        Assert.False(result.MeetsThreshold);
        Assert.Contains("Skipped", result.ErrorMessage);
    }

    [Fact]
    public async Task ValidateImage_RejectsCorruptFile()
    {
        string corruptPath = Path.Combine(_tempDir, "corrupt.jpg");
        await File.WriteAllTextAsync(corruptPath, "This is not a real JPEG file content.");

        var valResult = await _engine.ValidateImageAsync(corruptPath);
        Assert.False(valResult.IsValid);
        Assert.NotNull(valResult.ErrorMessage);
    }
}
