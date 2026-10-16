using GrueGPT.Core.Stories;
using GrueGPT.Tests.TestUtilities;
using Xunit;

namespace GrueGPT.Tests.Stories;

public sealed class Z3DictionaryTests
{
    private const ushort DictionaryAddress = 0x0500;

    [Fact]
    public void ParsesDictionaryHeaderAndFindsSortedEncodedWords()
    {
        (Z3Memory memory, IReadOnlyList<(string Word, ushort Address)> entries) = CreateDictionary("lamp", "look", "north");
        Z3Dictionary dictionary = new(memory, DictionaryAddress);

        Assert.Equal((byte)7, dictionary.EntryLength);
        Assert.Equal(3, dictionary.EntryCount);
        Assert.True(dictionary.IsSorted);
        Assert.Contains((byte)',', dictionary.WordSeparators);
        Assert.Contains((byte)'.', dictionary.WordSeparators);
        Assert.Equal(entries[1].Address, dictionary.FindEntry(Z3Dictionary.EncodeWord("look")));
        Assert.Null(dictionary.FindEntry(Z3Dictionary.EncodeWord("south")));
    }

    [Fact]
    public void EncodesDictionaryWordsCaseInsensitivelyAndTruncatesToSixZCharacters()
    {
        Assert.Equal(Z3Dictionary.EncodeWord("north"), Z3Dictionary.EncodeWord("NORTH"));
        Assert.Equal(Z3Dictionary.EncodeWord("abcdef"), Z3Dictionary.EncodeWord("abcdefg"));
        Assert.Equal(Z3Dictionary.EncodeWord("abcde"), Z3Dictionary.EncodeWord("abcde,"));
        Assert.Equal(4, Z3Dictionary.EncodeWord("a?").Length);
    }

    [Fact]
    public void SearchesUnsortedUserDictionariesLinearly()
    {
        (Z3Memory memory, IReadOnlyList<(string Word, ushort Address)> entries) =
            CreateDictionary(["north", "look", "lamp"], sorted: false);
        Z3Dictionary dictionary = new(memory, DictionaryAddress);

        Assert.False(dictionary.IsSorted);
        Assert.Equal(entries[0].Address, dictionary.FindEntry(Z3Dictionary.EncodeWord("north")));
        Assert.Equal(entries[2].Address, dictionary.FindEntry(Z3Dictionary.EncodeWord("lamp")));
    }

    [Fact]
    public void EmptyDictionaryHasNoMatches()
    {
        SyntheticStoryBuilder story = StartDictionary(sorted: true, entryCount: 0);
        Z3Dictionary dictionary = CreateTable(story);

        Assert.Equal(0, dictionary.EntryCount);
        Assert.Null(dictionary.FindEntry(Z3Dictionary.EncodeWord("look")));
    }

    [Fact]
    public void RejectsTheMostNegativeEntryCountWhenItsTableCannotFit()
    {
        SyntheticStoryBuilder story = StartDictionary(sorted: false, entryCount: short.MinValue);

        Assert.Throws<InvalidDataException>(() => CreateTable(story));
    }

    [Fact]
    public void RejectsUnsortedOrDuplicateEntriesWhenTheHeaderDeclaresSortedOrder()
    {
        SyntheticStoryBuilder builder = StartDictionary(sorted: true, entryCount: 2);
        WriteEntry(builder, 0, "north");
        WriteEntry(builder, 1, "look");
        Assert.Throws<InvalidDataException>(() => CreateTable(builder));
    }

