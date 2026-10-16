using GrueGPT.Core.Stories;
using GrueGPT.Tests.TestUtilities;
using Xunit;

namespace GrueGPT.Tests.Stories;

public sealed class Z3TokenizerTests
{
    private const ushort DictionaryAddress = 0x0500;
    private const ushort TextBufferAddress = 0x0600;
    private const ushort ParseBufferAddress = 0x0700;

    [Fact]
    public void SplitsWordsAndSeparatorsAndWritesDictionaryAddressesLengthsAndPositions()
    {
        Z3Memory memory = CreateMemory(WriteDictionary(
            WriteTextBuffer(new SyntheticStoryBuilder(), "Look,  north. xx", maximumCharacters: 20),
            "look", ",", "north", "."));
        Z3Tokenizer tokenizer = new(memory, DictionaryAddress);

        int tokenCount = tokenizer.TokenizeTextBuffer(TextBufferAddress, ParseBufferAddress);

        Assert.Equal(5, tokenCount);
        Assert.Equal((byte)5, memory.ReadByte(ParseBufferAddress + 1));
        AssertEntry(memory, 0, "look", 4, 1);
        AssertEntry(memory, 1, ",", 1, 5);
        AssertEntry(memory, 2, "north", 5, 8);
        AssertEntry(memory, 3, ".", 1, 13);
        Assert.Equal((ushort)0, ReadDictionaryAddress(memory, 4));
        Assert.Equal((byte)2, memory.ReadByte(ParseBufferAddress + 2 + (4 * 4) + 2));
        Assert.Equal((byte)15, memory.ReadByte(ParseBufferAddress + 2 + (4 * 4) + 3));
    }

    [Fact]
    public void HonorsParseCapacityAndLeavesUnknownSlotsUntouchedWhenRequested()
    {
        SyntheticStoryBuilder builder = WriteDictionary(
            WriteTextBuffer(new SyntheticStoryBuilder(), "xyz look", maximumCharacters: 12),
            "look");
        builder.WriteByte(ParseBufferAddress, 2)
            .WriteWord(ParseBufferAddress + 2, 0xAAAA)
            .WriteByte(ParseBufferAddress + 4, 0xBB)
            .WriteByte(ParseBufferAddress + 5, 0xCC)
            .WriteWord(ParseBufferAddress + 6, 0xDDDD)
            .WriteByte(ParseBufferAddress + 8, 0xEE)
            .WriteByte(ParseBufferAddress + 9, 0xFF);
        Z3Memory memory = CreateMemory(builder);
        Z3Tokenizer tokenizer = new(memory, DictionaryAddress);

        int tokenCount = tokenizer.TokenizeTextBuffer(TextBufferAddress, ParseBufferAddress, preserveUnknownEntries: true);

        Assert.Equal(2, tokenCount);
        Assert.Equal((byte)2, memory.ReadByte(ParseBufferAddress + 1));
        Assert.Equal((ushort)0xAAAA, memory.ReadWord(ParseBufferAddress + 2));
        Assert.Equal((byte)0xBB, memory.ReadByte(ParseBufferAddress + 4));
        Assert.Equal((byte)0xCC, memory.ReadByte(ParseBufferAddress + 5));
        AssertEntry(memory, 1, "look", 4, 5);
    }

    [Fact]
    public void UsesAnExplicitUnsortedDictionaryWhenSupplied()
    {
        const ushort userDictionaryAddress = 0x0800;
        SyntheticStoryBuilder builder = WriteDictionary(
            WriteTextBuffer(new SyntheticStoryBuilder(), "north", maximumCharacters: 8),
            "look");
        WriteDictionaryAt(builder, userDictionaryAddress, sorted: false, "north", "lamp");
        Z3Memory memory = CreateMemory(builder);
        Z3Tokenizer tokenizer = new(memory, DictionaryAddress);

        tokenizer.TokenizeTextBuffer(TextBufferAddress, ParseBufferAddress, userDictionaryAddress);

        Assert.Equal((ushort)(userDictionaryAddress + 6), ReadDictionaryAddress(memory, 0));
    }

