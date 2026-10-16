using System.Buffers.Binary;

namespace GrueGPT.Core.Stories;

/// <summary>Mutable version 3 memory with the story's dynamic and static regions.</summary>
public sealed class Z3Memory
{
    public const int Flags1Address = 0x01;
    public const int Flags2Address = 0x10;

    private const byte Flags2GameMutableMask = 0x03;
    private const byte Flags2GameClearOnlyMask = 0x04;
    private const byte Flags1InterpreterMutableMask = 0x70;
    private const byte Flags2InterpreterMutableMask = 0x00;

    private readonly byte[] _bytes;
    private readonly byte[] _originalBytes;

    public Z3Memory(LoadedStory story)
    {
        ArgumentNullException.ThrowIfNull(story);
        _originalBytes = story.Image.ToArray();
        _bytes = (byte[])_originalBytes.Clone();
        StaticMemoryBase = story.Header.StaticMemoryBase;
        HighMemoryBase = story.Header.HighMemoryBase;
    }

    /// <summary>First byte that cannot be modified by story code.</summary>
    public ushort StaticMemoryBase { get; }

    /// <summary>First byte of high memory, where routines and packed strings reside.</summary>
    public ushort HighMemoryBase { get; }

    public int Length => _bytes.Length;

    public byte ReadByte(int address)
    {
        ValidateByteAddress(address);
        return _bytes[address];
    }

    /// <summary>Reads an immutable byte from the loaded story image.</summary>
    public byte ReadOriginalByte(int address)
    {
        ValidateByteAddress(address);
        return _originalBytes[address];
    }

    public ushort ReadWord(int address)
    {
        ValidateByteAddress(address);
        if (address + sizeof(ushort) > _bytes.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(address), "A word read must fit within addressable story memory.");
        }

        return BinaryPrimitives.ReadUInt16BigEndian(_bytes.AsSpan(address, sizeof(ushort)));
    }

    /// <summary>
    /// Writes one byte as a story program. Static memory writes are rejected; header
    /// flag writes are restricted to the bits that the standard permits the game to change.
    /// </summary>
    public void WriteByte(int address, byte value)
    {
        ValidateDynamicAddress(address);
        if (address == Flags1Address)
        {
            // Version 3 Flags 1 has no game-writable bits.
            return;
        }

        if (address == Flags2Address)
        {
            // Flags 2 is a big-endian word at $10; bits 0-7 are in byte $11.
            return;
        }

        if (address == Flags2Address + 1)
        {
            byte current = _bytes[address];
            byte gameBits = (byte)(value & Flags2GameMutableMask);
            byte clearOnlyBits = (byte)(current & value & Flags2GameClearOnlyMask);
            _bytes[address] = (byte)((current & ~(Flags2GameMutableMask | Flags2GameClearOnlyMask)) | gameBits | clearOnlyBits);
            return;
        }

        _bytes[address] = value;
    }

    /// <summary>Writes a big-endian word as two consecutive story byte writes.</summary>
    public void WriteWord(int address, ushort value)
    {
        ValidateDynamicRange(address, sizeof(ushort));
        WriteByte(address, (byte)(value >> 8));
        WriteByte(address + 1, (byte)value);
    }

    /// <summary>Sets or clears interpreter-controlled Version 3 Flags 1 capabilities.</summary>
    public void SetInterpreterFlags1(byte mask, byte value)
    {
        if ((mask & ~Flags1InterpreterMutableMask) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mask), "Only Version 3 interpreter-controlled Flags 1 bits may be changed.");
        }

        _bytes[Flags1Address] = ApplyMask(_bytes[Flags1Address], mask, value);
    }

    /// <summary>Sets or clears interpreter-controlled Version 3 Flags 2 bits.</summary>
    public void SetInterpreterFlags2(byte mask, byte value)
    {
        if ((mask & ~Flags2InterpreterMutableMask) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mask), "Only Version 3 interpreter-controlled Flags 2 bits may be changed.");
        }

        _bytes[Flags2Address + 1] = ApplyMask(_bytes[Flags2Address + 1], mask, value);
    }

    /// <summary>Converts a Version 3 packed routine or string address to a byte address.</summary>
    public static int UnpackAddress(ushort packedAddress) => packedAddress * 2;

    /// <summary>Converts an even byte address to its Version 3 packed form.</summary>
    public static ushort PackAddress(int byteAddress)
    {
        if (byteAddress < 0 || (byteAddress & 1) != 0 || byteAddress / 2 > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(byteAddress), "A Version 3 packed address must represent an even address from 0 through 131070.");
        }

        return (ushort)(byteAddress / 2);
    }

    private void ValidateByteAddress(int address)
    {
        if (address < 0 || address >= _bytes.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(address), "The address must identify a byte present in the story image.");
        }
    }

    private void ValidateDynamicAddress(int address)
    {
        ValidateByteAddress(address);
        if (address >= StaticMemoryBase)
        {
            throw new InvalidOperationException($"Story code cannot write to static memory at address 0x{address:X4}.");
        }
    }

    private void ValidateDynamicRange(int address, int length)
    {
        if (length <= 0 || address < 0 || address > StaticMemoryBase - length)
        {
            throw new InvalidOperationException("The write range must fit entirely in dynamic memory.");
        }

        ValidateByteAddress(address);
        ValidateByteAddress(address + length - 1);
    }

    private static byte ApplyMask(byte current, byte mask, byte value) =>
        (byte)((current & ~mask) | (value & mask));
}
