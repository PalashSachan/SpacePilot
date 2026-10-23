using Microsoft.Extensions.Logging;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using SpacePilot.Core.Interfaces;
using SpacePilot.Core.Models;

namespace SpacePilot.Infrastructure.Pdf;

public class PdfSharpOptimizationEngine : IPdfOptimizationEngine
{
    private readonly ILogger<PdfSharpOptimizationEngine> _logger;

    public PdfSharpOptimizationEngine(ILogger<PdfSharpOptimizationEngine> logger)
    {
        _logger = logger;
    }

    public async Task<CompressionResult> OptimizePdfAsync(
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
                result.ErrorMessage = $"Source PDF not found: {sourcePath}";
                return result;
            }

            var origInfo = new FileInfo(sourcePath);
            result.OriginalSizeBytes = origInfo.Length;

            try
            {
                // 1. Open and validate source document
                using var inputDoc = PdfReader.Open(sourcePath, PdfDocumentOpenMode.Import);
                int originalPageCount = inputDoc.PageCount;
                result.PageCount = originalPageCount;

                if (originalPageCount <= 0)
                {
                    result.Success = false;
                    result.ErrorMessage = "Source PDF contains no pages.";
                    return result;
                }

                // 2. Create clean output document and import pages (strips orphaned objects)
                using var outputDoc = new PdfDocument();
                outputDoc.Options.FlateEncodeMode = PdfFlateEncodeMode.BestCompression;
                outputDoc.Options.CompressContentStreams = true;
                outputDoc.Options.NoCompression = false;

                for (int i = 0; i < originalPageCount; i++)
                {
                    outputDoc.AddPage(inputDoc.Pages[i]);
                }

                // Ensure target directory exists
                string? outDir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(outDir))
                    Directory.CreateDirectory(outDir);

                // Save optimized document
                outputDoc.Save(outputPath);

                // 3. Validation: ensure output is nonempty and valid
                var outInfo = new FileInfo(outputPath);
                result.ProcessedSizeBytes = outInfo.Length;

                if (outInfo.Length == 0)
                {
                    result.Success = false;
                    result.ErrorMessage = "Optimized PDF output was empty.";
                    return result;
                }

                // Re-open output document to verify page count and structural integrity
                using var verifyDoc = PdfReader.Open(outputPath, PdfDocumentOpenMode.Import);
                if (verifyDoc.PageCount != originalPageCount)
                {
                    result.Success = false;
                    result.ErrorMessage = $"Page count mismatch: expected {originalPageCount}, but got {verifyDoc.PageCount}.";
                    return result;
                }

                result.Success = true;
                result.IsLossy = false; // Lossless structural optimization
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to optimize PDF '{SourcePath}'", sourcePath);
                result.Success = false;
                result.ErrorMessage = ex.Message;
                return result;
            }
        }, ct);
    }

    public async Task<ValidationResult> ValidatePdfAsync(string filePath, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            if (!File.Exists(filePath))
            {
                return new ValidationResult { IsValid = false, ErrorMessage = "File does not exist." };
            }

            try
            {
                using var doc = PdfReader.Open(filePath, PdfDocumentOpenMode.Import);
                return new ValidationResult
                {
                    IsValid = doc.PageCount > 0,
                    PageCount = doc.PageCount,
                    Format = "PDF"
                };
            }
            catch (Exception ex)
            {
                return new ValidationResult { IsValid = false, ErrorMessage = ex.Message };
            }
        }, ct);
    }
}
