using GrueGPT.Core.Stories;
using GrueGPT.Tests.TestUtilities;
using Xunit;

namespace GrueGPT.Tests.Stories;

public sealed class Z3ObjectTableTests
{
    private const int ObjectTableAddress = 0x0120;
    private const int ObjectEntriesAddress = ObjectTableAddress + (31 * sizeof(ushort));

    [Fact]
    public void ReadsAttributesInMostSignificantBitFirstOrderAndUpdatesThem()
    {
        Z3ObjectTable objects = CreateObjectTable(new SyntheticStoryBuilder()
            .WriteByte(ObjectAddress(1), 0x81)
            .WriteByte(ObjectAddress(1) + 3, 0x01));

        Assert.True(objects.TestAttribute(1, 0));
        Assert.True(objects.TestAttribute(1, 7));
        Assert.True(objects.TestAttribute(1, 31));
        Assert.False(objects.TestAttribute(1, 8));

        objects.SetAttribute(1, 8);
        objects.ClearAttribute(1, 7);

        Assert.True(objects.TestAttribute(1, 0));
        Assert.False(objects.TestAttribute(1, 7));
        Assert.True(objects.TestAttribute(1, 8));
        Assert.True(objects.TestAttribute(1, 31));
    }

    [Theory]
    [InlineData(32)]
    public void RejectsAttributeNumbersOutsideTheVersion3Range(byte attributeNumber)
    {
        Z3ObjectTable objects = CreateObjectTable(new SyntheticStoryBuilder());

        Assert.Throws<ArgumentOutOfRangeException>(() => objects.TestAttribute(1, attributeNumber));
    }

    [Fact]
    public void ReadsPropertyDefaultsAndDeclaredPropertiesInDescendingOrder()
    {
        Z3ObjectTable objects = CreateObjectTable(PropertyStory());

        Assert.Equal(0xCAFE, objects.GetPropertyDefault(5));
        Assert.Equal(0x1234, objects.GetProperty(1, 5));
        Assert.Equal(0x56, objects.GetProperty(1, 2));
        Assert.Equal(0xCAFE, objects.GetProperty(1, 7));
        Assert.Equal(0x0404, objects.GetPropertyAddress(1, 5));
        Assert.Equal((byte)2, objects.GetPropertyLength(0x0404));
        Assert.Equal((byte)1, objects.GetPropertyLength(0x0407));
        Assert.Equal((byte)0, objects.GetPropertyLength(0));
        Assert.Equal((byte)5, objects.GetNextProperty(1, 0));
        Assert.Equal((byte)2, objects.GetNextProperty(1, 5));
        Assert.Equal((byte)0, objects.GetNextProperty(1, 2));
        Assert.Equal((ushort)0, objects.GetPropertyAddress(1, 7));
    }

    [Fact]
    public void UpdatesOneAndTwoBytePropertiesThroughDynamicMemory()
    {
        Z3ObjectTable objects = CreateObjectTable(PropertyStory());

        objects.PutProperty(1, 5, 0xBEEF);
        objects.PutProperty(1, 2, 0xABCD);

        Assert.Equal(0xBEEF, objects.GetProperty(1, 5));
        Assert.Equal(0xCD, objects.GetProperty(1, 2));
    }

