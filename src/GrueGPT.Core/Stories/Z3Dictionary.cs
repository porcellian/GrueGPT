using System.Buffers.Binary;

namespace GrueGPT.Core.Stories;

/// <summary>Reads and searches a Version 3 dictionary table.</summary>
public sealed class Z3Dictionary
{
    public const int EncodedWordLength = 4;
    public const int EncodedZCharacterCount = 6;

    private const int DictionaryHeaderFixedSize = 3;
    private const int MinimumDictionaryAddress = 0x40;
    private const string AlphabetA2 = " ^0123456789.,!?_#'\"/\\-:()";

    private readonly Z3Memory _memory;
    private readonly int _entriesAddress;

    public Z3Dictionary(Z3Memory memory, ushort dictionaryAddress)
    {
        ArgumentNullException.ThrowIfNull(memory);
        if (dictionaryAddress < MinimumDictionaryAddress)
        {
            throw new ArgumentOutOfRangeException(nameof(dictionaryAddress), "A Version 3 dictionary must be outside the Z-machine header.");
        }

        _memory = memory;
        Address = dictionaryAddress;

        int separatorCount = memory.ReadByte(dictionaryAddress);
        int fixedHeaderAddress = dictionaryAddress + 1 + separatorCount;
        ValidateRange(fixedHeaderAddress, DictionaryHeaderFixedSize, "dictionary header");

        HashSet<byte> separators = [];
        for (int index = 0; index < separatorCount; index++)
        {
            byte separator = memory.ReadByte(dictionaryAddress + 1 + index);
            if (separator == 32)
            {
                throw new InvalidDataException("A Version 3 dictionary cannot declare space as a word separator.");
            }

            if (separator != 13 && (separator < 33 || separator > 126) && (separator < 155 || separator > 223))
            {
                throw new InvalidDataException($"ZSCII code {separator} is not a defined Version 3 word separator.");
            }

            separators.Add(separator);
        }

        WordSeparators = separators;
        EntryLength = memory.ReadByte(fixedHeaderAddress);
        if (EntryLength < EncodedWordLength)
        {
            throw new InvalidDataException($"A Version 3 dictionary entry must be at least {EncodedWordLength} bytes long.");
        }

        short signedEntryCount = unchecked((short)memory.ReadWord(fixedHeaderAddress + 1));
        IsSorted = signedEntryCount >= 0;
        EntryCount = Math.Abs((int)signedEntryCount);
        _entriesAddress = fixedHeaderAddress + DictionaryHeaderFixedSize;

        long tableEnd = (long)_entriesAddress + ((long)EntryCount * EntryLength);
        if (tableEnd > memory.Length || tableEnd > ushort.MaxValue + 1L)
        {
            throw new InvalidDataException("The Version 3 dictionary entries extend beyond addressable story memory.");
        }

        if (IsSorted)
        {
            ValidateEntryOrdering();
        }
    }

    /// <summary>Dictionary table's byte address.</summary>
    public ushort Address { get; }

    /// <summary>Word separator codes declared by this dictionary.</summary>
    public IReadOnlySet<byte> WordSeparators { get; }

    /// <summary>Length of each entry, including its four encoded-text bytes.</summary>
    public byte EntryLength { get; }

    /// <summary>Number of entries, irrespective of the sign used in the dictionary header.</summary>
    public int EntryCount { get; }

    /// <summary>False when the dictionary header uses a negative count for an unsorted user dictionary.</summary>
    public bool IsSorted { get; }

    /// <summary>Finds an encoded word, returning its entry address or null if absent.</summary>
    public ushort? FindEntry(ReadOnlySpan<byte> encodedWord)
    {
        if (encodedWord.Length != EncodedWordLength)
        {
            throw new ArgumentException($"A Version 3 dictionary key must contain {EncodedWordLength} bytes.", nameof(encodedWord));
        }

        if (IsSorted)
        {
            int low = 0;
            int high = EntryCount - 1;
            while (low <= high)
            {
                int middle = low + ((high - low) / 2);
                int address = _entriesAddress + (middle * EntryLength);
                int comparison = CompareKeyAt(address, encodedWord);
                if (comparison == 0)
                {
                    return checked((ushort)address);
                }

                if (comparison < 0)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle - 1;
                }
            }

            return null;
        }

