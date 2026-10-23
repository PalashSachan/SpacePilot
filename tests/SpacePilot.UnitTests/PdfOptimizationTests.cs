using Microsoft.Extensions.Logging.Abstractions;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using SpacePilot.Application.Compression;
using SpacePilot.Core.Models;
using SpacePilot.Infrastructure.Images;
using SpacePilot.Infrastructure.Pdf;
using Xunit;

namespace SpacePilot.UnitTests;

public class PdfOptimizationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly PdfSharpOptimizationEngine _engine;
    private readonly UnifiedCompressionService _unifiedEngine;

    public PdfOptimizationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SpacePilot_PdfTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _engine = new PdfSharpOptimizationEngine(NullLogger<PdfSharpOptimizationEngine>.Instance);
        var imgEngine = new MagickImageCompressionEngine(NullLogger<MagickImageCompressionEngine>.Instance);
        _unifiedEngine = new UnifiedCompressionService(imgEngine, _engine, NullLogger<UnifiedCompressionService>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    private string CreateTestPdf(string filename, int pageCount)
    {
        string path = Path.Combine(_tempDir, filename);
        using var doc = new PdfDocument();
        for (int i = 0; i < pageCount; i++)
        {
            var page = doc.AddPage();
            using var gfx = XGraphics.FromPdfPage(page);
            var font = new XFont("Arial", 20);
            gfx.DrawString($"SpacePilot Sample PDF Page {i + 1}", font, XBrushes.Navy, new XPoint(50, 100));
            gfx.DrawRectangle(XPens.DarkCyan, 50, 150, 400, 200);
        }
        doc.Save(path);
        return path;
    }

    [Fact]
    public async Task OptimizePdf_PreservesExactPageCount()
    {
        string source = CreateTestPdf("sample_document.pdf", 3);
        string output = Path.Combine(_tempDir, "sample_optimized.pdf");

        var profile = CompressionProfile.CreateLossless();
        var result = await _engine.OptimizePdfAsync(source, output, profile);

        Assert.True(result.Success);
        Assert.Equal(3, result.PageCount);
        Assert.True(File.Exists(output));
        Assert.True(new FileInfo(output).Length > 0);

        var valResult = await _engine.ValidatePdfAsync(output);
        Assert.True(valResult.IsValid);
        Assert.Equal(3, valResult.PageCount);
    }

    [Fact]
    public async Task UnifiedEngine_SkipsPdfBelowThreshold()
    {
        string source = CreateTestPdf("small.pdf", 1);
        string output = Path.Combine(_tempDir, "small_out.pdf");

        long minThresholdBytes = 300 * 1024; // 300 KB
        var profile = CompressionProfile.CreateLossless();

        var result = await _unifiedEngine.ProcessFileAsync(source, output, profile, 200 * 1024, minThresholdBytes, 15.0);

        Assert.True(result.Success);
        Assert.False(result.MeetsThreshold);
        Assert.Contains("Skipped", result.ErrorMessage);
    }

    [Fact]
    public async Task ValidatePdf_RejectsCorruptPdf()
    {
        string corruptPath = Path.Combine(_tempDir, "invalid.pdf");
        await File.WriteAllTextAsync(corruptPath, "%PDF-not a valid structure %EOF");

        var valResult = await _engine.ValidatePdfAsync(corruptPath);
        Assert.False(valResult.IsValid);
        Assert.NotNull(valResult.ErrorMessage);
    }
}
