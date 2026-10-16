using System.Buffers.Binary;
using System.Text;
using GrueGPT.Core.SaveFiles;
using Xunit;

namespace GrueGPT.Tests.SaveFiles;

public sealed class QuetzalFileCodecTests
{
    [Fact]
    public void WritesAnEmptyIfzsFormWithBigEndianLength()
    {
        byte[] encoded = QuetzalFileCodec.Write(new QuetzalFile([]));

        Assert.Equal("FORM", Encoding.ASCII.GetString(encoded, 0, 4));
        Assert.Equal(4u, BinaryPrimitives.ReadUInt32BigEndian(encoded.AsSpan(4, sizeof(uint))));
        Assert.Equal("IFZS", Encoding.ASCII.GetString(encoded, 8, 4));
        Assert.Equal(12, encoded.Length);
    }

    [Fact]
    public void WritesBigEndianChunkLengthsAndZeroPadsOddData()
    {
        QuetzalFile file = new(
        [
            new QuetzalChunk("IFhd", [0x12, 0x34, 0x56]),
            new QuetzalChunk("UMem", [0xAB, 0xCD]),
        ]);

        byte[] encoded = QuetzalFileCodec.Write(file);

        Assert.Equal(26u, BinaryPrimitives.ReadUInt32BigEndian(encoded.AsSpan(4, sizeof(uint))));
        Assert.Equal("IFhd", Encoding.ASCII.GetString(encoded, 12, 4));
        Assert.Equal(3u, BinaryPrimitives.ReadUInt32BigEndian(encoded.AsSpan(16, sizeof(uint))));
        Assert.Equal(new byte[] { 0x12, 0x34, 0x56, 0x00 }, encoded.AsSpan(20, 4).ToArray());
        Assert.Equal("UMem", Encoding.ASCII.GetString(encoded, 24, 4));
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32BigEndian(encoded.AsSpan(28, sizeof(uint))));
        Assert.Equal(new byte[] { 0xAB, 0xCD }, encoded.AsSpan(32, 2).ToArray());
    }

    [Fact]
    public void RoundTripsUnknownAndDuplicateChunksInTheirOriginalOrder()
    {
        QuetzalFile original = new(
        [
            new QuetzalChunk("IFhd", [1]),
            new QuetzalChunk("Xtra", [2, 3]),
            new QuetzalChunk("Xtra", [4]),
        ]);

        QuetzalFile parsed = QuetzalFileCodec.Read(QuetzalFileCodec.Write(original));

        Assert.Collection(
            parsed.Chunks,
            chunk =>
            {
                Assert.Equal("IFhd", chunk.Id);
                Assert.Equal(new byte[] { 1 }, chunk.Data.ToArray());
            },
            chunk =>
            {
                Assert.Equal("Xtra", chunk.Id);
                Assert.Equal(new byte[] { 2, 3 }, chunk.Data.ToArray());
            },
            chunk =>
            {
                Assert.Equal("Xtra", chunk.Id);
                Assert.Equal(new byte[] { 4 }, chunk.Data.ToArray());
            });
    }

    [Fact]
    public void CopiesDataProvidedToChunkSoCallerMutationDoesNotChangeIt()
    {
        byte[] data = [1, 2, 3];
        QuetzalChunk chunk = new("AUTH", data);
        data[0] = 9;

        Assert.Equal(new byte[] { 1, 2, 3 }, chunk.Data.ToArray());
    }

    [Theory]
    [InlineData("")]
    [InlineData("ABC")]
    [InlineData("ABCDE")]
    [InlineData("A B ")]
    [InlineData("A\nC ")]
    [InlineData("éBCD")]
    public void RejectsInvalidChunkIdentifiers(string id)
    {
        Assert.Throws<ArgumentException>(() => new QuetzalChunk(id, []));
    }

    [Fact]
    public void RejectsShortFormHeaders()
    {
        Assert.Throws<InvalidDataException>(() => QuetzalFileCodec.Read("FORM"u8));
    }

    [Fact]
    public void RejectsWrongRootIdentifier()
    {
        byte[] encoded = MakeForm([]);
        "NOPE"u8.CopyTo(encoded);

        Assert.Throws<InvalidDataException>(() => QuetzalFileCodec.Read(encoded));
    }

    [Fact]
    public void RejectsWrongFormType()
    {
        byte[] encoded = MakeForm([]);
        "IFRS"u8.CopyTo(encoded.AsSpan(8));

        Assert.Throws<InvalidDataException>(() => QuetzalFileCodec.Read(encoded));
    }

    [Theory]
    [InlineData("IfZS")]
    [InlineData("IF S")]
    [InlineData(" IFZ")]
    public void RejectsMalformedFormTypeIdentifiers(string formType)
    {
        byte[] encoded = MakeForm([]);
        Encoding.ASCII.GetBytes(formType, encoded.AsSpan(8, 4));

        Assert.Throws<InvalidDataException>(() => QuetzalFileCodec.Read(encoded));
    }

    [Theory]
    [InlineData(3u)]
    [InlineData(5u)]
    public void RejectsFormLengthsThatDoNotMatchTheInput(uint declaredLength)
    {
        byte[] encoded = MakeForm([]);
        BinaryPrimitives.WriteUInt32BigEndian(encoded.AsSpan(4, sizeof(uint)), declaredLength);

        Assert.Throws<InvalidDataException>(() => QuetzalFileCodec.Read(encoded));
    }

    [Fact]
    public void RejectsTrailingBytesAfterTheDeclaredForm()
    {
        byte[] encoded = [.. MakeForm([]), 0];

        Assert.Throws<InvalidDataException>(() => QuetzalFileCodec.Read(encoded));
    }

    [Fact]
    public void RejectsAnIncompleteChunkHeader()
    {
        byte[] encoded = MakeForm([0x41, 0x42, 0x43]);

        Assert.Throws<InvalidDataException>(() => QuetzalFileCodec.Read(encoded));
    }

    [Fact]
    public void RejectsChunkDataThatExtendsPastTheForm()
    {
        byte[] encoded = MakeForm([.. "CMem"u8, 0x00, 0x00, 0x00, 0x05, 0x01, 0x02]);

        Assert.Throws<InvalidDataException>(() => QuetzalFileCodec.Read(encoded));
    }

    [Fact]
    public void RejectsChunkLengthsThatOverflowTheAvailableData()
    {
        byte[] encoded = MakeForm([.. "CMem"u8, 0xFF, 0xFF, 0xFF, 0xFF]);

        Assert.Throws<InvalidDataException>(() => QuetzalFileCodec.Read(encoded));
    }

    [Fact]
    public void RejectsInvalidChunkIdentifiersInTheInput()
    {
        byte[] encoded = MakeForm([.. "A B "u8, 0, 0, 0, 0]);

        Assert.Throws<InvalidDataException>(() => QuetzalFileCodec.Read(encoded));
    }

    [Fact]
    public void RejectsNonAsciiBytesInIdentifiers()
    {
        byte[] encoded = MakeForm([0x80, 0x55, 0x54, 0x48, 0, 0, 0, 0]);

        Assert.Throws<InvalidDataException>(() => QuetzalFileCodec.Read(encoded));
    }

    [Fact]
    public void RejectsAnOddLengthChunkWithoutItsPadByte()
    {
        byte[] encoded = MakeForm([.. "AUTH"u8, 0, 0, 0, 1, 0x41]);

        Assert.Throws<InvalidDataException>(() => QuetzalFileCodec.Read(encoded));
    }

    [Fact]
    public void RejectsNonzeroChunkPadding()
    {
        byte[] encoded = MakeForm([.. "AUTH"u8, 0, 0, 0, 1, 0x41, 0xFF]);

        Assert.Throws<InvalidDataException>(() => QuetzalFileCodec.Read(encoded));
    }

    private static byte[] MakeForm(ReadOnlySpan<byte> chunks)
    {
        byte[] encoded = new byte[12 + chunks.Length];
        "FORM"u8.CopyTo(encoded);
        BinaryPrimitives.WriteUInt32BigEndian(encoded.AsSpan(4, sizeof(uint)), (uint)(sizeof(uint) + chunks.Length));
        "IFZS"u8.CopyTo(encoded.AsSpan(8));
        chunks.CopyTo(encoded.AsSpan(12));
        return encoded;
    }
}
