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

    [Fact]
    public void ParsesMetadataIdentityDescriptionsFrontispiecePaletteResolutionAndZMachineHints()
    {
        byte[] identity = new byte[13];
        BinaryPrimitives.WriteUInt16BigEndian(identity, 0x1234);
        new byte[] { 0x41, 0x42, 0x43, 0x44, 0x45, 0x46 }.CopyTo(identity, 2);
        BinaryPrimitives.WriteUInt16BigEndian(identity.AsSpan(8), 0x5678);
        byte[] encoded = BuildBlorb(
        [
            new ChunkDefinition("PNG ", [0x89, 0x50, 0x4E, 0x47]),
            new ChunkDefinition("OGGV", [1, 2]),
            new ChunkDefinition("IFmd", "<ifindex/>"u8.ToArray()),
            new ChunkDefinition("AUTH", "Example Author"u8.ToArray()),
            new ChunkDefinition("(c) ", "2026 Example"u8.ToArray()),
            new ChunkDefinition("ANNO", "Test package"u8.ToArray()),
            new ChunkDefinition("IFhd", identity),
            new ChunkDefinition("RelN", [0xAB, 0xCD]),
            new ChunkDefinition("Fspc", UInt32(17)),
            new ChunkDefinition("RDes", MakeDescriptions([("Pict", 17u, "Cover café")])),
            new ChunkDefinition("Plte", [0x10, 0x20, 0x30]),
            new ChunkDefinition("Reso", MakeResolution(17, 800, 600, 0, 0, 1600, 1200, 1, 1, 0, 0, 2, 1)),
            new ChunkDefinition("APal", UInt32(17)),
            new ChunkDefinition("Loop", UInt32Pairs((4, 0), (5, 1))),
        ],
        [
            new ResourceDefinition("Pict", 17, 0),
            new ResourceDefinition("Snd ", 4, 1),
            new ResourceDefinition("Snd ", 5, 1),
        ]);

        BlorbMetadata metadata = BlorbFileCodec.Read(encoded).Metadata;

        Assert.Equal("<ifindex/>", metadata.Xml);
        Assert.Equal("Example Author", metadata.Author);
        Assert.Equal("2026 Example", metadata.Copyright);
        Assert.Equal("Test package", metadata.Annotation);
        Assert.Equal((ushort)0x1234, metadata.GameIdentifier!.ReleaseNumber);
        Assert.Equal(new byte[] { 0x41, 0x42, 0x43, 0x44, 0x45, 0x46 }, metadata.GameIdentifier.SerialNumber.ToArray());
        Assert.Equal((ushort)0x5678, metadata.GameIdentifier.Checksum);
        Assert.Equal(0u, metadata.GameIdentifier.InitialProgramCounter);
        Assert.Equal((ushort)0xABCD, metadata.ReleaseNumber);
        Assert.Equal(17u, metadata.Frontispiece!.Number);
        Assert.Equal(BlorbPaletteKind.Colors, metadata.Palette!.Kind);
        Assert.Equal(new BlorbRgbColor(0x10, 0x20, 0x30), Assert.Single(metadata.Palette.Colors));
        Assert.Equal((uint)800, metadata.Resolution!.StandardWidth);
        Assert.Equal((uint)600, metadata.Resolution.StandardHeight);
        BlorbImageScale scale = Assert.Single(metadata.Resolution.ImageScales);
        Assert.Equal(17u, scale.PictureNumber);
        Assert.Equal(new BlorbScaleRatio(1, 1), scale.Standard);
        Assert.True(scale.Minimum.IsUnbounded);
        Assert.Equal(new BlorbScaleRatio(2, 1), scale.Maximum);
        BlorbResourceDescription description = Assert.Single(metadata.ResourceDescriptions);
        Assert.Equal("Pict", description.Usage);
        Assert.Equal(17u, description.Number);
        Assert.Equal("Cover café", description.Text);
        Assert.Same(description, metadata.FindDescription("Pict", 17));
        Assert.Equal(17u, Assert.Single(metadata.AdaptivePalettePictures));
        Assert.Collection(
            metadata.SoundLoops,
            loop =>
            {
                Assert.Equal(4u, loop.SoundNumber);
                Assert.True(loop.Repeats);
            },
            loop =>
            {
                Assert.Equal(5u, loop.SoundNumber);
                Assert.False(loop.Repeats);
            });
    }

    [Theory]
    [InlineData(16)]
    [InlineData(32)]
    public void ParsesPaletteMinimumColorDepth(byte depth)
    {
        BlorbFile parsed = BlorbFileCodec.Read(BuildBlorb([new ChunkDefinition("Plte", [depth])], []));

        Assert.Equal(BlorbPaletteKind.MinimumColorDepth, parsed.Metadata.Palette!.Kind);
        Assert.Equal(depth, parsed.Metadata.Palette.MinimumColorDepth);
        Assert.Empty(parsed.Metadata.Palette.Colors);
    }

    [Fact]
    public void DefaultsOptionalMetadataAndReleaseNumberWhenChunksAreAbsent()
    {
        BlorbMetadata metadata = BlorbFileCodec.Read(BuildBlorb([], [])).Metadata;

        Assert.Null(metadata.Xml);
        Assert.Null(metadata.GameIdentifier);
        Assert.Equal((ushort)0, metadata.ReleaseNumber);
        Assert.Null(metadata.Frontispiece);
        Assert.Null(metadata.Palette);
        Assert.Null(metadata.Resolution);
        Assert.Empty(metadata.ResourceDescriptions);
        Assert.Empty(metadata.AdaptivePalettePictures);
        Assert.Empty(metadata.SoundLoops);
    }

    [Fact]
    public void RejectsDuplicateOptionalSingletonChunks()
    {
        byte[] encoded = BuildBlorb(
        [
            new ChunkDefinition("IFmd", "<a/>"u8.ToArray()),
            new ChunkDefinition("IFmd", "<b/>"u8.ToArray()),
        ], []);

        Assert.Throws<InvalidDataException>(() => BlorbFileCodec.Read(encoded));
    }

    [Theory]
    [InlineData(new byte[] { 0xFF })]
    [InlineData(new byte[] { (byte)'<' })]
    public void RejectsInvalidUtf8OrMalformedIfmdXml(byte[] data)
    {
        byte[] encoded = BuildBlorb([new ChunkDefinition("IFmd", data)], []);

        Assert.Throws<InvalidDataException>(() => BlorbFileCodec.Read(encoded));
    }

    [Fact]
    public void RejectsIfmdDocumentTypeDeclarations()
    {
        byte[] encoded = BuildBlorb(
            [new ChunkDefinition("IFmd", "<!DOCTYPE a><a/>"u8.ToArray())], []);

        Assert.Throws<InvalidDataException>(() => BlorbFileCodec.Read(encoded));
    }

    [Fact]
    public void RejectsMalformedReleaseIdentityAndFrontispieceChunks()
    {
        byte[] shortIdentity = BuildBlorb([new ChunkDefinition("IFhd", new byte[12])], []);
        byte[] longRelease = BuildBlorb([new ChunkDefinition("RelN", [0, 1, 2])], []);
        byte[] badFrontispieceLength = BuildBlorb([new ChunkDefinition("Fspc", [0, 0, 0])], []);
        byte[] missingFrontispiece = BuildBlorb([new ChunkDefinition("Fspc", UInt32(5))], []);
        byte[] rectangleFrontispiece = BuildBlorb(
            [new ChunkDefinition("Rect", new byte[8]), new ChunkDefinition("Fspc", UInt32(5))],
            [new ResourceDefinition("Pict", 5, 0)]);

        Assert.Throws<InvalidDataException>(() => BlorbFileCodec.Read(shortIdentity));
        Assert.Throws<InvalidDataException>(() => BlorbFileCodec.Read(longRelease));
        Assert.Throws<InvalidDataException>(() => BlorbFileCodec.Read(badFrontispieceLength));
        Assert.Throws<InvalidDataException>(() => BlorbFileCodec.Read(missingFrontispiece));
        Assert.Throws<InvalidDataException>(() => BlorbFileCodec.Read(rectangleFrontispiece));
    }

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 15 })]
    [InlineData(new byte[] { 16, 17 })]
    public void RejectsInvalidPaletteLengthsAndDepth(byte[] palette)
    {
        byte[] encoded = BuildBlorb([new ChunkDefinition("Plte", palette)], []);

        Assert.Throws<InvalidDataException>(() => BlorbFileCodec.Read(encoded));
    }

    [Fact]
    public void RejectsPaletteWithMoreThan256Colors()
    {
        byte[] encoded = BuildBlorb([new ChunkDefinition("Plte", new byte[771])], []);

        Assert.Throws<InvalidDataException>(() => BlorbFileCodec.Read(encoded));
    }

    [Fact]
    public void RejectsMalformedResolutionDimensionsEntriesAndRatios()
    {
        byte[] shortChunk = BuildBlorb([new ChunkDefinition("Reso", new byte[23])], []);
        byte[] zeroStandard = BuildBlorb(
            [new ChunkDefinition("PNG ", [0]), new ChunkDefinition("Reso", MakeResolution(1, 0, 100, 0, 0, 0, 0, 1, 1, 0, 0, 0, 0))],
            [new ResourceDefinition("Pict", 1, 0)]);
        byte[] badRatio = BuildBlorb(
            [new ChunkDefinition("PNG ", [0]), new ChunkDefinition("Reso", MakeResolution(1, 100, 100, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0))],
            [new ResourceDefinition("Pict", 1, 0)]);
        byte[] missingPicture = BuildBlorb(
            [new ChunkDefinition("Reso", MakeResolution(7, 100, 100, 0, 0, 0, 0, 1, 1, 0, 0, 0, 0))], []);

        Assert.Throws<InvalidDataException>(() => BlorbFileCodec.Read(shortChunk));
        Assert.Throws<InvalidDataException>(() => BlorbFileCodec.Read(zeroStandard));
        Assert.Throws<InvalidDataException>(() => BlorbFileCodec.Read(badRatio));
        Assert.Throws<InvalidDataException>(() => BlorbFileCodec.Read(missingPicture));
    }

    [Fact]
    public void RejectsMalformedResourceDescriptionsAndZMachineTables()
    {
        byte[] shortDescriptionHeader = BuildBlorb([new ChunkDefinition("RDes", UInt32(1))], []);
        byte[] invalidDescriptionUtf8 = BuildBlorb(
            [new ChunkDefinition("PNG ", [0]), new ChunkDefinition("RDes", InvalidUtf8Description())],
            [new ResourceDefinition("Pict", 1, 0)]);
        byte[] badAdaptiveLength = BuildBlorb([new ChunkDefinition("APal", [0, 0, 0])], []);
        byte[] missingAdaptivePicture = BuildBlorb([new ChunkDefinition("APal", UInt32(1))], []);
        byte[] adaptivePaletteWithJpeg = BuildBlorb(
            [new ChunkDefinition("JPEG", [1]), new ChunkDefinition("APal", [])],
            [new ResourceDefinition("Pict", 1, 0)]);
        byte[] badLoopLength = BuildBlorb([new ChunkDefinition("Loop", [0, 0, 0, 1])], []);
        byte[] badLoopFlag = BuildBlorb(
            [new ChunkDefinition("OGGV", [1]), new ChunkDefinition("Loop", UInt32Pair(2, 3))],
            [new ResourceDefinition("Snd ", 2, 0)]);
        byte[] duplicateDescriptions = BuildBlorb(
            [
                new ChunkDefinition("PNG ", [0]),
                new ChunkDefinition("RDes", MakeDescriptions([("Pict", 1u, "first"), ("Pict", 1u, "second")])),
            ],
            [new ResourceDefinition("Pict", 1, 0)]);
        byte[] missingSound = BuildBlorb(
            [new ChunkDefinition("Loop", UInt32Pair(2, 0))], []);

        Assert.Throws<InvalidDataException>(() => BlorbFileCodec.Read(shortDescriptionHeader));
        Assert.Throws<InvalidDataException>(() => BlorbFileCodec.Read(invalidDescriptionUtf8));
        Assert.Throws<InvalidDataException>(() => BlorbFileCodec.Read(badAdaptiveLength));
        Assert.Throws<InvalidDataException>(() => BlorbFileCodec.Read(missingAdaptivePicture));
        Assert.Throws<InvalidDataException>(() => BlorbFileCodec.Read(adaptivePaletteWithJpeg));
        Assert.Throws<InvalidDataException>(() => BlorbFileCodec.Read(badLoopLength));
        Assert.Throws<InvalidDataException>(() => BlorbFileCodec.Read(badLoopFlag));
        Assert.Throws<InvalidDataException>(() => BlorbFileCodec.Read(duplicateDescriptions));
        Assert.Throws<InvalidDataException>(() => BlorbFileCodec.Read(missingSound));
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

    private static byte[] MakeDescriptions(IReadOnlyList<(string Usage, uint Number, string Text)> entries)
    {
        using MemoryStream stream = new();
        Span<byte> value = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(value, checked((uint)entries.Count));
        stream.Write(value);
        foreach ((string usage, uint number, string text) in entries)
        {
            stream.Write(Encoding.ASCII.GetBytes(usage));
            BinaryPrimitives.WriteUInt32BigEndian(value, number);
            stream.Write(value);
            byte[] textBytes = Encoding.UTF8.GetBytes(text);
            BinaryPrimitives.WriteUInt32BigEndian(value, checked((uint)textBytes.Length));
            stream.Write(value);
            stream.Write(textBytes);
        }

        return stream.ToArray();
    }

    private static byte[] MakeResolution(uint pictureNumber, params uint[] values)
    {
        if (values.Length != 12)
        {
            throw new ArgumentException("A test Reso entry requires six dimensions and three ratios.", nameof(values));
        }

        byte[] data = new byte[24 + 28];
        for (int i = 0; i < 6; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(i * 4, sizeof(uint)), values[i]);
        }

        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(24, sizeof(uint)), pictureNumber);
        for (int i = 0; i < 6; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(28 + i * 4, sizeof(uint)), values[i + 6]);
        }

        return data;
    }

    private static byte[] UInt32(uint value)
    {
        byte[] data = new byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(data, value);
        return data;
    }

    private static byte[] UInt32Pair(uint first, uint second)
    {
        byte[] data = new byte[sizeof(uint) * 2];
        BinaryPrimitives.WriteUInt32BigEndian(data, first);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(sizeof(uint)), second);
        return data;
    }

    private static byte[] UInt32Pairs(params (uint First, uint Second)[] values)
    {
        byte[] data = new byte[values.Length * sizeof(uint) * 2];
        for (int i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(i * 8, sizeof(uint)), values[i].First);
            BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(i * 8 + sizeof(uint), sizeof(uint)), values[i].Second);
        }

        return data;
    }

    private static byte[] InvalidUtf8Description()
    {
        byte[] data = new byte[17];
        BinaryPrimitives.WriteUInt32BigEndian(data, 1);
        "Pict"u8.CopyTo(data.AsSpan(4));
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(12), 1);
        data[16] = 0xFF;
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