        for (int index = 0; index < EntryCount; index++)
        {
            int address = _entriesAddress + (index * EntryLength);
            if (CompareKeyAt(address, encodedWord) == 0)
            {
                return checked((ushort)address);
            }
        }

        return null;
    }

    /// <summary>Encodes text using Version 3 dictionary rules into six Z-characters.</summary>
    public static byte[] EncodeWord(string word)
    {
        ArgumentNullException.ThrowIfNull(word);
        List<byte> zCharacters = [];
        Span<byte> construction = stackalloc byte[4];

        foreach (char originalCharacter in word)
        {
            char character = char.ToLowerInvariant(originalCharacter);
            int constructionLength = EncodeCharacter(character, construction);
            int remaining = EncodedZCharacterCount - zCharacters.Count;
            int appendCount = Math.Min(remaining, constructionLength);
            for (int index = 0; index < appendCount; index++)
            {
                zCharacters.Add(construction[index]);
            }

            if (zCharacters.Count == EncodedZCharacterCount)
            {
                break;
            }
        }

        while (zCharacters.Count < EncodedZCharacterCount)
        {
            zCharacters.Add(5);
        }

        byte[] encoded = new byte[EncodedWordLength];
        for (int wordIndex = 0; wordIndex < EncodedZCharacterCount / 3; wordIndex++)
        {
            ushort packed = (ushort)((zCharacters[wordIndex * 3] << 10)
                | (zCharacters[(wordIndex * 3) + 1] << 5)
                | zCharacters[(wordIndex * 3) + 2]);
            if (wordIndex == (EncodedZCharacterCount / 3) - 1)
            {
                packed |= 0x8000;
            }

            BinaryPrimitives.WriteUInt16BigEndian(encoded.AsSpan(wordIndex * sizeof(ushort)), packed);
        }

        return encoded;
    }

    private static int EncodeCharacter(char character, Span<byte> zCharacters)
    {
        if (character == '\n')
        {
            zCharacters[0] = 5;
            zCharacters[1] = 7;
            return 2;
        }

        if (character is >= 'a' and <= 'z')
        {
            zCharacters[0] = (byte)(character - 'a' + 6);
            return 1;
        }

        int alphabetPosition = AlphabetA2.IndexOf(character);
        if (alphabetPosition > 0)
        {
            zCharacters[0] = 5;
            zCharacters[1] = (byte)(alphabetPosition + 6);
            return 2;
        }

        int zscii = FindZscii(character);
        zCharacters[0] = 5;
        zCharacters[1] = 6;
        zCharacters[2] = (byte)((zscii >> 5) & 0x1F);
        zCharacters[3] = (byte)(zscii & 0x1F);
        return 4;
    }

    private static int FindZscii(char character)
    {
        if (character == '\n')
        {
            return 13;
        }

        if (character is >= ' ' and <= '~')
        {
            return character;
        }

        for (int code = 155; code <= 223; code++)
        {
            if (Z3TextDecoder.ToUnicodeCharacter(code) == character)
            {
                return code;
            }
        }

        throw new InvalidDataException($"Character U+{(int)character:X4} has no defined Version 3 ZSCII representation.");
    }

    private void ValidateEntryOrdering()
    {
        int previousAddress = _entriesAddress;
        for (int index = 1; index < EntryCount; index++)
        {
            int currentAddress = _entriesAddress + (index * EntryLength);
            if (CompareEntries(previousAddress, currentAddress) >= 0)
            {
                throw new InvalidDataException("A sorted Version 3 dictionary must contain unique entries in encoded-text order.");
            }

            previousAddress = currentAddress;
        }
    }

    private int CompareEntries(int leftAddress, int rightAddress)
    {
        for (int index = 0; index < EncodedWordLength; index++)
        {
            int comparison = _memory.ReadByte(leftAddress + index).CompareTo(_memory.ReadByte(rightAddress + index));
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return 0;
    }

    private int CompareKeyAt(int address, ReadOnlySpan<byte> key)
    {
        for (int index = 0; index < EncodedWordLength; index++)
        {
            int comparison = _memory.ReadByte(address + index).CompareTo(key[index]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return 0;
    }

    private void ValidateRange(int address, int length, string name)
    {
        if (address < 0 || length < 0 || address > _memory.Length - length)
        {
            throw new InvalidDataException($"The Version 3 {name} extends beyond story memory.");
        }
    }
}
