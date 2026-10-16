using System.Buffers.Binary;
using GrueGPT.Core.Stories;
using GrueGPT.Tests.TestUtilities;
using Xunit;

namespace GrueGPT.Tests.Stories;

public sealed class Z3TextDecoderTests
{
    [Fact]
    public void DecodesLowercaseTextAndSpaces()
    {
        Z3TextDecoder decoder = CreateDecoder(new SyntheticStoryBuilder()
            .WriteBytes(0x2000, Encode(25, 10, 24, 25, 0, 25, 10, 24, 25)));

        Assert.Equal("test test", decoder.DecodeAt(0x2000));
    }

    [Fact]
    public void AppliesSingleCharacterAlphabetShiftsAndA2Punctuation()
    {
        Z3TextDecoder decoder = CreateDecoder(new SyntheticStoryBuilder()
            .WriteBytes(0x2000, Encode(4, 13, 14, 5, 20)));

        Assert.Equal("Hi!", decoder.DecodeAt(0x2000));
    }

    [Fact]
    public void DecodesA2NewlineAndZsciiEscapes()
    {
        Z3TextDecoder decoder = CreateDecoder(new SyntheticStoryBuilder()
            .WriteBytes(0x2000, Encode(5, 7, 5, 6, 5, 10)));

        Assert.Equal("\né", decoder.DecodeAt(0x2000));
    }

    [Fact]
    public void ExpandsAbbreviationsFromAllThreeGroups()
    {
        SyntheticStoryBuilder builder = new();
        builder.WriteWord(0x300, Z3Memory.PackAddress(0x2100));
        builder.WriteWord(0x300 + 63 * sizeof(ushort), Z3Memory.PackAddress(0x2140));
        builder.WriteWord(0x300 + 69 * sizeof(ushort), Z3Memory.PackAddress(0x2120));
        builder.WriteBytes(0x2000, Encode(1, 0, 2, 31, 3, 5));
        builder.WriteBytes(0x2100, Encode(8, 6, 27, 10));
        builder.WriteBytes(0x2120, Encode(7, 10, 6, 23));
        builder.WriteBytes(0x2140, Encode(28, 20, 17, 11));
        Z3TextDecoder decoder = CreateDecoder(builder);

        Assert.Equal("cavewolfbear", decoder.DecodeAt(0x2000));
    }

    [Fact]
    public void DecodesStringsFromPackedAddresses()
    {
        Z3TextDecoder decoder = CreateDecoder(new SyntheticStoryBuilder()
            .WriteBytes(0x2000, Encode(25, 10, 24, 25)));

        Assert.Equal("test", decoder.DecodePackedAddress(0x1000));
    }

    [Fact]
    public void IgnoresIncompleteFinalAbbreviationAndEscapeConstructions()
    {
        SyntheticStoryBuilder builder = new();
        builder.WriteWord(0x2000, MakeWord(5, 5, 1, isLast: true));
        builder.WriteWord(0x2010, MakeWord(5, 6, 7, isLast: true));
        Z3TextDecoder decoder = CreateDecoder(builder);

        Assert.Equal(string.Empty, decoder.DecodeAt(0x2000));
        Assert.Equal(string.Empty, decoder.DecodeAt(0x2010));
    }

    [Fact]
    public void RejectsNestedAbbreviationsInsteadOfRecursing()
    {
        SyntheticStoryBuilder builder = new();
        builder.WriteWord(0x300, Z3Memory.PackAddress(0x2100));
        builder.WriteBytes(0x2000, Encode(1, 0));
        builder.WriteBytes(0x2100, Encode(2, 0));
        Z3TextDecoder decoder = CreateDecoder(builder);

        Assert.Throws<InvalidDataException>(() => decoder.DecodeAt(0x2000));
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(13, '\n')]
    [InlineData(65, 'A')]
    [InlineData(155, 'ä')]
    [InlineData(223, '¿')]
    [InlineData(129, '?')]
    [InlineData(256, '?')]
    public void ConvertsOutputZscii(int zscii, char? expected)
    {
        Assert.Equal(expected, Z3TextDecoder.ToUnicodeCharacter(zscii));
    }

    private static Z3TextDecoder CreateDecoder(SyntheticStoryBuilder builder)
    {
        using MemoryStream stream = builder.BuildStream();
        LoadedStory story = new Z3StoryLoader().Load(stream);
        return new Z3TextDecoder(new Z3Memory(story), story.Header.AbbreviationsAddress);
    }

    private static byte[] Encode(params byte[] zcharacters)
    {
        int wordCount = (zcharacters.Length + 2) / 3;
        byte[] bytes = new byte[wordCount * sizeof(ushort)];
        for (int wordIndex = 0; wordIndex < wordCount; wordIndex++)
        {
            int baseIndex = wordIndex * 3;
            int first = baseIndex < zcharacters.Length ? zcharacters[baseIndex] : 5;
            int second = baseIndex + 1 < zcharacters.Length ? zcharacters[baseIndex + 1] : 5;
            int third = baseIndex + 2 < zcharacters.Length ? zcharacters[baseIndex + 2] : 5;
            ushort word = (ushort)((first << 10) | (second << 5) | third);
            if (wordIndex == wordCount - 1)
            {
                word |= 0x8000;
            }

            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(wordIndex * sizeof(ushort)), word);
        }

        return bytes;
    }

    private static ushort MakeWord(int first, int second, int third, bool isLast)
    {
        ushort word = (ushort)((first << 10) | (second << 5) | third);
        return isLast ? (ushort)(word | 0x8000) : word;
    }
}
