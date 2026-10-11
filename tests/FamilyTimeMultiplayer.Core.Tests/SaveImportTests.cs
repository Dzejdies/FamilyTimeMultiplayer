using System;
using System.IO;
using FamilyTimeMultiplayer.Core.Saves;
using Xunit;

namespace FamilyTimeMultiplayer.Core.Tests;

public sealed class SaveImportTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ftmp-save-import-" + Guid.NewGuid().ToString("N"));
    private string SourceDirectory => Path.Combine(_directory, "source");
    private string DestinationDirectory => Path.Combine(_directory, "destination");
    private string SourcePath => Path.Combine(SourceDirectory, "original.world");

    public SaveImportTests()
    {
        Directory.CreateDirectory(SourceDirectory);
        Directory.CreateDirectory(DestinationDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    [Fact]
    public void CopyCreatesNamedDestinationAndPreservesSource()
    {
        byte[] original = { 3, 1, 4, 1, 5, 9 };
        File.WriteAllBytes(SourcePath, original);

        string result = SaveImport.CopyToUniqueName(SourcePath, DestinationDirectory, "Imported Camp", ".world");

        Assert.Equal(Path.Combine(DestinationDirectory, "Imported Camp.world"), result);
        Assert.Equal(original, File.ReadAllBytes(result));
        Assert.Equal(original, File.ReadAllBytes(SourcePath));
        Assert.Single(Directory.GetFiles(DestinationDirectory));
    }

    [Fact]
    public void ExistingDestinationIsNeverOverwritten()
    {
        byte[] original = { 1, 2, 3 };
        byte[] existing = { 9, 8, 7 };
        File.WriteAllBytes(SourcePath, original);
        string destinationPath = Path.Combine(DestinationDirectory, "Imported.world");
        File.WriteAllBytes(destinationPath, existing);

        Assert.Throws<SaveNameConflictException>(() => SaveImport.CopyToUniqueName(SourcePath, DestinationDirectory, "Imported", ".world"));

        Assert.Equal(original, File.ReadAllBytes(SourcePath));
        Assert.Equal(existing, File.ReadAllBytes(destinationPath));
        Assert.Single(Directory.GetFiles(DestinationDirectory));
    }

    [Theory]
    [InlineData("")]
    [InlineData("../outside")]
    [InlineData("folder/name")]
    public void UnsafeNamesAreRejected(string requestedName)
    {
        File.WriteAllBytes(SourcePath, new byte[] { 4 });

        Assert.Throws<ArgumentException>(() => SaveImport.CopyToUniqueName(SourcePath, DestinationDirectory, requestedName, ".world"));

        Assert.Empty(Directory.GetFiles(DestinationDirectory));
        Assert.True(File.Exists(SourcePath));
    }

    [Fact]
    public void MissingSourceLeavesNoPartialDestination()
    {
        string missingSource = Path.Combine(SourceDirectory, "missing.world");

        Assert.Throws<FileNotFoundException>(() => SaveImport.CopyToUniqueName(missingSource, DestinationDirectory, "Imported", ".world"));

        Assert.Empty(Directory.GetFiles(DestinationDirectory));
    }
}