    [Fact]
    public void RejectsMissingPropertiesAndUnsupportedValueLengths()
    {
        Z3ObjectTable objects = CreateObjectTable(new SyntheticStoryBuilder()
            .WriteWord(ObjectAddress(1) + 7, 0x0400)
            .WriteByte(0x0400, 0)
            .WriteByte(0x0401, 0x43)
            .WriteBytes(0x0402, [0x11, 0x22, 0x33, 0x44])
            .WriteByte(0x0406, 0));

        Assert.Throws<InvalidOperationException>(() => objects.PutProperty(1, 7, 1));
        Assert.Throws<InvalidOperationException>(() => objects.GetProperty(1, 3));
        Assert.Throws<InvalidOperationException>(() => objects.PutProperty(1, 3, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => objects.GetPropertyDefault(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => objects.GetPropertyDefault(32));
        Assert.Throws<ArgumentOutOfRangeException>(() => objects.GetNextProperty(1, 32));
    }

    [Fact]
    public void ReadsAndUpdatesParentSiblingAndChildLinksWhenObjectsMove()
    {
        SyntheticStoryBuilder builder = new SyntheticStoryBuilder()
            .WriteByte(ObjectAddress(1) + 6, 2)
            .WriteByte(ObjectAddress(2) + 4, 1)
            .WriteByte(ObjectAddress(2) + 5, 3)
            .WriteByte(ObjectAddress(3) + 4, 1)
            .WriteByte(ObjectAddress(4) + 6, 5)
            .WriteByte(ObjectAddress(5) + 4, 4);
        Z3ObjectTable objects = CreateObjectTable(builder);

        Assert.Equal((byte)2, objects.GetChild(1));
        Assert.Equal((byte)1, objects.GetParent(2));
        Assert.Equal((byte)3, objects.GetSibling(2));

        objects.InsertObject(2, 4);

        Assert.Equal((byte)3, objects.GetChild(1));
        Assert.Equal((byte)2, objects.GetChild(4));
        Assert.Equal((byte)5, objects.GetSibling(2));
        Assert.Equal((byte)4, objects.GetParent(2));

        objects.RemoveObject(2);

        Assert.Equal((byte)5, objects.GetChild(4));
        Assert.Equal((byte)0, objects.GetParent(2));
        Assert.Equal((byte)0, objects.GetSibling(2));
    }

    [Fact]
    public void RemovesAParentlessObjectByClearingItsSiblingLink()
    {
        Z3ObjectTable objects = CreateObjectTable(new SyntheticStoryBuilder()
            .WriteByte(ObjectAddress(4) + 5, 9));

        objects.RemoveObject(4);

        Assert.Equal((byte)0, objects.GetParent(4));
        Assert.Equal((byte)0, objects.GetSibling(4));
    }

    [Fact]
    public void RejectsObjectZeroAndEntriesThatCannotFitInDynamicMemory()
    {
        Z3ObjectTable objects = CreateObjectTable(new SyntheticStoryBuilder().WithStaticMemoryBase(0x0400));

        Assert.Throws<ArgumentOutOfRangeException>(() => objects.GetParent(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => objects.GetParent(byte.MaxValue));
    }

    [Fact]
    public void RejectsUnorderedOrUnterminatedPropertyTables()
    {
        Z3ObjectTable unordered = CreateObjectTable(new SyntheticStoryBuilder()
            .WriteWord(ObjectAddress(1) + 7, 0x0400)
            .WriteByte(0x0400, 0)
            .WriteByte(0x0401, 0x02)
            .WriteByte(0x0402, 0x11)
            .WriteByte(0x0403, 0x03));

        Assert.Throws<InvalidOperationException>(() => unordered.GetNextProperty(1, 0));

        Z3ObjectTable unterminated = CreateObjectTable(new SyntheticStoryBuilder()
            .WithStaticMemoryBase(0x0500)
            .WriteWord(ObjectAddress(1) + 7, 0x04FE)
            .WriteByte(0x04FE, 0)
            .WriteByte(0x04FF, 0x01));

        Assert.Throws<InvalidOperationException>(() => unterminated.GetNextProperty(1, 0));
    }

    private static SyntheticStoryBuilder PropertyStory() => new SyntheticStoryBuilder()
        .WriteWord(ObjectTableAddress + ((5 - 1) * sizeof(ushort)), 0xCAFE)
        .WriteWord(ObjectTableAddress + ((7 - 1) * sizeof(ushort)), 0xCAFE)
        .WriteWord(ObjectAddress(1) + 7, 0x0400)
        .WriteByte(0x0400, 1)
        .WriteWord(0x0401, 0xFFFF)
        .WriteByte(0x0403, 0x25)
        .WriteWord(0x0404, 0x1234)
        .WriteByte(0x0406, 0x02)
        .WriteByte(0x0407, 0x56)
        .WriteByte(0x0408, 0);

    private static int ObjectAddress(byte objectNumber) =>
        ObjectEntriesAddress + ((objectNumber - 1) * 9);

    private static Z3ObjectTable CreateObjectTable(SyntheticStoryBuilder builder)
    {
        using MemoryStream stream = builder.BuildStream();
        LoadedStory story = new Z3StoryLoader().Load(stream);
        return new Z3ObjectTable(new Z3Memory(story), ObjectTableAddress);
    }
}
