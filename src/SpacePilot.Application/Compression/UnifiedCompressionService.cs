using Microsoft.Extensions.Logging;
using SpacePilot.Core.Enums;
using SpacePilot.Core.Interfaces;
using SpacePilot.Core.Models;
using SpacePilot.Core.Utilities;

namespace SpacePilot.Application.Compression;

public class UnifiedCompressionService : IUnifiedCompressionEngine
{
    private readonly IImageCompressionEngine _imageEngine;
    private readonly IPdfOptimizationEngine _pdfEngine;
    private readonly ILogger<UnifiedCompressionService> _logger;

    public UnifiedCompressionService(
        IImageCompressionEngine imageEngine,
        IPdfOptimizationEngine pdfEngine,
        ILogger<UnifiedCompressionService> logger)
    {
        _imageEngine = imageEngine;
        _pdfEngine = pdfEngine;
        _logger = logger;
    }

    public async Task<CompressionResult> ProcessFileAsync(
        string sourcePath,
        string outputPath,
        CompressionProfile profile,
        long minImageSizeBytes,
        long minPdfSizeBytes,
        double minSavingsPercentage,
        CancellationToken ct = default)
    {
        var category = PathUtilities.CategorizeFile(sourcePath);
        var fileInfo = new FileInfo(sourcePath);

        if (!fileInfo.Exists)
        {
            return new CompressionResult
            {
                Success = false,
                SourcePath = sourcePath,
                OutputPath = outputPath,
                ErrorMessage = "Source file does not exist."
            };
        }

        long origSize = fileInfo.Length;

        // Threshold check: skip before decoding
        if (category == FileCategory.Image && origSize < minImageSizeBytes)
        {
            return new CompressionResult
            {
                Success = true,
                SourcePath = sourcePath,
                OutputPath = outputPath,
                OriginalSizeBytes = origSize,
                ProcessedSizeBytes = origSize,
                MeetsThreshold = false,
                ErrorMessage = $"Skipped: File size ({ByteSizeFormatter.Format(origSize)}) is below minimum image threshold ({ByteSizeFormatter.Format(minImageSizeBytes)})."
            };
        }

        if (category == FileCategory.Pdf && origSize < minPdfSizeBytes)
        {
            return new CompressionResult
            {
                Success = true,
                SourcePath = sourcePath,
                OutputPath = outputPath,
                OriginalSizeBytes = origSize,
                ProcessedSizeBytes = origSize,
                MeetsThreshold = false,
                ErrorMessage = $"Skipped: File size ({ByteSizeFormatter.Format(origSize)}) is below minimum PDF threshold ({ByteSizeFormatter.Format(minPdfSizeBytes)})."
            };
        }

        CompressionResult result;

        if (category == FileCategory.Image)
        {
            result = await _imageEngine.CompressImageAsync(sourcePath, outputPath, profile, ct);
        }
        else if (category == FileCategory.Pdf)
        {
            result = await _pdfEngine.OptimizePdfAsync(sourcePath, outputPath, profile, ct);
        }
        else
        {
            return new CompressionResult
            {
                Success = false,
                SourcePath = sourcePath,
                OutputPath = outputPath,
                OriginalSizeBytes = origSize,
                ErrorMessage = "Unsupported file format."
            };
        }

        if (result.Success)
        {
            double savings = result.SavingsPercentage;
            result.MeetsThreshold = savings >= minSavingsPercentage;
            if (!result.MeetsThreshold && result.SavedSizeBytes <= 0)
            {
                result.ErrorMessage = $"No savings achieved (output is {ByteSizeFormatter.Format(result.ProcessedSizeBytes)} vs original {ByteSizeFormatter.Format(result.OriginalSizeBytes)}).";
            }
            else if (!result.MeetsThreshold)
            {
                result.ErrorMessage = $"Savings ({savings:F1}%) did not reach the minimum threshold of {minSavingsPercentage:F1}%.";
            }
        }

        return result;
    }
}
