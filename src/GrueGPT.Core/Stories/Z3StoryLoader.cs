using System.Buffers.Binary;
using System.Text;

namespace GrueGPT.Core.Stories;

/// <summary>Loads and validates raw Z-machine version 3 story files.</summary>
public sealed class Z3StoryLoader
{
    public const int HeaderSize = 64;
    public const int MaximumFileSize = 128 * 1024;

    private const int DictionaryHeaderSize = 4;
    private const int ObjectDefaultsSize = 31 * sizeof(ushort);
    private const int GlobalVariablesSize = 240 * sizeof(ushort);
    private const int AbbreviationsTableSize = 96 * sizeof(ushort);

    /// <summary>
    /// Reads a story from the stream's current position without closing the stream.
    /// </summary>
    public LoadedStory Load(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead)
        {
            throw new ArgumentException("The story stream must be readable.", nameof(source));
        }

        byte[] physicalImage = ReadStoryBytes(source);
        if (physicalImage.Length < HeaderSize)
        {
            throw new StoryLoadException(
                StoryLoadErrorCode.FileTooSmall,
                $"The story file is {physicalImage.Length} bytes; a Z-machine header requires {HeaderSize} bytes.");
        }

        byte version = physicalImage[0];
        if (version != 3)
        {
            throw new StoryLoadException(
                StoryLoadErrorCode.UnsupportedVersion,
                $"Story version {version} is not supported; this loader accepts version 3 only.");
        }

        StoryHeader header = ReadHeader(physicalImage);
        ValidateHeader(header, physicalImage.Length);

        if (header.HasChecksum)
        {
            ushort actualChecksum = CalculateChecksum(physicalImage.AsSpan(0, header.FileLength));
            if (actualChecksum != header.Checksum)
            {
                throw new StoryLoadException(
                    StoryLoadErrorCode.ChecksumMismatch,
                    $"Story checksum mismatch: header contains 0x{header.Checksum:X4}, but the file contains 0x{actualChecksum:X4}.");
            }
        }

        byte[] logicalImage = physicalImage.AsSpan(0, header.FileLength).ToArray();
        return new LoadedStory(logicalImage, header);
    }

    private static byte[] ReadStoryBytes(Stream source)
    {
        using MemoryStream buffer = new();
        byte[] chunk = new byte[8192];

        while (true)
        {
            int remainingWithOverflowByte = MaximumFileSize + 1 - (int)buffer.Length;
            if (remainingWithOverflowByte <= 0)
            {
                throw new StoryLoadException(
                    StoryLoadErrorCode.FileTooLarge,
                    $"The story file exceeds the Z-machine v3 limit of {MaximumFileSize} bytes.");
            }

            int read = source.Read(chunk, 0, Math.Min(chunk.Length, remainingWithOverflowByte));
            if (read == 0)
            {
                break;
            }

            buffer.Write(chunk, 0, read);
            if (buffer.Length > MaximumFileSize)
            {
                throw new StoryLoadException(
                    StoryLoadErrorCode.FileTooLarge,
                    $"The story file exceeds the Z-machine v3 limit of {MaximumFileSize} bytes.");
            }
        }

        return buffer.ToArray();
    }

    private static StoryHeader ReadHeader(ReadOnlySpan<byte> image)
    {
        ushort encodedLength = ReadWord(image, 0x1A);
        ushort checksum = ReadWord(image, 0x1C);
        bool hasChecksum = encodedLength != 0 || checksum != 0;
        int fileLength = encodedLength == 0 ? image.Length : encodedLength * 2;

        return new StoryHeader(
            Version: image[0],
            Flags1: image[1],
            ReleaseNumber: ReadWord(image, 0x02),
            HighMemoryBase: ReadWord(image, 0x04),
            InitialProgramCounter: ReadWord(image, 0x06),
            DictionaryAddress: ReadWord(image, 0x08),
            ObjectTableAddress: ReadWord(image, 0x0A),
            GlobalVariablesAddress: ReadWord(image, 0x0C),
            StaticMemoryBase: ReadWord(image, 0x0E),
            Flags2: ReadWord(image, 0x10),
            AbbreviationsAddress: ReadWord(image, 0x18),
            Checksum: checksum,
            HasChecksum: hasChecksum,
            FileLength: fileLength,
            SerialNumber: Encoding.ASCII.GetString(image.Slice(0x12, 6)));
    }

    private static void ValidateHeader(StoryHeader header, int physicalLength)
    {
        if (header.FileLength < HeaderSize)
        {
            throw InvalidHeader($"Declared file length {header.FileLength} is smaller than the {HeaderSize}-byte header.");
        }

        if (header.FileLength > MaximumFileSize)
        {
            throw InvalidHeader($"Declared file length {header.FileLength} exceeds the {MaximumFileSize}-byte v3 limit.");
        }

        if (header.FileLength > physicalLength)
        {
            throw new StoryLoadException(
                StoryLoadErrorCode.TruncatedFile,
                $"The header declares {header.FileLength} story bytes, but the stream contains only {physicalLength}.");
        }

        if (header.StaticMemoryBase < HeaderSize || header.StaticMemoryBase >= header.FileLength)
        {
            throw InvalidHeader(
                $"Static memory base 0x{header.StaticMemoryBase:X4} must be at or after the header and before the end of the story.");
        }

        if (header.HighMemoryBase < header.StaticMemoryBase || header.HighMemoryBase >= header.FileLength)
        {
            throw InvalidHeader(
                $"High memory base 0x{header.HighMemoryBase:X4} must not overlap dynamic memory and must be inside the story.");
        }

        if (header.InitialProgramCounter < header.HighMemoryBase || header.InitialProgramCounter >= header.FileLength)
        {
            throw InvalidHeader(
                $"Initial program counter 0x{header.InitialProgramCounter:X4} is outside high memory or beyond the story file.");
        }

        ValidateFileRange("dictionary", header.DictionaryAddress, DictionaryHeaderSize, header.FileLength);
        ValidateFileRange("object table", header.ObjectTableAddress, ObjectDefaultsSize, header.FileLength);
        ValidateDynamicRange("global variables", header.GlobalVariablesAddress, GlobalVariablesSize, header.StaticMemoryBase);
        ValidateFileRange("abbreviations table", header.AbbreviationsAddress, AbbreviationsTableSize, header.FileLength);
    }

    private static void ValidateFileRange(string name, ushort address, int length, int fileLength)
    {
        int endAddress = address + length;
        if (address < HeaderSize || endAddress > fileLength)
        {
            throw InvalidHeader(
                $"The {name} range at 0x{address:X4} with length {length} must fit inside the {fileLength}-byte story image.");
        }
    }

    private static void ValidateDynamicRange(string name, ushort address, int length, ushort staticMemoryBase)
    {
        int endAddress = address + length;
        if (address < HeaderSize || endAddress > staticMemoryBase)
        {
            throw InvalidHeader(
                $"The {name} range at 0x{address:X4} with length {length} must fit in dynamic memory below 0x{staticMemoryBase:X4}.");
        }
    }

    private static ushort CalculateChecksum(ReadOnlySpan<byte> image)
    {
        uint sum = 0;
        foreach (byte value in image[HeaderSize..])
        {
            sum += value;
        }

        return unchecked((ushort)sum);
    }

    private static ushort ReadWord(ReadOnlySpan<byte> image, int address) =>
        BinaryPrimitives.ReadUInt16BigEndian(image.Slice(address, sizeof(ushort)));

    private static StoryLoadException InvalidHeader(string message) =>
        new(StoryLoadErrorCode.InvalidHeader, message);
}
