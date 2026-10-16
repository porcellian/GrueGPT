using System.Buffers.Binary;
using System.Text;

namespace GrueGPT.Core.Resources;

/// <summary>Reads Blorb IFRS FORM files and resolves their resource index.</summary>
public static class BlorbFileCodec
{
    private const int FormHeaderSize = 12;
    private const int ChunkHeaderSize = 8;

    /// <summary>Parses a Blorb file, validating its IFRS framing, RIdx chunk, and indexed offsets.</summary>
    /// <exception cref="InvalidDataException">The input is truncated or violates Blorb framing or index rules.</exception>
    public static BlorbFile Read(ReadOnlySpan<byte> fileData)
    {
        if (fileData.Length < FormHeaderSize)
        {
            throw new InvalidDataException("The Blorb file is shorter than the FORM header.");
        }

        if (!fileData[..4].SequenceEqual("FORM"u8))
        {
            throw new InvalidDataException("The Blorb file does not begin with a FORM chunk.");
        }

        uint formLength = BinaryPrimitives.ReadUInt32BigEndian(fileData.Slice(4, sizeof(uint)));
        if (formLength != fileData.Length - sizeof(uint) * 2)
        {
            throw new InvalidDataException("The Blorb FORM length does not match the file length.");
        }

        if (!fileData.Slice(8, 4).SequenceEqual("IFRS"u8))
        {
            throw new InvalidDataException("The FORM type must be IFRS.");
        }

        List<BlorbChunk> chunks = [];
        Dictionary<uint, BlorbChunk> chunksByOffset = [];
        int offset = FormHeaderSize;
        while (offset < fileData.Length)
        {
            if (fileData.Length - offset < ChunkHeaderSize)
            {
                throw new InvalidDataException("The IFRS FORM ends with an incomplete chunk header.");
            }

            string chunkId = ReadIdentifier(fileData.Slice(offset, 4), "chunk ID");
            uint chunkLength = BinaryPrimitives.ReadUInt32BigEndian(fileData.Slice(offset + 4, sizeof(uint)));
            long dataStart = (long)offset + ChunkHeaderSize;
            long dataEnd = dataStart + chunkLength;
            long paddedEnd = dataEnd + (chunkLength & 1);
            if (paddedEnd > fileData.Length)
            {
                throw new InvalidDataException($"Chunk '{chunkId}' extends beyond the end of the IFRS FORM.");
            }

            if ((chunkLength & 1) != 0 && fileData[(int)dataEnd] != 0)
            {
                throw new InvalidDataException($"Odd-length chunk '{chunkId}' has a nonzero pad byte.");
            }

            int dataLength = checked((int)chunkLength);
            BlorbChunk chunk = new(chunkId, checked((uint)offset), fileData.Slice((int)dataStart, dataLength));
            chunks.Add(chunk);
            chunksByOffset.Add(chunk.Offset, chunk);
            offset = checked((int)paddedEnd);
        }

        if (chunks.Count == 0 || !string.Equals(chunks[0].Id, "RIdx", StringComparison.Ordinal))
        {
            throw new InvalidDataException("The first IFRS chunk must be the resource index RIdx.");
        }

        if (chunks.Skip(1).Any(static chunk => string.Equals(chunk.Id, "RIdx", StringComparison.Ordinal)))
        {
            throw new InvalidDataException("An IFRS FORM must contain exactly one RIdx chunk.");
        }

        IReadOnlyList<BlorbResource> resources = ParseResourceIndex(chunks[0], chunksByOffset);
        if (resources.Count(static resource => string.Equals(resource.Usage, "Exec", StringComparison.Ordinal)) > 1)
        {
            throw new InvalidDataException("A Blorb file may contain at most one Exec resource.");
        }

        return new BlorbFile(chunks, resources);
    }

    private static IReadOnlyList<BlorbResource> ParseResourceIndex(
        BlorbChunk indexChunk,
        IReadOnlyDictionary<uint, BlorbChunk> chunksByOffset)
    {
        ReadOnlySpan<byte> data = indexChunk.Data.Span;
        if (data.Length < sizeof(uint))
        {
            throw new InvalidDataException("The RIdx chunk is missing its resource count.");
        }

        uint count = BinaryPrimitives.ReadUInt32BigEndian(data[..sizeof(uint)]);
        ulong requiredLength = sizeof(uint) + ((ulong)count * 12);
        if (requiredLength != (ulong)data.Length)
        {
            throw new InvalidDataException("The RIdx chunk length does not match its resource count.");
        }

        List<BlorbResource> resources = new(checked((int)count));
        HashSet<(string Usage, uint Number)> resourceKeys = [];
        int offset = sizeof(uint);
        for (uint index = 0; index < count; index++)
        {
            string usage = ReadIdentifier(data.Slice(offset, 4), "resource usage");
            uint number = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset + 4, sizeof(uint)));
            uint start = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset + 8, sizeof(uint)));
            if (!resourceKeys.Add((usage, number)))
            {
                throw new InvalidDataException($"The RIdx chunk contains duplicate '{usage}' resource number {number}.");
            }

            if (!chunksByOffset.TryGetValue(start, out BlorbChunk? chunk)
                || ReferenceEquals(chunk, indexChunk))
            {
                throw new InvalidDataException(
                    $"RIdx resource '{usage}' number {number} does not point to a resource chunk header.");
            }

            if (string.Equals(usage, "Exec", StringComparison.Ordinal) && number != 0)
            {
                throw new InvalidDataException("An Exec resource must have resource number zero.");
            }

            resources.Add(new BlorbResource(usage, number, start, chunk));
            offset += 12;
        }

        return resources.AsReadOnly();
    }

    private static string ReadIdentifier(ReadOnlySpan<byte> bytes, string description)
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
