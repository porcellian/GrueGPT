using System.Buffers.Binary;
using System.Text;

namespace GrueGPT.Core.SaveFiles;

/// <summary>Reads and writes the IFF FORM/IFZS container used by Quetzal.</summary>
public static class QuetzalFileCodec
{
    private const int FormHeaderSize = 12;
    private const int ChunkHeaderSize = 8;
    private const string FormId = "FORM";
    private const string FormType = "IFZS";

    /// <summary>Parses an IFZS file, preserving chunk order, duplicates, and unknown chunks.</summary>
    /// <exception cref="InvalidDataException">The input is truncated or has invalid IFF framing.</exception>
    public static QuetzalFile Read(ReadOnlySpan<byte> fileData)
    {
        if (fileData.Length < FormHeaderSize)
        {
            throw new InvalidDataException("The Quetzal file is shorter than the FORM header.");
        }

        if (!fileData[..4].SequenceEqual(Encoding.ASCII.GetBytes(FormId)))
        {
            throw new InvalidDataException("The Quetzal file does not begin with a FORM chunk.");
        }

        uint formLength = BinaryPrimitives.ReadUInt32BigEndian(fileData.Slice(4, sizeof(uint)));
        if (formLength != fileData.Length - sizeof(uint) * 2)
        {
            throw new InvalidDataException("The FORM length does not match the file length.");
        }

        string formType = ReadFourCharacterId(fileData.Slice(8, 4), "FORM type");
        if (!IffIdentifier.IsValidFormType(formType))
        {
            throw new InvalidDataException("The FORM type must use uppercase letters with only trailing spaces.");
        }

        if (!string.Equals(formType, FormType, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"The FORM type '{formType}' is not IFZS.");
        }

        List<QuetzalChunk> chunks = [];
        int offset = FormHeaderSize;
        while (offset < fileData.Length)
        {
            if (fileData.Length - offset < ChunkHeaderSize)
            {
                throw new InvalidDataException("The IFZS FORM ends with an incomplete chunk header.");
            }

            string chunkId = ReadFourCharacterId(fileData.Slice(offset, 4), "chunk ID");
            uint chunkLength = BinaryPrimitives.ReadUInt32BigEndian(fileData.Slice(offset + 4, sizeof(uint)));
            long dataStart = (long)offset + ChunkHeaderSize;
            long dataEnd = dataStart + chunkLength;
            if (dataEnd > fileData.Length)
            {
                throw new InvalidDataException($"Chunk '{chunkId}' extends beyond the end of the IFZS FORM.");
            }

            int dataLength = checked((int)chunkLength);
            chunks.Add(new QuetzalChunk(chunkId, fileData.Slice((int)dataStart, dataLength)));
            offset = (int)dataEnd;

            if ((chunkLength & 1) != 0)
            {
                if (offset >= fileData.Length)
                {
                    throw new InvalidDataException($"Odd-length chunk '{chunkId}' is missing its pad byte.");
                }

                if (fileData[offset] != 0)
                {
                    throw new InvalidDataException($"Odd-length chunk '{chunkId}' has a nonzero pad byte.");
                }

                offset++;
            }
        }

        return new QuetzalFile(chunks);
    }

    /// <summary>Writes an IFZS FORM with big-endian lengths and zero padding for odd chunks.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The encoded FORM would exceed supported sizes.</exception>
    public static byte[] Write(QuetzalFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        long formLength = sizeof(uint);
        foreach (QuetzalChunk chunk in file.Chunks)
        {
            formLength += ChunkHeaderSize + chunk.Data.Length + (chunk.Data.Length & 1);
            if (formLength > uint.MaxValue || formLength + sizeof(uint) * 2 > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(file), "The IFZS FORM is too large to encode.");
            }
        }

        byte[] encoded = new byte[checked((int)formLength + sizeof(uint) * 2)];
        Encoding.ASCII.GetBytes(FormId, encoded.AsSpan(0, 4));
        BinaryPrimitives.WriteUInt32BigEndian(encoded.AsSpan(4, sizeof(uint)), (uint)formLength);
        Encoding.ASCII.GetBytes(FormType, encoded.AsSpan(8, 4));

        int offset = FormHeaderSize;
        foreach (QuetzalChunk chunk in file.Chunks)
        {
            IffIdentifier.ValidateChunkId(chunk.Id, nameof(file));
            Encoding.ASCII.GetBytes(chunk.Id, encoded.AsSpan(offset, 4));
            BinaryPrimitives.WriteUInt32BigEndian(encoded.AsSpan(offset + 4, sizeof(uint)), (uint)chunk.Data.Length);
            offset += ChunkHeaderSize;
            chunk.Data.Span.CopyTo(encoded.AsSpan(offset, chunk.Data.Length));
            offset += chunk.Data.Length;
            if ((chunk.Data.Length & 1) != 0)
            {
                encoded[offset++] = 0;
            }
        }

        return encoded;
    }

    private static string ReadFourCharacterId(ReadOnlySpan<byte> bytes, string description)
    {
        bool foundSpace = false;
        foreach (byte value in bytes)
        {
            if (value < 0x20 || value > 0x7E || (foundSpace && value != ' '))
            {
                throw new InvalidDataException($"The {description} is not a valid four-character IFF identifier.");
            }

            foundSpace |= value == ' ';
        }

        return Encoding.ASCII.GetString(bytes);
    }
}
