using System.Buffers.Binary;

namespace GrueGPT.Tests.TestUtilities;

/// <summary>
/// Creates deterministic, generated Z-machine version 3 story images for tests.
/// The default image contains a plausible header and zero-filled memory; tests
/// can overwrite fields or memory to exercise valid and malformed inputs.
/// </summary>
internal sealed class SyntheticStoryBuilder
{
    public const int HeaderSize = 64;
    public const int DefaultFileSize = 0x8000;

    private readonly byte[] _image;
    private byte _version = 3;
    private ushort _highMemoryBase = 0x2000;
    private ushort _initialProgramCounter = 0x2000;
    private ushort _dictionaryAddress = 0x0100;
    private ushort _objectTableAddress = 0x0120;
    private ushort _globalVariablesAddress = 0x0200;
    private ushort _staticMemoryBase = 0x1000;
    private ushort _abbreviationsAddress = 0x0300;
    private ushort? _checksumOverride;

    public SyntheticStoryBuilder(int fileSize = DefaultFileSize)
    {
        if (fileSize < HeaderSize || fileSize > 0x1FFFE || (fileSize & 1) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fileSize),
                "A version 3 story image must be an even number of bytes between 64 and 131070.");
        }

        _image = new byte[fileSize];
    }

    public SyntheticStoryBuilder WithVersion(byte version)
    {
        _version = version;
        return this;
    }

    public SyntheticStoryBuilder WithHighMemoryBase(ushort address)
    {
        _highMemoryBase = address;
        return this;
    }

    public SyntheticStoryBuilder WithInitialProgramCounter(ushort address)
    {
        _initialProgramCounter = address;
        return this;
    }

    public SyntheticStoryBuilder WithDictionaryAddress(ushort address)
    {
        _dictionaryAddress = address;
        return this;
    }

    public SyntheticStoryBuilder WithObjectTableAddress(ushort address)
    {
        _objectTableAddress = address;
        return this;
    }

    public SyntheticStoryBuilder WithGlobalVariablesAddress(ushort address)
    {
        _globalVariablesAddress = address;
        return this;
    }

    public SyntheticStoryBuilder WithStaticMemoryBase(ushort address)
    {
        _staticMemoryBase = address;
        return this;
    }

    public SyntheticStoryBuilder WithAbbreviationsAddress(ushort address)
    {
        _abbreviationsAddress = address;
        return this;
    }

    /// <summary>Overrides the automatically calculated checksum for malformed-file tests.</summary>
    public SyntheticStoryBuilder WithChecksum(ushort checksum)
    {
        _checksumOverride = checksum;
        return this;
    }

    public SyntheticStoryBuilder WriteByte(int address, byte value)
    {
        ValidateRange(address, 1);
        _image[address] = value;
        return this;
    }

    public SyntheticStoryBuilder WriteWord(int address, ushort value)
    {
        ValidateRange(address, sizeof(ushort));
        BinaryPrimitives.WriteUInt16BigEndian(_image.AsSpan(address, sizeof(ushort)), value);
        return this;
    }

    public SyntheticStoryBuilder WriteBytes(int address, ReadOnlySpan<byte> values)
    {
        ValidateRange(address, values.Length);
        values.CopyTo(_image.AsSpan(address, values.Length));
        return this;
    }

    /// <summary>Builds a fresh image and sets the Z-machine header and checksum.</summary>
    public byte[] Build()
    {
        byte[] image = (byte[])_image.Clone();
        image[0] = _version;
        WriteHeaderWord(image, 0x04, _highMemoryBase);
        WriteHeaderWord(image, 0x06, _initialProgramCounter);
        WriteHeaderWord(image, 0x08, _dictionaryAddress);
        WriteHeaderWord(image, 0x0A, _objectTableAddress);
        WriteHeaderWord(image, 0x0C, _globalVariablesAddress);
        WriteHeaderWord(image, 0x0E, _staticMemoryBase);
        image[0x12] = (byte)'2';
        image[0x13] = (byte)'6';
        image[0x14] = (byte)'0';
        image[0x15] = (byte)'1';
        image[0x16] = (byte)'0';
        image[0x17] = (byte)'1';
        WriteHeaderWord(image, 0x18, _abbreviationsAddress);
        WriteHeaderWord(image, 0x1A, checked((ushort)(image.Length / 2)));

        ushort checksum = _checksumOverride ?? CalculateChecksum(image);
        WriteHeaderWord(image, 0x1C, checksum);
        return image;
    }

    public MemoryStream BuildStream() => new(Build(), writable: false);

    private static ushort CalculateChecksum(ReadOnlySpan<byte> image)
    {
        uint sum = 0;
        foreach (byte value in image[HeaderSize..])
        {
            sum += value;
        }

        return unchecked((ushort)sum);
    }

    private static void WriteHeaderWord(Span<byte> image, int address, ushort value) =>
        BinaryPrimitives.WriteUInt16BigEndian(image.Slice(address, sizeof(ushort)), value);

    private void ValidateRange(int address, int length)
    {
        if (address < 0 || length < 0 || address > _image.Length - length)
        {
            throw new ArgumentOutOfRangeException(nameof(address), "The write must fit within the story image.");
        }
    }
}
