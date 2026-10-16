using System.Buffers.Binary;
using GrueGPT.Core.Stories;
using GrueGPT.Tests.TestUtilities;
using Xunit;

namespace GrueGPT.Tests.Stories;

public sealed class Z3StoryLoaderTests
{
    private readonly Z3StoryLoader _loader = new();

    [Fact]
    public void LoadsVersion3HeaderAndValidatesChecksum()
    {
        byte[] image = new SyntheticStoryBuilder()
            .WriteByte(0x01, 0x02)
            .WriteWord(0x02, 23)
            .WriteWord(0x10, 0xBEEF)
            .WriteByte(0x2000, 0xBA)
            .Build();
        using MemoryStream stream = new(image);

        LoadedStory story = _loader.Load(stream);

        Assert.True(stream.CanRead);
        Assert.Equal(image.Length, story.Image.Length);
        Assert.Equal(3, story.Header.Version);
        Assert.Equal(0x02, story.Header.Flags1);
        Assert.Equal(23, story.Header.ReleaseNumber);
        Assert.Equal(0x2000, story.Header.HighMemoryBase);
        Assert.Equal(0x2000, story.Header.InitialProgramCounter);
        Assert.Equal(0x0100, story.Header.DictionaryAddress);
        Assert.Equal(0x0120, story.Header.ObjectTableAddress);
        Assert.Equal(0x0200, story.Header.GlobalVariablesAddress);
        Assert.Equal(0x1000, story.Header.StaticMemoryBase);
        Assert.Equal(0xBEEF, story.Header.Flags2);
        Assert.Equal(0x0300, story.Header.AbbreviationsAddress);
        Assert.Equal(image.Length, story.Header.FileLength);
        Assert.True(story.Header.HasChecksum);
        Assert.Equal("260101", story.Header.SerialNumber);
        Assert.Equal(image, story.Image.ToArray());
    }

    [Fact]
    public void UsesPhysicalLengthForLegacyVersion3WithoutLengthOrChecksum()
    {
        byte[] image = new SyntheticStoryBuilder().Build();
        WriteWord(image, 0x1A, 0);
        WriteWord(image, 0x1C, 0);

        LoadedStory story = Load(image);

        Assert.Equal(image.Length, story.Header.FileLength);
        Assert.False(story.Header.HasChecksum);
    }

    [Fact]
    public void HonorsDeclaredLengthAndExcludesPhysicalPaddingFromChecksumAndImage()
    {
        byte[] image = new SyntheticStoryBuilder().Build();
        const int declaredLength = 0x4000;
        WriteWord(image, 0x1A, declaredLength / 2);
        image[declaredLength + 5] = 0xA5;
        WriteWord(image, 0x1C, CalculateChecksum(image.AsSpan(0, declaredLength)));

        LoadedStory story = Load(image);

        Assert.Equal(declaredLength, story.Header.FileLength);
        Assert.Equal(declaredLength, story.Image.Length);
        Assert.Equal(image.AsSpan(0, declaredLength).ToArray(), story.Image.ToArray());
    }

    [Fact]
    public void RejectsFilesShorterThanTheHeader()
    {
        StoryLoadException exception = Assert.Throws<StoryLoadException>(() => Load(new byte[63]));

        Assert.Equal(StoryLoadErrorCode.FileTooSmall, exception.Code);
    }

    [Fact]
    public void RejectsFilesLargerThanTheVersion3Limit()
    {
        StoryLoadException exception = Assert.Throws<StoryLoadException>(
            () => Load(new byte[Z3StoryLoader.MaximumFileSize + 1]));

        Assert.Equal(StoryLoadErrorCode.FileTooLarge, exception.Code);
    }

    [Fact]
    public void RejectsUnsupportedStoryVersions()
    {
        byte[] image = new SyntheticStoryBuilder().WithVersion(5).Build();

        StoryLoadException exception = Assert.Throws<StoryLoadException>(() => Load(image));

        Assert.Equal(StoryLoadErrorCode.UnsupportedVersion, exception.Code);
        Assert.Contains("version 5", exception.Message);
    }

