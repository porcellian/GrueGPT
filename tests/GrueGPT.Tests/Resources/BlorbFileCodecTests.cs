using System.Buffers.Binary;
using System.Text;
using GrueGPT.Core.Resources;
using Xunit;

namespace GrueGPT.Tests.Resources;

public sealed class BlorbFileCodecTests
{
    [Fact]
    public void ParsesResourceIndexAndExtractsEmbeddedZCode()
    {
        byte[] story = [0x03, 0x00, 0x10, 0x20];
        byte[] encoded = BuildBlorb(
            [new ChunkDefinition("ZCOD", story), new ChunkDefinition("AUTH", "maker"u8.ToArray())],
            [new ResourceDefinition("Exec", 0, 0)]);

        BlorbFile parsed = BlorbFileCodec.Read(encoded);

        Assert.Collection(
            parsed.Chunks,
            chunk => Assert.Equal("RIdx", chunk.Id),
            chunk => Assert.Equal("ZCOD", chunk.Id),
            chunk => Assert.Equal("AUTH", chunk.Id));
        BlorbResource executable = Assert.Single(parsed.Resources);
        Assert.Equal("Exec", executable.Usage);
        Assert.Equal(0u, executable.Number);
        Assert.Equal(parsed.Chunks[1].Offset, executable.StartOffset);
        Assert.Equal(story, parsed.GetZCodeExecutable().ToArray());
    }

    [Fact]
    public void PreservesUnknownChunksAndAllowsMultipleResourceNumbersToAliasAChunk()
    {
        byte[] encoded = BuildBlorb(
            [new ChunkDefinition("PNG ", [0x89, 0x50, 0x4E, 0x47]), new ChunkDefinition("Xtra", [9])],
            [new ResourceDefinition("Pict", 4, 0), new ResourceDefinition("Pict", 7, 0)]);

        BlorbFile parsed = BlorbFileCodec.Read(encoded);

        Assert.Equal(2, parsed.Resources.Count);
        Assert.Equal(4u, parsed.Resources[0].Number);
        Assert.Equal(7u, parsed.Resources[1].Number);
        Assert.Same(parsed.Resources[0].Chunk, parsed.Resources[1].Chunk);
        Assert.Equal(new byte[] { 9 }, parsed.Chunks[2].Data.ToArray());
    }

    [Fact]
    public void ReadsZeroLengthAndOddLengthChunksWithRequiredPadding()
    {
        byte[] encoded = BuildBlorb(
            [new ChunkDefinition("TEXT", []), new ChunkDefinition("AUTH", [0x41])],
            []);

        BlorbFile parsed = BlorbFileCodec.Read(encoded);

        Assert.Empty(parsed.Resources);
        Assert.True(parsed.Chunks[1].Data.IsEmpty);
        Assert.Equal(new byte[] { 0x41 }, parsed.Chunks[2].Data.ToArray());
    }

    [Fact]
    public void ResolvesZCodeExecutableWhenOtherChunksArePresent()
    {
        byte[] encoded = BuildBlorb(
            [new ChunkDefinition("AUTH", [0x41]), new ChunkDefinition("ZCOD", [1, 2, 3])],
            [new ResourceDefinition("Exec", 0, 1)]);

        BlorbFile parsed = BlorbFileCodec.Read(encoded);

        Assert.Equal(new byte[] { 1, 2, 3 }, parsed.GetZCodeExecutable().ToArray());
    }

    [Fact]
    public void AllowsResourceOnlyBlorbButExecutableAccessFailsClearly()
    {
        BlorbFile parsed = BlorbFileCodec.Read(BuildBlorb([new ChunkDefinition("AUTH", [])], []));

        Assert.Throws<InvalidDataException>(() => parsed.GetZCodeExecutable());
    }

    [Fact]
    public void RejectsNonZCodeExecutableWhenZCodeBytesAreRequested()
    {
        BlorbFile parsed = BlorbFileCodec.Read(BuildBlorb(
            [new ChunkDefinition("GLUL", [1])],
            [new ResourceDefinition("Exec", 0, 0)]));

        Assert.Throws<InvalidDataException>(() => parsed.GetZCodeExecutable());
    }

    [Fact]
    public void RejectsShortOrWrongFormHeaders()
    {
        Assert.Throws<InvalidDataException>(() => BlorbFileCodec.Read("FORM"u8));

        byte[] encoded = BuildBlorb([], []);
        "IFZS"u8.CopyTo(encoded.AsSpan(8));
        Assert.Throws<InvalidDataException>(() => BlorbFileCodec.Read(encoded));
    }

