namespace GrueGPT.Core.SaveFiles;

/// <summary>Creates and restores Quetzal CMem and UMem dynamic-memory chunks.</summary>
public static class QuetzalMemoryCodec
{
    /// <summary>Creates a CMem chunk by XORing current memory with the original and run-length encoding zeroes.</summary>
    public static QuetzalChunk CreateCMemChunk(ReadOnlySpan<byte> originalMemory, ReadOnlySpan<byte> currentMemory)
    {
        if (originalMemory.Length != currentMemory.Length)
        {
            throw new ArgumentException("Original and current dynamic memory must have the same length.", nameof(currentMemory));
        }

        List<byte> compressed = [];
        int offset = 0;
        while (offset < originalMemory.Length)
        {
            byte difference = (byte)(originalMemory[offset] ^ currentMemory[offset]);
            if (difference != 0)
            {
                compressed.Add(difference);
                offset++;
                continue;
            }

            int runLength = 1;
            while (runLength < 256
                && offset + runLength < originalMemory.Length
                && originalMemory[offset + runLength] == currentMemory[offset + runLength])
            {
                runLength++;
            }

            compressed.Add(0);
            compressed.Add((byte)(runLength - 1));
            offset += runLength;
        }

        return new QuetzalChunk("CMem", compressed.ToArray());
    }

    /// <summary>Restores dynamic memory from a CMem chunk, applying XOR to the original story bytes.</summary>
    public static byte[] ReadCMemChunk(QuetzalChunk chunk, ReadOnlySpan<byte> originalMemory)
    {
        ValidateChunkId(chunk, "CMem");

        ReadOnlySpan<byte> encoded = chunk.Data.Span;
        byte[] restored = new byte[originalMemory.Length];
        int inputOffset = 0;
        int outputOffset = 0;

        while (inputOffset < encoded.Length)
        {
            byte value = encoded[inputOffset++];
            if (value == 0)
            {
                if (inputOffset >= encoded.Length)
                {
                    throw new InvalidDataException("The CMem chunk ends with an incomplete zero run.");
                }

                int runLength = encoded[inputOffset++] + 1;
                if (runLength > originalMemory.Length - outputOffset)
                {
                    throw new InvalidDataException("The decoded CMem data exceeds dynamic memory.");
                }

                originalMemory.Slice(outputOffset, runLength).CopyTo(restored.AsSpan(outputOffset));
                outputOffset += runLength;
            }
            else
            {
                if (outputOffset >= originalMemory.Length)
                {
                    throw new InvalidDataException("The decoded CMem data exceeds dynamic memory.");
                }

                restored[outputOffset] = (byte)(originalMemory[outputOffset] ^ value);
                outputOffset++;
            }
        }

        originalMemory[outputOffset..].CopyTo(restored.AsSpan(outputOffset));
        return restored;
    }

    /// <summary>Creates a UMem chunk containing an uncompressed copy of dynamic memory.</summary>
    public static QuetzalChunk CreateUMemChunk(ReadOnlySpan<byte> dynamicMemory) => new("UMem", dynamicMemory);

    /// <summary>Reads an exact-length UMem chunk.</summary>
    public static byte[] ReadUMemChunk(QuetzalChunk chunk, int expectedLength)
    {
        ValidateChunkId(chunk, "UMem");
        ArgumentOutOfRangeException.ThrowIfNegative(expectedLength);

        if (chunk.Data.Length != expectedLength)
        {
            throw new InvalidDataException(
                $"The UMem chunk contains {chunk.Data.Length} bytes; expected exactly {expectedLength}.");
        }

        return chunk.Data.ToArray();
    }

    private static void ValidateChunkId(QuetzalChunk chunk, string expectedId)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        if (!string.Equals(chunk.Id, expectedId, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Expected a {expectedId} chunk, found '{chunk.Id}'.");
        }
    }
}
