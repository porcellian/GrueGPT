using System.Buffers.Binary;

namespace GrueGPT.Core.SaveFiles;

/// <summary>The story identity and resume program counter stored in Quetzal's IFhd chunk.</summary>
public sealed class QuetzalIfhd
{
    public const int SerialNumberLength = 6;
    public const uint MaximumProgramCounter = 0x00FF_FFFF;

    private readonly byte[] _serialNumber;
    private readonly byte[] _extensionData;

    /// <summary>Creates IFhd data from the story header identity and byte-address PC.</summary>
    public QuetzalIfhd(
        ushort releaseNumber,
        ReadOnlySpan<byte> serialNumber,
        ushort checksum,
        uint programCounter,
        ReadOnlySpan<byte> extensionData = default)
    {
        if (serialNumber.Length != SerialNumberLength)
        {
            throw new ArgumentException("An IFhd serial number must contain exactly six bytes.", nameof(serialNumber));
        }

        if (programCounter > MaximumProgramCounter)
        {
            throw new ArgumentOutOfRangeException(nameof(programCounter), "An IFhd PC must fit in three bytes.");
        }

        ReleaseNumber = releaseNumber;
        _serialNumber = serialNumber.ToArray();
        Checksum = checksum;
        ProgramCounter = programCounter;
        _extensionData = extensionData.ToArray();
    }

    /// <summary>The release number copied from story header offset $02.</summary>
    public ushort ReleaseNumber { get; }

    /// <summary>The six serial-number bytes copied from story header offset $12.</summary>
    public ReadOnlyMemory<byte> SerialNumber => _serialNumber;

    /// <summary>The checksum copied from story header offset $1C.</summary>
    public ushort Checksum { get; }

    /// <summary>The byte-address program counter encoded in three big-endian bytes.</summary>
    public uint ProgramCounter { get; }

    /// <summary>Any bytes beyond the standard 13-byte IFhd fields, retained for forward compatibility.</summary>
    public ReadOnlyMemory<byte> ExtensionData => _extensionData;

    /// <summary>Reads the first 13 bytes of an IFhd chunk.</summary>
    public static QuetzalIfhd Read(QuetzalChunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        if (!string.Equals(chunk.Id, "IFhd", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Expected an IFhd chunk, found '{chunk.Id}'.");
        }

        ReadOnlySpan<byte> data = chunk.Data.Span;
        if (data.Length < 13)
        {
            throw new InvalidDataException("An IFhd chunk must contain at least 13 bytes.");
        }

        uint programCounter = ((uint)data[10] << 16) | ((uint)data[11] << 8) | data[12];
        return new QuetzalIfhd(
            BinaryPrimitives.ReadUInt16BigEndian(data[..2]),
            data.Slice(2, SerialNumberLength),
            BinaryPrimitives.ReadUInt16BigEndian(data.Slice(8, 2)),
            programCounter,
            data[13..]);
    }

    /// <summary>Encodes the standard 13-byte IFhd payload.</summary>
    public QuetzalChunk ToChunk()
    {
        byte[] data = new byte[13 + _extensionData.Length];
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(0, 2), ReleaseNumber);
        _serialNumber.CopyTo(data, 2);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(8, 2), Checksum);
        data[10] = (byte)(ProgramCounter >> 16);
        data[11] = (byte)(ProgramCounter >> 8);
        data[12] = (byte)ProgramCounter;
        _extensionData.CopyTo(data, 13);
        return new QuetzalChunk("IFhd", data);
    }
}