    [Fact]
    public void RejectsFormLengthsThatDoNotMatchTheFile()
    {
        byte[] encoded = BuildBlorb([new ChunkDefinition("AUTH", [])], []);
        BinaryPrimitives.WriteUInt32BigEndian(encoded.AsSpan(4, sizeof(uint)), 4);

        Assert.Throws<InvalidDataException>(() => BlorbFileCodec.Read(encoded));
    }

    [Fact]
    public void RequiresRIdxToBeTheFirstAndOnlyIndexChunk()
    {
        byte[] notFirst = BuildRawBlorb(
        [
            new ChunkDefinition("AUTH", []),
            new ChunkDefinition("RIdx", MakeIndex([])),
        ]);
        byte[] duplicate = BuildRawBlorb(
        [
            new ChunkDefinition("RIdx", MakeIndex([])),
            new ChunkDefinition("RIdx", MakeIndex([])),
        ]);

        Assert.Throws<InvalidDataException>(() => BlorbFileCodec.Read(notFirst));
        Assert.Throws<InvalidDataException>(() => BlorbFileCodec.Read(duplicate));
    }

    [Fact]
    public void RejectsMalformedResourceIndexLengthAndCount()
    {
        byte[] missingCount = BuildRawBlorb([new ChunkDefinition("RIdx", [])]);
        byte[] incompleteEntry = BuildRawBlorb([new ChunkDefinition("RIdx", [0, 0, 0, 1])]);
        byte[] impossibleCount = BuildRawBlorb([new ChunkDefinition("RIdx", [0xFF, 0xFF, 0xFF, 0xFF])]);

        Assert.Throws<InvalidDataException>(() => BlorbFileCodec.Read(missingCount));
        Assert.Throws<InvalidDataException>(() => BlorbFileCodec.Read(incompleteEntry));
        Assert.Throws<InvalidDataException>(() => BlorbFileCodec.Read(impossibleCount));
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(13u)]
    [InlineData(uint.MaxValue)]
    public void RejectsResourceOffsetsThatDoNotIdentifyAChunkHeader(uint start)
    {
        byte[] encoded = BuildRawBlorb(
        [
            new ChunkDefinition("RIdx", MakeIndex([("Exec", 0u, start)])),
            new ChunkDefinition("ZCOD", [1]),
        ]);

        Assert.Throws<InvalidDataException>(() => BlorbFileCodec.Read(encoded));
    }

    [Fact]
    public void RejectsAnIndexThatPointsToRIdxInsteadOfAResourceChunk()
    {
        byte[] encoded = BuildBlorb(
            [new ChunkDefinition("ZCOD", [1])],
            [new ResourceDefinition("Exec", 0, -1)]);

        Assert.Throws<InvalidDataException>(() => BlorbFileCodec.Read(encoded));
    }

    [Fact]
    public void RejectsExecutableNumberOtherThanZeroAndMultipleExecutables()
    {
        byte[] nonzeroNumber = BuildBlorb(
            [new ChunkDefinition("ZCOD", [1])],
            [new ResourceDefinition("Exec", 1, 0)]);
        byte[] multiple = BuildBlorb(
            [new ChunkDefinition("ZCOD", [1]), new ChunkDefinition("GLUL", [2])],
            [new ResourceDefinition("Exec", 0, 0), new ResourceDefinition("Exec", 0, 1)]);

        Assert.Throws<InvalidDataException>(() => BlorbFileCodec.Read(nonzeroNumber));
        Assert.Throws<InvalidDataException>(() => BlorbFileCodec.Read(multiple));
    }

    [Fact]
    public void RejectsDuplicateUsageAndNumberPairs()
    {
        byte[] encoded = BuildBlorb(
            [new ChunkDefinition("PNG ", [1]), new ChunkDefinition("JPEG", [2])],
            [new ResourceDefinition("Pict", 3, 0), new ResourceDefinition("Pict", 3, 1)]);

        Assert.Throws<InvalidDataException>(() => BlorbFileCodec.Read(encoded));
    }

    [Fact]
    public void PreservesUnknownChunkTypesForForwardCompatibility()
    {
        byte[] encoded = BuildBlorb(
            [new ChunkDefinition("FUTR", [0x41])],
            [new ResourceDefinition("Exec", 0, 0)]);

        BlorbFile parsed = BlorbFileCodec.Read(encoded);

        Assert.Equal("FUTR", Assert.Single(parsed.Resources).Chunk.Id);
        Assert.Throws<InvalidDataException>(() => parsed.GetZCodeExecutable());
    }

