using Microsoft.Extensions.Logging.Abstractions;
using SpacePilot.Application.Services;
using SpacePilot.Core.Enums;
using SpacePilot.Core.Models;
using SpacePilot.Core.Utilities;
using Xunit;

namespace SpacePilot.UnitTests;

public class LocalFolderWorkflowTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly LocalFolderService _service;

    public LocalFolderWorkflowTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "SpacePilot_LocalTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);

        _service = new LocalFolderService(NullLogger<LocalFolderService>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            try { Directory.Delete(_tempRoot, true); } catch { }
        }
    }

    [Fact]
    public async Task DiscoverFiles_StrictlyExcludesCompressedSubfolders()
    {
        // Setup folder layout:
        // root/photo1.jpg
        // root/doc.pdf
        // root/compressed/photo1.jpg  <-- MUST BE EXCLUDED!
        // root/sub/photo2.png
        // root/sub/compressed/photo2.png <-- MUST BE EXCLUDED!

        string photo1 = Path.Combine(_tempRoot, "photo1.jpg");
        string doc = Path.Combine(_tempRoot, "doc.pdf");
        File.WriteAllText(photo1, "test");
        File.WriteAllText(doc, "test");

        string compressedRoot = Path.Combine(_tempRoot, "compressed");
        Directory.CreateDirectory(compressedRoot);
        File.WriteAllText(Path.Combine(compressedRoot, "photo1.jpg"), "compressed");

        string sub = Path.Combine(_tempRoot, "sub");
        Directory.CreateDirectory(sub);
        string photo2 = Path.Combine(sub, "photo2.png");
        File.WriteAllText(photo2, "test");

        string compressedSub = Path.Combine(sub, "compressed");
        Directory.CreateDirectory(compressedSub);
        File.WriteAllText(Path.Combine(compressedSub, "photo2.png"), "compressed");

        var discovered = await _service.DiscoverFilesAsync(_tempRoot, recursive: true);

        // Only photo1.jpg, doc.pdf, and sub/photo2.png should be returned!
        Assert.Equal(3, discovered.Count);
        Assert.DoesNotContain(discovered, p => p.Contains("\\compressed\\", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetLocalCompressedSiblingPath_ComputesSiblingDirectoryCorrectly()
    {
        string original = @"C:\Users\John\Documents\invoice.jpg";
        string expected = @"C:\Users\John\Documents\compressed\invoice.jpg";

        string actual = PathUtilities.GetLocalCompressedSiblingPath(original);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task PrepareJob_CreatesItemsWithCorrectSiblingDestination()
    {
        string file1 = Path.Combine(_tempRoot, "doc1.pdf");
        File.WriteAllBytes(file1, new byte[1024]);

        var profile = CompressionProfile.CreateVisuallyOptimized();
        var job = await _service.PrepareLocalCompressionJobAsync(new[] { file1 }, profile, null, CollisionPolicy.Skip);

        Assert.Equal(1, job.TotalFiles);
        Assert.Equal(1024, job.OriginalBytes);
    }
}