    [Fact]
    public void RejectsUnterminatedTextAndBuffersOutsideDynamicMemory()
    {
        SyntheticStoryBuilder unterminated = WriteDictionary(new SyntheticStoryBuilder(), "look")
            .WriteByte(TextBufferAddress, 2)
            .WriteByte(TextBufferAddress + 1, (byte)'x')
            .WriteByte(TextBufferAddress + 2, (byte)'y')
            .WriteByte(TextBufferAddress + 3, (byte)'z');
        Z3Tokenizer tokenizer = new(CreateMemory(unterminated), DictionaryAddress);
        Assert.Throws<InvalidDataException>(() => tokenizer.TokenizeTextBuffer(TextBufferAddress, ParseBufferAddress));

        Z3Memory memory = CreateMemory(WriteDictionary(WriteTextBuffer(new SyntheticStoryBuilder(), "look", 8), "look"));
        Z3Tokenizer regularTokenizer = new(memory, DictionaryAddress);
        Assert.Throws<InvalidDataException>(() => regularTokenizer.TokenizeTextBuffer(0x1000, ParseBufferAddress));
        Assert.Throws<InvalidDataException>(() => regularTokenizer.TokenizeTextBuffer(TextBufferAddress, 0x0FFF));
    }

    [Fact]
    public void RejectsTextContainingNonPrintableZscii()
    {
        SyntheticStoryBuilder builder = WriteDictionary(new SyntheticStoryBuilder(), "look")
            .WriteByte(TextBufferAddress, 5)
            .WriteBytes(TextBufferAddress + 1, [0x01, 0]);
        Z3Tokenizer tokenizer = new(CreateMemory(builder), DictionaryAddress);

        Assert.Throws<InvalidDataException>(() => tokenizer.TokenizeTextBuffer(TextBufferAddress, ParseBufferAddress));
    }

    private static void AssertEntry(Z3Memory memory, int slot, string word, byte length, byte position)
    {
        int address = ParseBufferAddress + 2 + (slot * 4);
        ushort? expectedAddress = new Z3Dictionary(memory, DictionaryAddress).FindEntry(Z3Dictionary.EncodeWord(word));
        Assert.NotNull(expectedAddress);
        Assert.Equal(expectedAddress.Value, memory.ReadWord(address));
        Assert.Equal(length, memory.ReadByte(address + 2));
        Assert.Equal(position, memory.ReadByte(address + 3));
    }

    private static ushort ReadDictionaryAddress(Z3Memory memory, int slot) =>
        memory.ReadWord(ParseBufferAddress + 2 + (slot * 4));

    private static SyntheticStoryBuilder WriteTextBuffer(
        SyntheticStoryBuilder builder,
        string text,
        byte maximumCharacters)
    {
        byte[] bytes = System.Text.Encoding.ASCII.GetBytes(text);
        return builder.WriteByte(TextBufferAddress, maximumCharacters)
            .WriteBytes(TextBufferAddress + 1, bytes)
            .WriteByte(TextBufferAddress + 1 + bytes.Length, 0)
            .WriteByte(ParseBufferAddress, 8);
    }

    private static SyntheticStoryBuilder WriteDictionary(SyntheticStoryBuilder builder, params string[] words) =>
        WriteDictionaryAt(builder, DictionaryAddress, sorted: true, words);

    private static SyntheticStoryBuilder WriteDictionaryAt(
        SyntheticStoryBuilder builder,
        ushort address,
        bool sorted,
        params string[] words)
    {
        if (sorted)
        {
            Array.Sort(words, (left, right) => Z3Dictionary.EncodeWord(left).AsSpan()
                .SequenceCompareTo(Z3Dictionary.EncodeWord(right)));
        }

        builder.WriteByte(address, 2)
            .WriteByte(address + 1, (byte)',')
            .WriteByte(address + 2, (byte)'.')
            .WriteByte(address + 3, 7)
            .WriteWord(address + 4, sorted ? (ushort)words.Length : unchecked((ushort)-words.Length));

        for (int index = 0; index < words.Length; index++)
        {
            int entryAddress = address + 6 + (index * 7);
            builder.WriteBytes(entryAddress, Z3Dictionary.EncodeWord(words[index]));
            builder.WriteBytes(entryAddress + 4, [0, 0, 0]);
        }

        return builder;
    }

    private static Z3Memory CreateMemory(SyntheticStoryBuilder builder)
    {
        using MemoryStream stream = builder.BuildStream();
        LoadedStory story = new Z3StoryLoader().Load(stream);
        return new Z3Memory(story);
    }
}
