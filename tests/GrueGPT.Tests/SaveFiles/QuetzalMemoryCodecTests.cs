using GrueGPT.Core.SaveFiles;
using Xunit;

namespace GrueGPT.Tests.SaveFiles;

public sealed class QuetzalMemoryCodecTests
{
    [Fact]
    public void CompressesXorDifferencesAndRoundTripsDynamicMemory()
    {
        byte[] original = [0x10, 0x20, 0x30, 0x40];
        byte[] current = [0x11, 0x20, 0x31, 0x40];

        QuetzalChunk chunk = QuetzalMemoryCodec.CreateCMemChunk(original, current);
        byte[] restored = QuetzalMemoryCodec.ReadCMemChunk(chunk, original);

        Assert.Equal("CMem", chunk.Id);
        Assert.Equal(new byte[] { 0x01, 0, 0, 0x01, 0, 0 }, chunk.Data.ToArray());
        Assert.Equal(current, restored);
    }

    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(255, 0, 254)]
    [InlineData(256, 0, 255)]
    [InlineData(257, 0, 255, 0, 0)]
    [InlineData(512, 0, 255, 0, 255)]
    public void EncodesEqualBytesAsZeroRunsOfAtMost256(int length, params int[] expected)
    {
        byte[] memory = Enumerable.Repeat((byte)0xA5, length).ToArray();

        QuetzalChunk chunk = QuetzalMemoryCodec.CreateCMemChunk(memory, memory);

        Assert.Equal(expected.Select(static value => (byte)value), chunk.Data.ToArray());
        Assert.Equal(memory, QuetzalMemoryCodec.ReadCMemChunk(chunk, memory));
    }

    [Fact]
    public void TreatsMissingDecodedTailAsUnchangedOriginalMemory()
    {
        byte[] original = [0x10, 0x20, 0x30];

        byte[] restored = QuetzalMemoryCodec.ReadCMemChunk(new QuetzalChunk("CMem", [0x01]), original);

        Assert.Equal(new byte[] { 0x11, 0x20, 0x30 }, restored);
    }

    [Fact]
    public void EmptyCompressedDataRestoresTheOriginalMemory()
    {
        byte[] original = [0x10, 0x20, 0x30];

        Assert.Equal(original, QuetzalMemoryCodec.ReadCMemChunk(new QuetzalChunk("CMem", []), original));
    }

    [Fact]
    public void RejectsDifferentOriginalAndCurrentMemoryLengths()
    {
        Assert.Throws<ArgumentException>(() => QuetzalMemoryCodec.CreateCMemChunk([1, 2], [1]));
    }

    [Fact]
    public void RejectsAnIncompleteZeroRun()
    {
        Assert.Throws<InvalidDataException>(() =>
            QuetzalMemoryCodec.ReadCMemChunk(new QuetzalChunk("CMem", [0]), [1]));
    }

    [Theory]
    [InlineData(new byte[] { 0, 2 }, 2)]
    [InlineData(new byte[] { 1, 2, 3 }, 2)]
    public void RejectsDecodedMemoryThatExceedsTheExpectedLength(byte[] encoded, int expectedLength)
    {
        Assert.Throws<InvalidDataException>(() =>
            QuetzalMemoryCodec.ReadCMemChunk(new QuetzalChunk("CMem", encoded), new byte[expectedLength]));
    }

    [Fact]
    public void RejectsACompressedLiteralForZeroLengthMemory()
    {
        Assert.Throws<InvalidDataException>(() =>
            QuetzalMemoryCodec.ReadCMemChunk(new QuetzalChunk("CMem", [1]), []));
    }

    [Fact]
    public void RequiresTheExpectedChunkIdentifiers()
    {
        Assert.Throws<InvalidDataException>(() =>
            QuetzalMemoryCodec.ReadCMemChunk(new QuetzalChunk("UMem", []), []));
        Assert.Throws<InvalidDataException>(() =>
            QuetzalMemoryCodec.ReadUMemChunk(new QuetzalChunk("CMem", []), 0));
    }

    [Fact]
    public void WritesAndReadsUncompressedDynamicMemoryAtExactLength()
    {
        byte[] memory = [0, 1, 0xFE, 0xFF];

        QuetzalChunk chunk = QuetzalMemoryCodec.CreateUMemChunk(memory);
        byte[] restored = QuetzalMemoryCodec.ReadUMemChunk(chunk, memory.Length);

        Assert.Equal("UMem", chunk.Id);
        Assert.Equal(memory, chunk.Data.ToArray());
        Assert.Equal(memory, restored);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void RejectsUncompressedMemoryWithTheWrongLength(int expectedLength)
    {
        Assert.Throws<InvalidDataException>(() =>
            QuetzalMemoryCodec.ReadUMemChunk(new QuetzalChunk("UMem", [1, 2, 3]), expectedLength));
    }

    [Fact]
    public void RejectsNegativeExpectedUncompressedLength()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            QuetzalMemoryCodec.ReadUMemChunk(new QuetzalChunk("UMem", []), -1));
    }
}
