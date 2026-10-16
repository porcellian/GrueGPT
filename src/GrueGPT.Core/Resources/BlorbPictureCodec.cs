namespace GrueGPT.Core.Resources;

/// <summary>Validates supported Blorb picture resources and exposes their encoded image data.</summary>
public static class BlorbPictureCodec
{
    private const ulong MaximumRasterPixels = 67_108_864;
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];

    /// <summary>Reads a PNG, JPEG, or Rect picture resource.</summary>
    public static BlorbPicture Read(BlorbResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (!string.Equals(resource.Usage, "Pict", StringComparison.Ordinal))
        {
            throw new InvalidDataException("The Blorb resource is not a picture.");
        }

        ReadOnlySpan<byte> data = resource.Chunk.Data.Span;
        return resource.Chunk.Id switch
        {
            "PNG " => ReadPng(data),
            "JPEG" => ReadJpeg(data),
            "Rect" => ReadRectangle(data),
            _ => throw new InvalidDataException($"Unsupported Blorb picture format '{resource.Chunk.Id}'."),
        };
    }

    private static BlorbPicture ReadPng(ReadOnlySpan<byte> data)
    {
        if (data.Length < 8 || !data[..8].SequenceEqual(PngSignature))
        {
            throw new InvalidDataException("The PNG picture has an invalid signature.");
        }

        uint width = 0;
        uint height = 0;
        bool hasHeader = false;
        bool hasImageData = false;
        bool hasEnd = false;
        int offset = 8;
        while (offset < data.Length)
        {
            if (offset + 12 > data.Length)
            {
                throw new InvalidDataException("The PNG picture contains a truncated chunk.");
            }

            uint chunkLength = ReadUInt32(data, offset);
            if (chunkLength > (uint)(data.Length - offset - 12))
            {
                throw new InvalidDataException("The PNG picture contains a chunk outside its data bounds.");
            }

            int length = checked((int)chunkLength);
            ReadOnlySpan<byte> type = data.Slice(offset + 4, 4);
            ReadOnlySpan<byte> chunkData = data.Slice(offset + 8, length);
            uint expectedCrc = ReadUInt32(data, offset + 8 + length);
            if (PngCrc32(data.Slice(offset + 4, 4 + length)) != expectedCrc)
            {
                throw new InvalidDataException("The PNG picture contains an invalid chunk checksum.");
            }

            if (type.SequenceEqual("IHDR"u8))
            {
                if (hasHeader || offset != 8 || length != 13)
                {
                    throw new InvalidDataException("The PNG picture has a duplicate or malformed IHDR chunk.");
                }

                width = ReadUInt32(chunkData, 0);
                height = ReadUInt32(chunkData, 4);
                if (width == 0 || height == 0)
                {
                    throw new InvalidDataException("The PNG picture dimensions must be nonzero.");
                }

                ValidateRasterDimensions(width, height);

                hasHeader = true;
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                if (!hasHeader || hasEnd)
                {
                    throw new InvalidDataException("The PNG picture has image data in an invalid position.");
                }

                hasImageData = true;
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                if (!hasHeader || !hasImageData || length != 0 || offset + 12 != data.Length)
                {
                    throw new InvalidDataException("The PNG picture has an invalid IEND chunk.");
                }

                hasEnd = true;
            }

            offset = checked(offset + 12 + length);
        }

        if (!hasHeader || !hasImageData || !hasEnd)
        {
            throw new InvalidDataException("The PNG picture is missing IHDR, IDAT, or IEND data.");
        }

        return new BlorbPicture(BlorbPictureFormat.Png, width, height, data.ToArray());
    }

    private static BlorbPicture ReadJpeg(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8 || data[^2] != 0xFF || data[^1] != 0xD9)
        {
            throw new InvalidDataException("The JPEG picture has invalid start or end markers.");
        }

        int offset = 2;
        while (offset < data.Length - 2)
        {
            if (data[offset++] != 0xFF)
            {
                throw new InvalidDataException("The JPEG picture contains an invalid marker.");
            }

            while (offset < data.Length && data[offset] == 0xFF)
            {
                offset++;
            }

            if (offset >= data.Length)
            {
                break;
            }

            byte marker = data[offset++];
            if (marker is 0xD8 or 0xD9 or 0x01 || marker is >= 0xD0 and <= 0xD7)
            {
                continue;
            }

            if (offset + 2 > data.Length)
            {
                break;
            }

            int segmentLength = (data[offset] << 8) | data[offset + 1];
            if (segmentLength < 2 || offset + segmentLength > data.Length)
            {
                throw new InvalidDataException("The JPEG picture contains an invalid segment length.");
            }

            if (marker is >= 0xC0 and <= 0xC3 or >= 0xC5 and <= 0xC7 or >= 0xC9 and <= 0xCB or >= 0xCD and <= 0xCF)
            {
                if (segmentLength < 8)
                {
                    throw new InvalidDataException("The JPEG frame header is incomplete.");
                }

                uint height = (uint)((data[offset + 3] << 8) | data[offset + 4]);
                uint width = (uint)((data[offset + 5] << 8) | data[offset + 6]);
                if (width == 0 || height == 0)
                {
                    throw new InvalidDataException("The JPEG picture dimensions must be nonzero.");
                }

                ValidateRasterDimensions(width, height);

                return new BlorbPicture(BlorbPictureFormat.Jpeg, width, height, data.ToArray());
            }

            offset += segmentLength;
        }

        throw new InvalidDataException("The JPEG picture has no supported frame header.");
    }

    private static BlorbPicture ReadRectangle(ReadOnlySpan<byte> data)
    {
        if (data.Length != 8)
        {
            throw new InvalidDataException("A Rect picture must contain exactly width and height words.");
        }

        uint width = ReadUInt32(data, 0);
        uint height = ReadUInt32(data, 4);
        if (width == 0 || height == 0)
        {
            throw new InvalidDataException("The Rect picture dimensions must be nonzero.");
        }

        return new BlorbPicture(BlorbPictureFormat.Rectangle, width, height, ReadOnlySpan<byte>.Empty);
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset) =>
        ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) | ((uint)data[offset + 2] << 8) | data[offset + 3];

    private static uint PngCrc32(ReadOnlySpan<byte> data)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in data)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xEDB88320);
            }
        }

        return ~crc;
    }

    private static void ValidateRasterDimensions(uint width, uint height)
    {
        if ((ulong)width * height > MaximumRasterPixels)
        {
            throw new InvalidDataException("The picture dimensions exceed the supported raster size.");
        }
    }
}

/// <summary>The decoded image kind supported by <see cref="BlorbPictureCodec"/>.</summary>
public enum BlorbPictureFormat
{
    Png,
    Jpeg,
    Rectangle,
}

/// <summary>A validated picture resource ready for host image decoding and rendering.</summary>
public sealed class BlorbPicture
{
    private readonly byte[] _encodedData;

    internal BlorbPicture(BlorbPictureFormat format, uint width, uint height, ReadOnlySpan<byte> encodedData)
    {
        Format = format;
        Width = width;
        Height = height;
        _encodedData = encodedData.ToArray();
    }

    public BlorbPictureFormat Format { get; }
    public uint Width { get; }
    public uint Height { get; }
    public ReadOnlyMemory<byte> EncodedData => _encodedData;
}
