using GrueGPT.Core.Stories;
using GrueGPT.Tests.TestUtilities;
using Xunit;

namespace GrueGPT.Tests.Stories;

public sealed class Z3MemoryTests
{
    [Fact]
    public void ReadsBytesAndBigEndianWordsFromAnIndependentMutableCopy()
    {
        LoadedStory story = Load(new SyntheticStoryBuilder()
            .WriteByte(0x0200, 0x12)
            .WriteByte(0x0201, 0x34)
            .Build());
        Z3Memory memory = new(story);

        Assert.Equal(0x12, memory.ReadByte(0x0200));
        Assert.Equal(0x1234, memory.ReadWord(0x0200));
        memory.WriteByte(0x0200, 0xAB);

        Assert.Equal(0xAB, memory.ReadByte(0x0200));
        Assert.Equal(0x12, story.Image.Span[0x0200]);
    }

    [Fact]
    public void ReadsStaticMemoryButRejectsWritesThere()
    {
        Z3Memory memory = new(Load(new SyntheticStoryBuilder()
            .WithStaticMemoryBase(0x1000)
            .WriteByte(0x1000, 0x5A)
            .Build()));

        Assert.Equal(0x5A, memory.ReadByte(0x1000));
        Assert.Throws<InvalidOperationException>(() => memory.WriteByte(0x1000, 0));
        Assert.Throws<InvalidOperationException>(() => memory.WriteWord(0x0FFF, 0));
    }

    [Fact]
    public void RejectsAWordWriteCrossingIntoStaticMemoryWithoutChangingDynamicMemory()
    {
        Z3Memory memory = new(Load(new SyntheticStoryBuilder()
            .WithStaticMemoryBase(0x1000)
            .WriteByte(0x0FFF, 0x5A)
            .WriteByte(0x1000, 0xA5)
            .Build()));

        Assert.Throws<InvalidOperationException>(() => memory.WriteWord(0x0FFF, 0x1234));

        Assert.Equal((byte)0x5A, memory.ReadByte(0x0FFF));
        Assert.Equal((byte)0xA5, memory.ReadByte(0x1000));
    }

    [Fact]
    public void ReadsPackedMemoryBeyond64KiBInLongVersion3Stories()
    {
        const int address = 0x10000;
        Z3Memory memory = new(Load(new SyntheticStoryBuilder(fileSize: 0x1FFFE)
            .WriteByte(address, 0xBE)
            .WriteByte(address + 1, 0xEF)
            .Build()));

        Assert.Equal(0xBEEF, memory.ReadWord(address));
        Assert.Equal(address, Z3Memory.UnpackAddress(Z3Memory.PackAddress(address)));
    }

    [Fact]
    public void ReadsAndWritesWordsInBigEndianOrder()
    {
        Z3Memory memory = new(Load(new SyntheticStoryBuilder().Build()));

        memory.WriteWord(0x0200, 0xBEEF);

        Assert.Equal(0xBE, memory.ReadByte(0x0200));
        Assert.Equal(0xEF, memory.ReadByte(0x0201));
        Assert.Equal(0xBEEF, memory.ReadWord(0x0200));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0x10000)]
    [InlineData(0x8000)]
    public void RejectsByteReadsOutsideAddressableStoryBytes(int address)
    {
        Z3Memory memory = new(Load(new SyntheticStoryBuilder().Build()));

        Assert.Throws<ArgumentOutOfRangeException>(() => memory.ReadByte(address));
    }

    [Fact]
    public void RejectsWordReadsPastStoryImageOrAddressSpace()
    {
        Z3Memory memory = new(Load(new SyntheticStoryBuilder().Build()));

        Assert.Throws<ArgumentOutOfRangeException>(() => memory.ReadWord(0x7FFF));
        Assert.Throws<ArgumentOutOfRangeException>(() => memory.ReadWord(0xFFFF));
    }

    [Fact]
    public void StoryWritesOnlyChangeGameWritableVersion3HeaderFlags()
    {
        Z3Memory memory = new(Load(new SyntheticStoryBuilder()
            .WriteByte(0x01, 0x72)
            .WriteWord(0x10, 0xA5FF)
            .Build()));

        memory.WriteByte(0x01, 0x00);
        memory.WriteWord(0x10, 0x5A00);

        Assert.Equal(0x72, memory.ReadByte(0x01));
        Assert.Equal(0xA5F8, memory.ReadWord(0x10));

        memory.WriteWord(0x10, 0x0007);

        Assert.Equal(0xA5FB, memory.ReadWord(0x10));
    }

    [Fact]
    public void InterpreterCanChangeOnlyItsVersion3HeaderFlags()
    {
        Z3Memory memory = new(Load(new SyntheticStoryBuilder()
            .WriteByte(0x01, 0x82)
            .WriteWord(0x10, 0x1234)
            .Build()));

        memory.SetInterpreterFlags1(0x70, 0x30);

        Assert.Equal(0xB2, memory.ReadByte(0x01));
        Assert.Equal(0x1234, memory.ReadWord(0x10));
        Assert.Throws<ArgumentOutOfRangeException>(() => memory.SetInterpreterFlags1(0x01, 0x01));
        Assert.Throws<ArgumentOutOfRangeException>(() => memory.SetInterpreterFlags2(0x04, 0x04));
        Assert.Throws<ArgumentOutOfRangeException>(() => memory.SetInterpreterFlags2(0x08, 0x08));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0x2000, 0x1000)]
    [InlineData(0x1FFFE, 0xFFFF)]
    public void ConvertsBetweenVersion3ByteAndPackedAddresses(int byteAddress, int packedAddress)
    {
        Assert.Equal(packedAddress, Z3Memory.PackAddress(byteAddress));
        Assert.Equal(byteAddress, Z3Memory.UnpackAddress((ushort)packedAddress));
    }

    [Theory]
    [InlineData(-2)]
    [InlineData(1)]
    [InlineData(0x20000)]
    public void RejectsByteAddressesThatCannotBePacked(int byteAddress)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Z3Memory.PackAddress(byteAddress));
    }

    private static LoadedStory Load(byte[] image)
    {
        using MemoryStream stream = new(image, writable: false);
        return new Z3StoryLoader().Load(stream);
    }
}
