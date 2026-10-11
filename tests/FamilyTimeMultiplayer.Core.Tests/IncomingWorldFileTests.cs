using System;
using System.IO;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Core.Transport;
using Xunit;

namespace FamilyTimeMultiplayer.Core.Tests;

public sealed class IncomingWorldFileTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ftmp-transfer-" + Guid.NewGuid().ToString("N"));
    private string Destination => Path.Combine(_directory, "world");
    public IncomingWorldFileTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, true);

    [Theory]
    [InlineData(1073741825L)]
    [InlineData(long.MaxValue)]
    public void OversizedTransferCannotCreateOrTruncateDestination(long length)
    {
        File.WriteAllBytes(Destination, new byte[] { 7 });
        Assert.Throws<InvalidDataException>(() => new IncomingWorldFile(Destination, length));
        Assert.Equal(new byte[] { 7 }, File.ReadAllBytes(Destination));
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public void InterruptedTransferPreservesExistingWorldAndRemovesPartialData()
    {
        File.WriteAllBytes(Destination, new byte[] { 7 });
        using (var incoming = new IncomingWorldFile(Destination, 3)) incoming.Append(new WorldChunkMessage(0, new byte[] { 1 }));
        Assert.Equal(new byte[] { 7 }, File.ReadAllBytes(Destination));
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public void CompletedTransferAtomicallyReplacesWorldAndAllowsAnotherTransfer()
    {
        File.WriteAllBytes(Destination, new byte[] { 7 });
        foreach (byte value in new byte[] { 1, 2 })
        {
            byte[] bytes = { value, 3, 4 };
            using var incoming = new IncomingWorldFile(Destination, bytes.Length);
            incoming.Append(new WorldChunkMessage(0, bytes));
            incoming.Complete(new WorldCompleteMessage(Crc32.Compute(bytes)));
            Assert.Equal(bytes, File.ReadAllBytes(Destination));
        }
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public void BadOffsetsOverrunsAndChecksumsDoNotPublishWorld()
    {
        using (var incoming = new IncomingWorldFile(Destination, 2))
        {
            Assert.Throws<InvalidDataException>(() => incoming.Append(new WorldChunkMessage(long.MaxValue, new byte[] { 1 })));
            Assert.Throws<InvalidDataException>(() => incoming.Append(new WorldChunkMessage(0, new byte[] { 1, 2, 3 })));
            Assert.Equal(0, incoming.Received);
            incoming.Append(new WorldChunkMessage(0, new byte[] { 1, 2 }));
            Assert.Throws<InvalidDataException>(() => incoming.Complete(new WorldCompleteMessage(0)));
        }
        Assert.Empty(Directory.GetFiles(_directory));
    }
}