    [Fact]
    public void RejectsInvalidHeaderLengthsAndTruncatedEntryTables()
    {
        SyntheticStoryBuilder shortEntry = new SyntheticStoryBuilder()
            .WriteByte(DictionaryAddress, 0)
            .WriteByte(DictionaryAddress + 1, 3)
            .WriteWord(DictionaryAddress + 2, 0);
        Assert.Throws<InvalidDataException>(() => CreateTable(shortEntry));

        const ushort truncatedDictionaryAddress = 0x7FFC;
        SyntheticStoryBuilder truncated = new SyntheticStoryBuilder()
            .WriteByte(truncatedDictionaryAddress, 0)
            .WriteByte(truncatedDictionaryAddress + 1, 7)
            .WriteWord(truncatedDictionaryAddress + 2, 1);
        Assert.Throws<InvalidDataException>(() => CreateTable(truncated, truncatedDictionaryAddress));
    }

    [Fact]
    public void RejectsSpacesAndUndefinedZsciiAsDictionarySeparators()
    {
        SyntheticStoryBuilder spaceSeparator = new SyntheticStoryBuilder()
            .WriteByte(DictionaryAddress, 1)
            .WriteByte(DictionaryAddress + 1, 32);
        SyntheticStoryBuilder undefinedSeparator = new SyntheticStoryBuilder()
            .WriteByte(DictionaryAddress, 1)
            .WriteByte(DictionaryAddress + 1, 1);

        Assert.Throws<InvalidDataException>(() => CreateTable(spaceSeparator));
        Assert.Throws<InvalidDataException>(() => CreateTable(undefinedSeparator));
    }

    [Fact]
    public void RejectsWordsWithoutDefinedVersion3ZsciiCharacters()
    {
        Assert.Throws<InvalidDataException>(() => Z3Dictionary.EncodeWord("word\u0001"));
    }

    private static (Z3Memory Memory, IReadOnlyList<(string Word, ushort Address)> Entries) CreateDictionary(
        params string[] words) => CreateDictionary(words, sorted: true);

    private static (Z3Memory Memory, IReadOnlyList<(string Word, ushort Address)> Entries) CreateDictionary(
        string[] words,
        bool sorted)
    {
        if (sorted)
        {
            Array.Sort(words, CompareEncodedWords);
        }

        SyntheticStoryBuilder builder = StartDictionary(sorted, words.Length);
        List<(string Word, ushort Address)> entries = [];
        int entryAddress = DictionaryAddress + 6;
        for (int index = 0; index < words.Length; index++)
        {
            WriteEntry(builder, index, words[index]);
            entries.Add((words[index], checked((ushort)entryAddress)));
            entryAddress += 7;
        }

        return (CreateMemory(builder), entries);
    }

    private static SyntheticStoryBuilder StartDictionary(bool sorted, int entryCount) => new SyntheticStoryBuilder()
        .WriteByte(DictionaryAddress, 2)
        .WriteByte(DictionaryAddress + 1, (byte)',')
        .WriteByte(DictionaryAddress + 2, (byte)'.')
        .WriteByte(DictionaryAddress + 3, 7)
        .WriteWord(DictionaryAddress + 4, sorted ? (ushort)entryCount : unchecked((ushort)-entryCount));

    private static void WriteEntry(SyntheticStoryBuilder builder, int index, string word)
    {
        int entryAddress = DictionaryAddress + 6 + (index * 7);
        builder.WriteBytes(entryAddress, Z3Dictionary.EncodeWord(word));
        builder.WriteBytes(entryAddress + 4, [0xA0, 0xA1, 0xA2]);
    }

    private static Z3Dictionary CreateTable(SyntheticStoryBuilder builder, ushort dictionaryAddress = DictionaryAddress) =>
        new(CreateMemory(builder), dictionaryAddress);

    private static Z3Memory CreateMemory(SyntheticStoryBuilder builder)
    {
        using MemoryStream stream = builder.BuildStream();
        LoadedStory story = new Z3StoryLoader().Load(stream);
        return new Z3Memory(story);
    }

    private static int CompareEncodedWords(string left, string right)
    {
        byte[] leftBytes = Z3Dictionary.EncodeWord(left);
        byte[] rightBytes = Z3Dictionary.EncodeWord(right);
        return leftBytes.AsSpan().SequenceCompareTo(rightBytes);
    }
}
