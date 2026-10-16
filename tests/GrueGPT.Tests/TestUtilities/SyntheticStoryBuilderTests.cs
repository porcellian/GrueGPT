using System.Buffers.Binary;
using GrueGPT.Tests.TestUtilities;
using Xunit;

namespace GrueGPT.Tests;

public sealed class SyntheticStoryBuilderTests
{
    [Fact]
    public void BuildCreatesDeterministicVersion3ImageWithConsistentHeaderAndChecksum()
    {
        byte[] first = new SyntheticStoryBuilder()
            .WriteByte(0x2000, 0xBA)
            .Build();
        byte[] second = new SyntheticStoryBuilder()
            .WriteByte(0x2000, 0xBA)
            .Build();

        Assert.Equal(SyntheticStoryBuilder.DefaultFileSize, first.Length);
        Assert.Equal(first, second);
        Assert.Equal(3, first[0]);
        Assert.Equal(0x2000, ReadWord(first, 0x04));
        Assert.Equal(0x2000, ReadWord(first, 0x06));
        Assert.Equal(0x0100, ReadWord(first, 0x08));
        Assert.Equal(0x0120, ReadWord(first, 0x0A));
        Assert.Equal(0x0200, ReadWord(first, 0x0C));
        Assert.Equal(0x1000, ReadWord(first, 0x0E));
        Assert.Equal(0x0300, ReadWord(first, 0x18));
        Assert.Equal(first.Length / 2, ReadWord(first, 0x1A));
        Assert.Equal(CalculateChecksum(first), ReadWord(first, 0x1C));
    }

    [Fact]
    public void BuildStreamContainsGeneratedImageAndIsReadOnly()
    {
        using MemoryStream stream = new SyntheticStoryBuilder()
            .WriteByte(0x2000, 0xBA)
            .BuildStream();

        Assert.False(stream.CanWrite);
        Assert.Equal(SyntheticStoryBuilder.DefaultFileSize, stream.Length);
        Assert.Equal(0xBA, stream.ToArray()[0x2000]);
    }

    [Fact]
    public void BuilderAllowsHeaderAndBodyOverridesForMalformedInputCases()
    {
        byte[] image = new SyntheticStoryBuilder()
            .WithVersion(5)
            .WithInitialProgramCounter(0xFFFF)
            .WithChecksum(0x1234)
            .WriteWord(0x2000, 0xABCD)
            .Build();

        Assert.Equal(5, image[0]);
        Assert.Equal(0xFFFF, ReadWord(image, 0x06));
        Assert.Equal(0x1234, ReadWord(image, 0x1C));
        Assert.Equal(0xABCD, ReadWord(image, 0x2000));
    }

    [Theory]
    [InlineData(63)]
    [InlineData(65)]
    [InlineData(0x20002)]
    public void ConstructorRejectsFileSizesOutsideVersion3Range(int fileSize)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SyntheticStoryBuilder(fileSize));
    }

    [Fact]
    public void WritesCannotExtendPastStoryImage()
    {
        SyntheticStoryBuilder builder = new();

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.WriteByte(SyntheticStoryBuilder.DefaultFileSize, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.WriteWord(SyntheticStoryBuilder.DefaultFileSize - 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.WriteByte(-1, 1));
    }

    private static ushort ReadWord(byte[] image, int address) =>
        BinaryPrimitives.ReadUInt16BigEndian(image.AsSpan(address, sizeof(ushort)));

    private static ushort CalculateChecksum(byte[] image)
    {
        uint sum = 0;
        foreach (byte value in image.AsSpan(SyntheticStoryBuilder.HeaderSize))
        {
            sum += value;
        }

        return unchecked((ushort)sum);
    }
}