    [Fact]
    public void RejectsMalformedChunkHeadersAndOutOfBoundsData()
    {
        byte[] incompleteHeader = BuildRawBlorb(
            [new ChunkDefinition("RIdx", MakeIndex([])), new ChunkDefinition("X", [])]);
        byte[] outOfBounds = BuildRawBlorb(
        [
            new ChunkDefinition("RIdx", MakeIndex([])),
            new ChunkDefinition("AUTH", [1, 2]),
        ]);
        int lastChunkHeader = outOfBounds.Length - 8 - 2;
        BinaryPrimitives.WriteUInt32BigEndian(outOfBounds.AsSpan(lastChunkHeader + 4, sizeof(uint)), 99);

        Assert.Throws<InvalidDataException>(() => BlorbFileCodec.Read(incompleteHeader));
        Assert.Throws<InvalidDataException>(() => BlorbFileCodec.Read(outOfBounds));
    }

    [Fact]
    public void RejectsNonzeroOddChunkPadding()
    {
        byte[] encoded = BuildBlorb([new ChunkDefinition("AUTH", [0x41])], []);
        encoded[^1] = 0xFF;

        Assert.Throws<InvalidDataException>(() => BlorbFileCodec.Read(encoded));
    }

    [Fact]
    public void RejectsMissingOddChunkPadding()
    {
        byte[] withPad = BuildBlorb([new ChunkDefinition("AUTH", [0x41])], []);
        byte[] withoutPad = withPad[..^1];
        BinaryPrimitives.WriteUInt32BigEndian(withoutPad.AsSpan(4, sizeof(uint)), checked((uint)(withoutPad.Length - 8)));

        Assert.Throws<InvalidDataException>(() => BlorbFileCodec.Read(withoutPad));
    }

    private static byte[] BuildBlorb(IReadOnlyList<ChunkDefinition> chunks, IReadOnlyList<ResourceDefinition> resources)
    {
        int indexOffset = 12 + 8 + sizeof(uint) + resources.Count * 12;
        List<(string Usage, uint Number, uint Start)> entries = [];
        for (int i = 0; i < resources.Count; i++)
        {
            ResourceDefinition resource = resources[i];
            uint start = resource.ChunkIndex < 0
                ? 12
                : checked((uint)(indexOffset + GetChunkOffset(chunks, resource.ChunkIndex)));
            entries.Add((resource.Usage, resource.Number, start));
        }

        return BuildRawBlorb([new ChunkDefinition("RIdx", MakeIndex(entries)), .. chunks]);
    }

    private static int GetChunkOffset(IReadOnlyList<ChunkDefinition> chunks, int index)
    {
        int offset = 0;
        for (int i = 0; i < index; i++)
        {
            offset += 8 + chunks[i].Data.Length + (chunks[i].Data.Length & 1);
        }

        return offset;
    }

    private static byte[] MakeIndex(IReadOnlyList<(string Usage, uint Number, uint Start)> entries)
    {
        byte[] data = new byte[sizeof(uint) + entries.Count * 12];
        BinaryPrimitives.WriteUInt32BigEndian(data, checked((uint)entries.Count));
        int offset = sizeof(uint);
        foreach ((string usage, uint number, uint start) in entries)
        {
            Encoding.ASCII.GetBytes(usage, data.AsSpan(offset, 4));
            BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(offset + 4, sizeof(uint)), number);
            BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(offset + 8, sizeof(uint)), start);
            offset += 12;
        }

        return data;
    }

    private static byte[] BuildRawBlorb(IReadOnlyList<ChunkDefinition> chunks)
    {
        using MemoryStream stream = new();
        stream.Write("FORM"u8);
        stream.Write(new byte[sizeof(uint)]);
        stream.Write("IFRS"u8);
        Span<byte> header = stackalloc byte[sizeof(uint)];
        foreach (ChunkDefinition chunk in chunks)
        {
            stream.Write(Encoding.ASCII.GetBytes(chunk.Id));
            BinaryPrimitives.WriteUInt32BigEndian(header, checked((uint)chunk.Data.Length));
            stream.Write(header);
            stream.Write(chunk.Data);
            if ((chunk.Data.Length & 1) != 0)
            {
                stream.WriteByte(0);
            }
        }

        byte[] encoded = stream.ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(encoded.AsSpan(4, sizeof(uint)), checked((uint)(encoded.Length - 8)));
        return encoded;
    }

    private sealed record ChunkDefinition(string Id, byte[] Data);

    private sealed record ResourceDefinition(string Usage, uint Number, int ChunkIndex);
}
