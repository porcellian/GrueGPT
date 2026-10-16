using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using GrueGPT.Core.Resources;
using Xunit;

namespace GrueGPT.Tests.Resources;

public sealed class BlorbMediaCodecTests
{
    [Fact]
    public void ReadsPngDimensionsAndPreservesEncodedPixelsForHostDecoder()
    {
        byte[] png = Png(3, 2);
        BlorbPicture picture = BlorbPictureCodec.Read(ReadResource("PNG ", "Pict", png));

        Assert.Equal(BlorbPictureFormat.Png, picture.Format);
        Assert.Equal(3u, picture.Width);
        Assert.Equal(2u, picture.Height);
        Assert.Equal(png, picture.EncodedData.ToArray());
    }

    [Fact]
    public void ReadsJpegFrameDimensions()
    {
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xC0, 0, 11, 8, 0, 2, 0, 3, 1, 1, 0x11, 0, 0xFF, 0xD9];

        BlorbPicture picture = BlorbPictureCodec.Read(ReadResource("JPEG", "Pict", jpeg));

        Assert.Equal(BlorbPictureFormat.Jpeg, picture.Format);
        Assert.Equal(3u, picture.Width);
        Assert.Equal(2u, picture.Height);
        Assert.Equal(jpeg, picture.EncodedData.ToArray());
    }

    [Fact]
    public void ReadsRectDimensionsWithoutCreatingImageBytes()
    {
        byte[] rect = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(rect, 640);
        BinaryPrimitives.WriteUInt32BigEndian(rect.AsSpan(4), 480);

        BlorbPicture picture = BlorbPictureCodec.Read(ReadResource("Rect", "Pict", rect));

        Assert.Equal(BlorbPictureFormat.Rectangle, picture.Format);
        Assert.Equal(640u, picture.Width);
        Assert.Equal(480u, picture.Height);
        Assert.Empty(picture.EncodedData.ToArray());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void RejectsMalformedPicturePayloads(int invalidCase)
    {
        (string chunkId, byte[] payload) = invalidCase switch
        {
            1 => ("PNG ", [1, 2, 3]),
            2 => ("Rect", new byte[7]),
            3 => ("JPEG", [0xFF, 0xD8, 0, 0]),
            _ => ("PNG ", Png(uint.MaxValue, 1)),
        };

        Assert.Throws<InvalidDataException>(() => BlorbPictureCodec.Read(ReadResource(chunkId, "Pict", payload)));
    }

    [Fact]
    public void ConvertsBigEndianAiffPcmToLittleEndianWave()
    {
        byte[] wave = BlorbAiffCodec.DecodeToWave(ReadResource("FORM", "Snd ", Aiff(1, 16, [0x12, 0x34, 0xFE, 0xDC])));

        Assert.Equal("RIFF", Encoding.ASCII.GetString(wave, 0, 4));
        Assert.Equal("WAVE", Encoding.ASCII.GetString(wave, 8, 4));
        Assert.Equal((short)0x1234, BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(44)));
        Assert.Equal(unchecked((short)0xFEDC), BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(46)));
    }

    [Fact]
    public void ConvertsEightBitAiffSamplesToSignedSixteenBitPcm()
    {
        byte[] wave = BlorbAiffCodec.DecodeToWave(ReadResource("FORM", "Snd ", Aiff(1, 8, [0x7F, 0x80])));

        Assert.Equal(short.MaxValue - 255, BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(44)));
        Assert.Equal(short.MinValue, BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(46)));
    }

    [Theory]
    [InlineData(24, "7FFFFF")]
    [InlineData(32, "7FFF0000")]
    public void ConvertsHighResolutionAiffSamplesToSixteenBitPcm(int sampleBits, string sampleHex)
    {
        byte[] sample = Convert.FromHexString(sampleHex);
        byte[] wave = BlorbAiffCodec.DecodeToWave(ReadResource("FORM", "Snd ", Aiff(1, (ushort)sampleBits, sample)));

        Assert.Equal(short.MaxValue, BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(44)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void RejectsMalformedAiffPayloads(int invalidCase)
    {
        byte[] payload = invalidCase == 1
            ? [1, 2, 3]
            : [0x46, 0x4F, 0x52, 0x4D, 0, 0, 0, 4, 0x41, 0x49, 0x46, 0x46];
        Assert.Throws<InvalidDataException>(() => BlorbAiffCodec.DecodeToWave(ReadResource("FORM", "Snd ", payload)));
    }

    [Fact]
    public void RejectsResourcesWithWrongUsageOrFormat()
    {
        BlorbResource picture = ReadResource("PNG ", "Pict", Png(1, 1));
        BlorbResource ogg = ReadResource("OGGV", "Snd ", [0x4F, 0x67, 0x67, 0x53]);

        Assert.Throws<InvalidDataException>(() => BlorbAiffCodec.DecodeToWave(picture));
        Assert.Throws<InvalidDataException>(() => BlorbAiffCodec.DecodeToWave(ogg));
        Assert.Throws<InvalidDataException>(() => BlorbPictureCodec.Read(ogg));
    }

    private static BlorbResource ReadResource(string chunkId, string usage, byte[] payload)
    {
        byte[] encoded = BuildBlorb(chunkId, usage, payload);
        return Assert.Single(BlorbFileCodec.Read(encoded).Resources);
    }

    private static byte[] Png(uint width, uint height)
    {
        using MemoryStream png = new();
        png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        byte[] header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;
        header[9] = 6;
        WritePngChunk(png, "IHDR", header);

        byte[] compressedPixels;
        if ((ulong)width * height > 67_108_864)
        {
            compressedPixels = [];
        }
        else
        {
            int rowSize = checked((int)width * 4 + 1);
            byte[] pixels = new byte[checked(rowSize * (int)height)];
            using MemoryStream compressed = new();
            using (ZLibStream compressor = new(compressed, CompressionLevel.Optimal, leaveOpen: true))
            {
                compressor.Write(pixels);
            }

            compressedPixels = compressed.ToArray();
        }

        WritePngChunk(png, "IDAT", compressedPixels);
        WritePngChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void WritePngChunk(Stream stream, string id, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, checked((uint)data.Length));
        stream.Write(length);
        byte[] idBytes = Encoding.ASCII.GetBytes(id);
        stream.Write(idBytes);
        stream.Write(data);
        byte[] crcData = new byte[idBytes.Length + data.Length];
        idBytes.CopyTo(crcData, 0);
        data.CopyTo(crcData, idBytes.Length);
        BinaryPrimitives.WriteUInt32BigEndian(length, PngCrc32(crcData));
        stream.Write(length);
    }

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

    internal static byte[] Aiff(ushort channels, ushort sampleBits, byte[] sampleData)
    {
        uint frames = checked((uint)(sampleData.Length / (channels * (sampleBits / 8))));
        byte[] aiff = new byte[12 + 8 + 18 + 8 + 8 + sampleData.Length + (sampleData.Length & 1)];
        "FORM"u8.CopyTo(aiff);
        BinaryPrimitives.WriteUInt32BigEndian(aiff.AsSpan(4), checked((uint)(aiff.Length - 8)));
        "AIFF"u8.CopyTo(aiff.AsSpan(8));
        "COMM"u8.CopyTo(aiff.AsSpan(12));
        BinaryPrimitives.WriteUInt32BigEndian(aiff.AsSpan(16), 18);
        BinaryPrimitives.WriteUInt16BigEndian(aiff.AsSpan(20), channels);
        BinaryPrimitives.WriteUInt32BigEndian(aiff.AsSpan(22), frames);
        BinaryPrimitives.WriteUInt16BigEndian(aiff.AsSpan(26), sampleBits);
        BinaryPrimitives.WriteUInt16BigEndian(aiff.AsSpan(28), 16398);
        BinaryPrimitives.WriteUInt64BigEndian(aiff.AsSpan(30), 0xAC44000000000000);
        int soundHeader = 38;
        "SSND"u8.CopyTo(aiff.AsSpan(soundHeader));
        BinaryPrimitives.WriteUInt32BigEndian(aiff.AsSpan(soundHeader + 4), checked((uint)(8 + sampleData.Length)));
        sampleData.CopyTo(aiff, soundHeader + 16);
        return aiff;
    }

    internal static byte[] BuildBlorb(string chunkId, string usage, byte[] payload, bool repeatSound = false)
    {
        const int resourceChunkOffset = 36;
        using MemoryStream stream = new();
        stream.Write("FORM"u8);
        stream.Write(new byte[4]);
        stream.Write("IFRS"u8);
        stream.Write("RIdx"u8);
        WriteUInt32(stream, 16);
        WriteUInt32(stream, 1);
        stream.Write(Encoding.ASCII.GetBytes(usage));
        WriteUInt32(stream, 1);
        WriteUInt32(stream, resourceChunkOffset);
        stream.Write(Encoding.ASCII.GetBytes(chunkId));
        WriteUInt32(stream, checked((uint)payload.Length));
        stream.Write(payload);
        if ((payload.Length & 1) != 0)
        {
            stream.WriteByte(0);
        }

        if (repeatSound)
        {
            stream.Write("Loop"u8);
            WriteUInt32(stream, 8);
            WriteUInt32(stream, 1);
            WriteUInt32(stream, 0);
        }

        byte[] result = stream.ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(4), checked((uint)(result.Length - 8)));
        return result;
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> data = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(data, value);
        stream.Write(data);
    }
}
