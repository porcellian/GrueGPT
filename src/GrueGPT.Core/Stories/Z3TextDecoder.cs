namespace GrueGPT.Core.Stories;

/// <summary>Decodes Version 3 packed text and ZSCII characters.</summary>
public sealed class Z3TextDecoder
{
    private const int ZsciiNewLine = 13;
    private const int AbbreviationsPerGroup = 32;
    private const string AlphabetA2 = " ^0123456789.,!?_#'\"/\\-:()";

    private static readonly char[] DefaultExtraCharacters =
    [
        'ä', 'ö', 'ü', 'Ä', 'Ö', 'Ü', 'ß', '»', '«', 'ë', 'ï', 'ÿ', 'Ë', 'Ï',
        'á', 'é', 'í', 'ó', 'ú', 'ý', 'Á', 'É', 'Í', 'Ó', 'Ú', 'Ý', 'à', 'è',
        'ì', 'ò', 'ù', 'À', 'È', 'Ì', 'Ò', 'Ù', 'â', 'ê', 'î', 'ô', 'û', 'Â',
        'Ê', 'Î', 'Ô', 'Û', 'å', 'Å', 'ø', 'Ø', 'ã', 'ñ', 'õ', 'Ã', 'Ñ', 'Õ',
        'æ', 'Æ', 'ç', 'Ç', 'þ', 'ð', 'Þ', 'Ð', '£', 'œ', 'Œ', '¡', '¿'
    ];

    private readonly Z3Memory _memory;
    private readonly int _abbreviationsAddress;

    public Z3TextDecoder(Z3Memory memory, ushort abbreviationsAddress)
    {
        ArgumentNullException.ThrowIfNull(memory);
        _memory = memory;
        _abbreviationsAddress = abbreviationsAddress;
    }

    /// <summary>Decodes a string stored at a byte address.</summary>
    public string DecodeAt(int byteAddress) => DecodeAt(byteAddress, allowAbbreviations: true);

    /// <summary>Decodes a string whose address is stored in Version 3 packed form.</summary>
    public string DecodePackedAddress(ushort packedAddress) =>
        DecodeAt(Z3Memory.UnpackAddress(packedAddress));

    /// <summary>Maps one output ZSCII value to its Unicode character; zero has no output.</summary>
    public static char? ToUnicodeCharacter(int zscii)
    {
        if (zscii == 0)
        {
            return null;
        }

        if (zscii == ZsciiNewLine)
        {
            return '\n';
        }

        if (zscii is >= 32 and <= 126)
        {
            return (char)zscii;
        }

        if (zscii is >= 155 and <= 223)
        {
            return DefaultExtraCharacters[zscii - 155];
        }

        // Undefined and input-only codes have no printable v3 representation.
        return '?';
    }

    /// <summary>Maps a Unicode character to Version 3 ZSCII, replacing unsupported characters with '?'.</summary>
    public static byte ToZsciiCharacter(char character)
    {
        if (character is '\r' or '\n')
        {
            return 13;
        }

        if (character is >= (char)32 and <= (char)126)
        {
            return (byte)character;
        }

        int extraCharacterIndex = Array.IndexOf(DefaultExtraCharacters, character);
        return extraCharacterIndex >= 0 ? (byte)(155 + extraCharacterIndex) : (byte)'?';
    }

    private string DecodeAt(int byteAddress, bool allowAbbreviations)
    {
        System.Text.StringBuilder output = new();
        using IEnumerator<byte> characters = ReadZCharacters(byteAddress).GetEnumerator();
        int? shiftedAlphabet = null;

        while (characters.MoveNext())
        {
            byte zchar = characters.Current;
            if (zchar == 0)
            {
                output.Append(' ');
                shiftedAlphabet = null;
                continue;
            }

            if (zchar is >= 1 and <= 3)
            {
                shiftedAlphabet = null;
                if (!allowAbbreviations)
                {
                    throw new InvalidDataException("A Version 3 abbreviation string cannot contain another abbreviation.");
                }

                if (!characters.MoveNext())
                {
                    break;
                }

                int abbreviationIndex = (zchar - 1) * AbbreviationsPerGroup + characters.Current;
                int tableEntryAddress = _abbreviationsAddress + abbreviationIndex * sizeof(ushort);
                ushort packedAddress = _memory.ReadWord(tableEntryAddress);
                output.Append(DecodeAt(Z3Memory.UnpackAddress(packedAddress), allowAbbreviations: false));
                continue;
            }

            if (zchar is 4 or 5)
            {
                shiftedAlphabet = zchar - 3;
                continue;
            }

            int alphabet = shiftedAlphabet ?? 0;
            shiftedAlphabet = null;

            if (alphabet == 2 && zchar == 6)
            {
                if (!characters.MoveNext())
                {
                    break;
                }

                int highBits = characters.Current;
                if (!characters.MoveNext())
                {
                    break;
                }

                int zscii = (highBits << 5) | characters.Current;
                AppendZscii(output, zscii);
                continue;
            }

            if (alphabet == 2 && zchar == 7)
            {
                output.Append('\n');
                continue;
            }

            int zsciiCharacter = alphabet switch
            {
                0 => 'a' + zchar - 6,
                1 => 'A' + zchar - 6,
                2 => AlphabetA2[zchar - 6],
                _ => throw new InvalidOperationException("Unexpected Version 3 alphabet.")
            };
            AppendZscii(output, zsciiCharacter);
        }

        return output.ToString();
    }

    private IEnumerable<byte> ReadZCharacters(int byteAddress)
    {
        int address = byteAddress;
        bool isLastWord;
        do
        {
            ushort word = _memory.ReadWord(address);
            address += sizeof(ushort);
            isLastWord = (word & 0x8000) != 0;
            yield return (byte)((word >> 10) & 0x1F);
            yield return (byte)((word >> 5) & 0x1F);
            yield return (byte)(word & 0x1F);
        }
        while (!isLastWord);
    }

    private static void AppendZscii(System.Text.StringBuilder output, int zscii)
    {
        char? character = ToUnicodeCharacter(zscii);
        if (character.HasValue)
        {
            output.Append(character.Value);
        }
    }
}
