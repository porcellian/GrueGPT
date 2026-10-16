using System.Buffers.Binary;

namespace GrueGPT.Core.Resources;

/// <summary>Converts uncompressed integer PCM AIFF sound resources to little-endian PCM WAV.</summary>
public static class BlorbAiffCodec
{
    /// <summary>Decodes an indexed AIFF sound resource into 16-bit PCM WAV bytes.</summary>
    public static byte[] DecodeToWave(BlorbResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (!string.Equals(resource.Usage, "Snd ", StringComparison.Ordinal)
            || !string.Equals(resource.Chunk.Id, "FORM", StringComparison.Ordinal))
        {
            throw new InvalidDataException("The Blorb resource is not an AIFF sound.");
        }

        ReadOnlySpan<byte> data = resource.Chunk.Data.Span;
        if (data.Length < 12 || !data[..4].SequenceEqual("FORM"u8)
            || !data.Slice(8, 4).SequenceEqual("AIFF"u8)
            || ReadUInt32(data, 4) != data.Length - 8)
        {
            throw new InvalidDataException("The AIFF FORM header is invalid or incomplete.");
        }

        ushort channels = 0;
        ushort sampleBits = 0;
        uint frameCount = 0;
        uint sampleRate = 0;
        bool hasCommonChunk = false;
        bool hasSoundChunk = false;
        ReadOnlySpan<byte> samples = default;
        int offset = 12;
        while (offset < data.Length)
        {
            if (offset + 8 > data.Length)
            {
                throw new InvalidDataException("The AIFF contains a truncated chunk header.");
            }

            ReadOnlySpan<byte> id = data.Slice(offset, 4);
            uint chunkLength = ReadUInt32(data, offset + 4);
            int chunkStart = offset + 8;
            if (chunkLength > (uint)(data.Length - chunkStart))
            {
                throw new InvalidDataException("The AIFF contains a chunk outside the FORM bounds.");
            }

            ReadOnlySpan<byte> chunk = data.Slice(chunkStart, (int)chunkLength);
            if (id.SequenceEqual("COMM"u8))
            {
                if (hasCommonChunk || chunk.Length < 18)
                {
                    throw new InvalidDataException("The AIFF COMM chunk is duplicate or incomplete.");
                }

                hasCommonChunk = true;
                channels = BinaryPrimitives.ReadUInt16BigEndian(chunk);
                frameCount = ReadUInt32(chunk, 2);
                sampleBits = BinaryPrimitives.ReadUInt16BigEndian(chunk[6..]);
                sampleRate = ReadExtendedSampleRate(chunk[8..18]);
            }
            else if (id.SequenceEqual("SSND"u8))
            {
                if (hasSoundChunk || chunk.Length < 8)
                {
                    throw new InvalidDataException("The AIFF SSND chunk is duplicate or incomplete.");
                }

                hasSoundChunk = true;
                uint sampleOffset = ReadUInt32(chunk, 0);
                if (sampleOffset > chunk.Length - 8)
                {
                    throw new InvalidDataException("The AIFF SSND sample offset exceeds its chunk.");
                }

                samples = chunk.Slice(8 + (int)sampleOffset);
            }

            offset = checked(chunkStart + (int)chunkLength + ((int)chunkLength & 1));
        }

        if (offset != data.Length || !hasCommonChunk || !hasSoundChunk || channels is < 1 or > 2 || frameCount == 0 || sampleRate is < 1 or > 192000
            || sampleBits is not (8 or 16 or 24 or 32) || samples.IsEmpty)
        {
            throw new InvalidDataException("The AIFF must contain supported mono/stereo integer PCM COMM and SSND chunks.");
        }

        int bytesPerSample = sampleBits / 8;
        int bytesPerFrame = checked(bytesPerSample * channels);
        long requiredSampleBytes = (long)frameCount * bytesPerFrame;
        long pcmLengthValue = (long)frameCount * channels * 2;
        if (requiredSampleBytes > samples.Length)
        {
            throw new InvalidDataException("The AIFF sample data is shorter than its declared frame count.");
        }

        if (pcmLengthValue > int.MaxValue - 44)
        {
            throw new InvalidDataException("The decoded AIFF sample exceeds the supported WAV size.");
        }

        int pcmLength = (int)pcmLengthValue;

        int byteRate = checked((int)sampleRate * channels * 2);
        byte[] wave = new byte[checked(44 + pcmLength)];
        "RIFF"u8.CopyTo(wave);
        BinaryPrimitives.WriteUInt32LittleEndian(wave.AsSpan(4), checked((uint)(wave.Length - 8)));
        "WAVEfmt "u8.CopyTo(wave.AsSpan(8));
        BinaryPrimitives.WriteUInt32LittleEndian(wave.AsSpan(16), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(wave.AsSpan(20), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(wave.AsSpan(22), channels);
        BinaryPrimitives.WriteUInt32LittleEndian(wave.AsSpan(24), sampleRate);
        BinaryPrimitives.WriteUInt32LittleEndian(wave.AsSpan(28), checked((uint)byteRate));
        BinaryPrimitives.WriteUInt16LittleEndian(wave.AsSpan(32), checked((ushort)(channels * 2)));
        BinaryPrimitives.WriteUInt16LittleEndian(wave.AsSpan(34), 16);
        "data"u8.CopyTo(wave.AsSpan(36));
        BinaryPrimitives.WriteUInt32LittleEndian(wave.AsSpan(40), checked((uint)pcmLength));

        Span<byte> output = wave.AsSpan(44);
        for (int index = 0; index < frameCount * channels; index++)
        {
            ReadOnlySpan<byte> input = samples.Slice(index * bytesPerSample, bytesPerSample);
            short value = sampleBits switch
            {
                8 => unchecked((short)((sbyte)input[0] << 8)),
                16 => BinaryPrimitives.ReadInt16BigEndian(input),
                24 => (short)(unchecked((int)((uint)input[0] << 24 | (uint)input[1] << 16 | (uint)input[2] << 8)) >> 16),
                32 => (short)(BinaryPrimitives.ReadInt32BigEndian(input) >> 16),
                _ => throw new InvalidOperationException(),
            };

            BinaryPrimitives.WriteInt16LittleEndian(output[(index * 2)..], value);
        }

        return wave;
    }

    private static uint ReadExtendedSampleRate(ReadOnlySpan<byte> data)
    {
        ushort exponentWord = BinaryPrimitives.ReadUInt16BigEndian(data);
        ulong mantissa = BinaryPrimitives.ReadUInt64BigEndian(data[2..]);
        if ((exponentWord & 0x8000) != 0 || (exponentWord & 0x7FFF) == 0 || mantissa == 0)
        {
            throw new InvalidDataException("The AIFF sample rate is invalid.");
        }

        int exponent = (exponentWord & 0x7FFF) - 16383;
        double value = Math.ScaleB((double)mantissa, exponent - 63);
        if (!double.IsFinite(value) || value < 1 || value > 192000)
        {
            throw new InvalidDataException("The AIFF sample rate is outside the supported range.");
        }

        return checked((uint)Math.Round(value));
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
}
