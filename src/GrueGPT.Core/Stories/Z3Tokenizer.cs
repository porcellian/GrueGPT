namespace GrueGPT.Core.Stories;

/// <summary>Tokenizes a Version 3 text buffer and writes its parse-buffer entries.</summary>
public sealed class Z3Tokenizer
{
    private const int ParseHeaderLength = 2;
    private const int ParseEntryLength = 4;

    private readonly Z3Memory _memory;
    private readonly ushort _defaultDictionaryAddress;

    public Z3Tokenizer(Z3Memory memory, ushort defaultDictionaryAddress)
    {
        ArgumentNullException.ThrowIfNull(memory);
        _memory = memory;
        _defaultDictionaryAddress = defaultDictionaryAddress;
    }

    /// <summary>
    /// Tokenizes text from a Version 3 input buffer and writes token records to a parse buffer.
    /// The dictionary address may be zero to select the story's default dictionary.
    /// </summary>
    /// <param name="preserveUnknownEntries">Leaves the existing parse entry unchanged for unrecognized words.</param>
    /// <returns>The number of tokens written to parse-buffer slots.</returns>
    public int TokenizeTextBuffer(
        ushort textBufferAddress,
        ushort parseBufferAddress,
        ushort dictionaryAddress = 0,
        bool preserveUnknownEntries = false)
    {
        int textLength = _memory.ReadByte(textBufferAddress);
        int textBufferSize = textLength + 2;
        ValidateDynamicRange(textBufferAddress, textBufferSize, "text buffer");
        if (textLength < 1)
        {
            throw new InvalidDataException("A Version 3 text buffer must allow at least one typed character.");
        }

        int maximumWords = _memory.ReadByte(parseBufferAddress);
        int parseBufferSize = ParseHeaderLength + (maximumWords * ParseEntryLength);
        ValidateDynamicRange(parseBufferAddress, parseBufferSize, "parse buffer");

        List<byte> text = ReadTextBuffer(textBufferAddress, textLength);
        ushort selectedDictionary = dictionaryAddress == 0 ? _defaultDictionaryAddress : dictionaryAddress;
        Z3Dictionary dictionary = new(_memory, selectedDictionary);
        List<Token> tokens = SplitIntoTokens(text, dictionary.WordSeparators);
        int tokenCount = Math.Min(tokens.Count, maximumWords);

        for (int index = 0; index < tokenCount; index++)
        {
            Token token = tokens[index];
            ushort? dictionaryEntry = dictionary.FindEntry(Z3Dictionary.EncodeWord(token.Text));
            if (dictionaryEntry is null && preserveUnknownEntries)
            {
                continue;
            }

            int entryAddress = parseBufferAddress + ParseHeaderLength + (index * ParseEntryLength);
            _memory.WriteWord(entryAddress, dictionaryEntry ?? 0);
            _memory.WriteByte(entryAddress + 2, checked((byte)token.Length));
            _memory.WriteByte(entryAddress + 3, checked((byte)token.Position));
        }

        _memory.WriteByte(parseBufferAddress + 1, checked((byte)tokenCount));
        return tokenCount;
    }

    private List<byte> ReadTextBuffer(ushort address, int maximumCharacters)
    {
        List<byte> characters = [];
        bool foundTerminator = false;
        for (int index = 0; index <= maximumCharacters; index++)
        {
            byte character = _memory.ReadByte(address + 1 + index);
            if (character == 0)
            {
                foundTerminator = true;
                break;
            }

            if (index == maximumCharacters)
            {
                throw new InvalidDataException("The Version 3 text buffer is not terminated within its declared capacity.");
            }

            characters.Add(character);
        }

        if (!foundTerminator)
        {
            throw new InvalidDataException("The Version 3 text buffer is not zero-terminated.");
        }

        return characters;
    }

    private static List<Token> SplitIntoTokens(IReadOnlyList<byte> text, IReadOnlySet<byte> separators)
    {
        List<Token> tokens = [];
        int index = 0;
        while (index < text.Count)
        {
            byte character = text[index];
            if (character == 32)
            {
                index++;
                continue;
            }

            if (separators.Contains(character))
            {
                tokens.Add(new Token(ToDictionaryCharacter(character).ToString(), Length: 1, Position: index + 1));
                index++;
                continue;
            }

            int start = index;
            while (index < text.Count && text[index] != 32 && !separators.Contains(text[index]))
            {
                index++;
            }

            char[] wordCharacters = new char[index - start];
            for (int characterIndex = 0; characterIndex < wordCharacters.Length; characterIndex++)
            {
                wordCharacters[characterIndex] = ToDictionaryCharacter(text[start + characterIndex]);
            }

            tokens.Add(new Token(new string(wordCharacters), Length: index - start, Position: start + 1));
        }

        return tokens;
    }

    private static char ToDictionaryCharacter(byte zscii)
    {
        if (zscii == 13 || zscii is >= 32 and <= 126 || zscii is >= 155 and <= 223)
        {
            return Z3TextDecoder.ToUnicodeCharacter(zscii)
                ?? throw new InvalidDataException($"ZSCII code {zscii} has no Version 3 dictionary representation.");
        }

        throw new InvalidDataException($"ZSCII code {zscii} is not a printable Version 3 input character.");
    }

    private void ValidateDynamicRange(int address, int length, string name)
    {
        if (length <= 0 || address < 0 || address > _memory.StaticMemoryBase - length)
        {
            throw new InvalidDataException($"The Version 3 {name} must fit entirely in dynamic memory.");
        }

        if (address + length > _memory.Length)
        {
            throw new InvalidDataException($"The Version 3 {name} extends beyond story memory.");
        }
    }

    private sealed record Token(string Text, int Length, int Position);
}