    [Fact]
    public void RejectsDeclaredLengthLongerThanTheStream()
    {
        byte[] image = new SyntheticStoryBuilder().Build();
        WriteWord(image, 0x1A, (image.Length + 2) / 2);

        StoryLoadException exception = Assert.Throws<StoryLoadException>(() => Load(image));

        Assert.Equal(StoryLoadErrorCode.TruncatedFile, exception.Code);
    }

    [Fact]
    public void RejectsDeclaredLengthShorterThanTheHeader()
    {
        byte[] image = new SyntheticStoryBuilder().Build();
        WriteWord(image, 0x1A, (Z3StoryLoader.HeaderSize - 2) / 2);

        StoryLoadException exception = Assert.Throws<StoryLoadException>(() => Load(image));

        Assert.Equal(StoryLoadErrorCode.InvalidHeader, exception.Code);
        Assert.Contains("smaller than the 64-byte header", exception.Message);
    }

    [Fact]
    public void RejectsChecksumMismatch()
    {
        byte[] image = new SyntheticStoryBuilder().WriteByte(0x2000, 0xBA).Build();
        image[0x2000] ^= 0x01;

        StoryLoadException exception = Assert.Throws<StoryLoadException>(() => Load(image));

        Assert.Equal(StoryLoadErrorCode.ChecksumMismatch, exception.Code);
    }

    [Theory]
    [InlineData("static")]
    [InlineData("high")]
    [InlineData("pc")]
    [InlineData("dictionary")]
    [InlineData("objects")]
    [InlineData("globals")]
    [InlineData("abbreviations")]
    public void RejectsInvalidHeaderMemoryRanges(string field)
    {
        SyntheticStoryBuilder builder = new();
        switch (field)
        {
            case "static":
                builder.WithStaticMemoryBase(0x003F);
                break;
            case "high":
                builder.WithHighMemoryBase(0x0FFF);
                break;
            case "pc":
                builder.WithInitialProgramCounter(0x1FFF);
                break;
            case "dictionary":
                builder.WithDictionaryAddress(0x7FFF);
                break;
            case "objects":
                builder.WithObjectTableAddress(0x7FFF);
                break;
            case "globals":
                builder.WithGlobalVariablesAddress(0x0F00);
                break;
            case "abbreviations":
                builder.WithAbbreviationsAddress(0x7FFF);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(field));
        }

        StoryLoadException exception = Assert.Throws<StoryLoadException>(() => Load(builder.Build()));

        Assert.Equal(StoryLoadErrorCode.InvalidHeader, exception.Code);
    }

    [Fact]
    public void AllowsReadOnlyTablesInStaticMemory()
    {
        SyntheticStoryBuilder builder = new();
        builder
            .WithDictionaryAddress(0x1100)
            .WithObjectTableAddress(0x1200)
            .WithAbbreviationsAddress(0x1300)
            .WriteByte(0x1100, 0)
            .WriteByte(0x1101, 6);

        LoadedStory story = Load(builder.Build());

        Assert.Equal(0x1100, story.Header.DictionaryAddress);
        Assert.Equal(0x1200, story.Header.ObjectTableAddress);
        Assert.Equal(0x1300, story.Header.AbbreviationsAddress);
    }

    private LoadedStory Load(byte[] image)
    {
        using MemoryStream stream = new(image, writable: false);
        return _loader.Load(stream);
    }

    private static ushort CalculateChecksum(ReadOnlySpan<byte> image)
    {
        uint sum = 0;
        foreach (byte value in image[Z3StoryLoader.HeaderSize..])
        {
            sum += value;
        }

        return unchecked((ushort)sum);
    }

    private static void WriteWord(byte[] image, int address, int value) =>
        BinaryPrimitives.WriteUInt16BigEndian(image.AsSpan(address, sizeof(ushort)), checked((ushort)value));
}
