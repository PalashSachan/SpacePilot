using SpacePilot.Core.Utilities;
using Xunit;

namespace SpacePilot.UnitTests;

public class S3ServiceTests
{
    [Theory]
    [InlineData("folder/subfolder/", "folder/subfolder/")]
    [InlineData("folder/subfolder", "folder/subfolder/")]
    [InlineData("/folder/subfolder/", "folder/subfolder/")]
    [InlineData("", "")]
    [InlineData("/", "")]
    public void NormalizeS3Prefix_NormalizesTrailingSlashAndRoot(string input, string expected)
    {
        string actual = PathUtilities.NormalizeS3Prefix(input);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("images/photos/vacation/", "images/photos/")]
    [InlineData("images/photos/", "images/")]
    [InlineData("images/", "")]
    [InlineData("", "")]
    public void GetParentPrefix_ResolvesCorrectAncestor(string input, string expected)
    {
        string actual = PathUtilities.GetParentPrefix(input);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void BuildStagingKey_PreservesRelativePathUnderStagingPrefix()
    {
        string stagingPrefix = "__spacepilot_compressed/";
        string originalKey = "customers/invoices/2026/inv_101.pdf";

        string stagingKey = PathUtilities.BuildStagingKey(stagingPrefix, originalKey);
        Assert.Equal("__spacepilot_compressed/customers/invoices/2026/inv_101.pdf", stagingKey);
    }

    [Fact]
    public void BuildBackupKey_GeneratesTimestampedBackupName()
    {
        var timestamp = new DateTime(2026, 10, 23, 14, 30, 0, DateTimeKind.Utc);
        string originalKey = "archives/document.pdf";

        string backupKey = PathUtilities.BuildBackupKey(originalKey, timestamp);
        Assert.Equal("archives/document.backup_20261023_143000.pdf", backupKey);
    }

    [Fact]
    public void GetS3Name_ExtractsFileNameOrFolderName()
    {
        Assert.Equal("photo.jpg", PathUtilities.GetS3Name("folder/sub/photo.jpg"));
        Assert.Equal("sub", PathUtilities.GetS3Name("folder/sub/"));
        Assert.Equal("rootfile.txt", PathUtilities.GetS3Name("rootfile.txt"));
    }
}
