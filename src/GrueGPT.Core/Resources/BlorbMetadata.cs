using System.Buffers.Binary;
using System.Text;
using System.Xml;

namespace GrueGPT.Core.Resources;

/// <summary>Parsed metadata and Z-machine resource hints from optional Blorb chunks.</summary>
public sealed class BlorbMetadata
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly IReadOnlyList<BlorbResourceDescription> _descriptions;
    private readonly IReadOnlyList<uint> _adaptivePalettePictures;
    private readonly IReadOnlyList<BlorbSoundLoop> _soundLoops;

    private BlorbMetadata(
        string? xml,
        string? author,
        string? copyright,
        string? annotation,
        BlorbGameIdentifier? gameIdentifier,
        ushort releaseNumber,
        BlorbResource? frontispiece,
        BlorbPalette? palette,
        BlorbResolution? resolution,
        IEnumerable<BlorbResourceDescription> descriptions,
        IEnumerable<uint> adaptivePalettePictures,
        IEnumerable<BlorbSoundLoop> soundLoops)
    {
        Xml = xml;
        Author = author;
        Copyright = copyright;
        Annotation = annotation;
        GameIdentifier = gameIdentifier;
        ReleaseNumber = releaseNumber;
        Frontispiece = frontispiece;
        Palette = palette;
        Resolution = resolution;
        _descriptions = Array.AsReadOnly(descriptions.ToArray());
        _adaptivePalettePictures = Array.AsReadOnly(adaptivePalettePictures.ToArray());
        _soundLoops = Array.AsReadOnly(soundLoops.ToArray());
    }

    /// <summary>IFmd XML text, or <see langword="null"/> when the chunk is absent.</summary>
    public string? Xml { get; }

    /// <summary>Optional ASCII author name from AUTH.</summary>
    public string? Author { get; }

    /// <summary>Optional ASCII copyright notice from (c).</summary>
    public string? Copyright { get; }

    /// <summary>Optional ASCII annotation from ANNO.</summary>
    public string? Annotation { get; }

    /// <summary>The optional Z-code IFhd game identity.</summary>
    public BlorbGameIdentifier? GameIdentifier { get; }

    /// <summary>The resource release number, defaulting to zero when RelN is absent.</summary>
    public ushort ReleaseNumber { get; }

    /// <summary>The picture resource designated by Fspc, if present.</summary>
    public BlorbResource? Frontispiece { get; }

    /// <summary>The optional Plte display hint.</summary>
    public BlorbPalette? Palette { get; }

    /// <summary>The optional Reso screen and scalable-picture information.</summary>
    public BlorbResolution? Resolution { get; }

    /// <summary>RDes entries in their file order.</summary>
    public IReadOnlyList<BlorbResourceDescription> ResourceDescriptions => _descriptions;

    /// <summary>Finds a textual description by resource usage and number.</summary>
    public BlorbResourceDescription? FindDescription(string usage, uint number) =>
        _descriptions.FirstOrDefault(description =>
            string.Equals(description.Usage, usage, StringComparison.Ordinal) && description.Number == number);

    /// <summary>Picture numbers listed by the optional APal chunk.</summary>
    public IReadOnlyList<uint> AdaptivePalettePictures => _adaptivePalettePictures;

    /// <summary>Version 3 sound looping hints from the optional Loop chunk.</summary>
    public IReadOnlyList<BlorbSoundLoop> SoundLoops => _soundLoops;

    internal static BlorbMetadata Parse(IReadOnlyList<BlorbChunk> chunks, IReadOnlyList<BlorbResource> resources)
    {
        BlorbChunk? metadataChunk = GetOptionalChunk(chunks, "IFmd");
        string? xml = metadataChunk is null ? null : ReadXml(metadataChunk.Data.Span);
        string? author = ReadOptionalAsciiChunk(GetOptionalChunk(chunks, "AUTH"));
        string? copyright = ReadOptionalAsciiChunk(GetOptionalChunk(chunks, "(c) "));
        string? annotation = ReadOptionalAsciiChunk(GetOptionalChunk(chunks, "ANNO"));
        BlorbGameIdentifier? gameIdentifier = ParseGameIdentifier(GetOptionalChunk(chunks, "IFhd"));
        ushort releaseNumber = ParseReleaseNumber(GetOptionalChunk(chunks, "RelN"));
        BlorbResource? frontispiece = ParseFrontispiece(GetOptionalChunk(chunks, "Fspc"), resources);
        BlorbPalette? palette = ParsePalette(GetOptionalChunk(chunks, "Plte"));
        BlorbResolution? resolution = ParseResolution(GetOptionalChunk(chunks, "Reso"), resources);
        IReadOnlyList<BlorbResourceDescription> descriptions = ParseDescriptions(GetOptionalChunk(chunks, "RDes"), resources);
        IReadOnlyList<uint> adaptivePalettePictures = ParseAdaptivePalette(GetOptionalChunk(chunks, "APal"), resources);
        IReadOnlyList<BlorbSoundLoop> soundLoops = ParseSoundLoops(GetOptionalChunk(chunks, "Loop"), resources);

        return new BlorbMetadata(
            xml,
            author,
            copyright,
            annotation,
            gameIdentifier,
            releaseNumber,
            frontispiece,
            palette,
            resolution,
            descriptions,
            adaptivePalettePictures,
            soundLoops);
    }

    private static string? ReadOptionalAsciiChunk(BlorbChunk? chunk)
    {
        if (chunk is null)
        {
            return null;
        }

        ReadOnlySpan<byte> data = chunk.Data.Span;
        foreach (byte value in data)
        {
            if (value < 0x20 || value > 0x7E)
            {
                throw new InvalidDataException($"The {chunk.Id} chunk must contain printable ASCII text only.");
            }
        }

        return Encoding.ASCII.GetString(data);
    }

    private static BlorbChunk? GetOptionalChunk(IReadOnlyList<BlorbChunk> chunks, string id)
    {
        BlorbChunk? found = null;
        foreach (BlorbChunk chunk in chunks)
        {
            if (!string.Equals(chunk.Id, id, StringComparison.Ordinal))
            {
                continue;
            }

            if (found is not null)
            {
                throw new InvalidDataException($"A Blorb file may contain at most one '{id}' chunk.");
            }

            found = chunk;
        }

        return found;
    }

    private static string ReadXml(ReadOnlySpan<byte> data)
    {
        string xml;
        try
        {
            xml = StrictUtf8.GetString(data);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("The IFmd metadata chunk is not valid UTF-8.", exception);
        }

        try
        {
            using StringReader input = new(xml);
            using XmlReader reader = XmlReader.Create(input, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
            });
            while (reader.Read())
            {
            }
        }
        catch (XmlException exception)
        {
            throw new InvalidDataException("The IFmd metadata chunk is not well-formed XML.", exception);
        }

        return xml;
    }

    private static BlorbGameIdentifier? ParseGameIdentifier(BlorbChunk? chunk)
    {
        if (chunk is null)
        {
            return null;
        }

        ReadOnlySpan<byte> data = chunk.Data.Span;
        if (data.Length < 13)
        {
            throw new InvalidDataException("The IFhd game identifier chunk must contain at least 13 bytes.");
        }

        uint initialProgramCounter = ((uint)data[10] << 16) | ((uint)data[11] << 8) | data[12];
        return new BlorbGameIdentifier(
            BinaryPrimitives.ReadUInt16BigEndian(data[..2]),
            data.Slice(2, 6),
            BinaryPrimitives.ReadUInt16BigEndian(data.Slice(8, 2)),
            initialProgramCounter,
            data[13..]);
    }

    private static ushort ParseReleaseNumber(BlorbChunk? chunk)
    {
        if (chunk is null)
        {
            return 0;
        }

        if (chunk.Data.Length != sizeof(ushort))
        {
            throw new InvalidDataException("The RelN release number chunk must contain exactly two bytes.");
        }

        return BinaryPrimitives.ReadUInt16BigEndian(chunk.Data.Span);
    }

    private static BlorbResource? ParseFrontispiece(BlorbChunk? chunk, IReadOnlyList<BlorbResource> resources)
    {
        if (chunk is null)
        {
            return null;
        }

        if (chunk.Data.Length != sizeof(uint))
        {
            throw new InvalidDataException("The Fspc frontispiece chunk must contain exactly four bytes.");
        }

        uint number = BinaryPrimitives.ReadUInt32BigEndian(chunk.Data.Span);
        BlorbResource? picture = FindResource(resources, "Pict", number);
        if (picture is null)
        {
            throw new InvalidDataException($"The Fspc chunk refers to missing picture resource {number}.");
        }

        if (string.Equals(picture.Chunk.Id, "Rect", StringComparison.Ordinal))
        {
            throw new InvalidDataException("A Rect placeholder cannot be used as a Blorb frontispiece.");
        }

        return picture;
    }

    private static BlorbPalette? ParsePalette(BlorbChunk? chunk)
    {
        if (chunk is null)
        {
            return null;
        }

        ReadOnlySpan<byte> data = chunk.Data.Span;
        if (data.Length == 1)
        {
            if (data[0] is not (16 or 32))
            {
                throw new InvalidDataException("A one-byte Plte chunk must specify a color depth of 16 or 32 bits.");
            }

            return BlorbPalette.ForMinimumColorDepth(data[0]);
        }

        if (data.Length == 0 || data.Length % 3 != 0 || data.Length > 256 * 3)
        {
            throw new InvalidDataException("A Plte chunk must contain one color-depth byte or 1 through 256 RGB colors.");
        }

        List<BlorbRgbColor> colors = new(data.Length / 3);
        for (int offset = 0; offset < data.Length; offset += 3)
        {
            colors.Add(new BlorbRgbColor(data[offset], data[offset + 1], data[offset + 2]));
        }

        return BlorbPalette.ForColors(colors);
    }

    private static BlorbResolution? ParseResolution(BlorbChunk? chunk, IReadOnlyList<BlorbResource> resources)
    {
        if (chunk is null)
        {
            return null;
        }

        ReadOnlySpan<byte> data = chunk.Data.Span;
        if (data.Length < 24 || (data.Length - 24) % 28 != 0)
        {
            throw new InvalidDataException("The Reso chunk must contain a 24-byte header and complete 28-byte entries.");
        }

        uint standardWidth = ReadUInt32(data, 0);
        uint standardHeight = ReadUInt32(data, 4);
        uint minimumWidth = ReadUInt32(data, 8);
        uint minimumHeight = ReadUInt32(data, 12);
        uint maximumWidth = ReadUInt32(data, 16);
        uint maximumHeight = ReadUInt32(data, 20);
        if (standardWidth == 0 || standardHeight == 0
            || (minimumWidth != 0 && minimumWidth > standardWidth)
            || (minimumHeight != 0 && minimumHeight > standardHeight)
            || (maximumWidth != 0 && standardWidth > maximumWidth)
            || (maximumHeight != 0 && standardHeight > maximumHeight))
        {
            throw new InvalidDataException("The Reso window dimensions contain invalid standard, minimum, or maximum values.");
        }

        List<BlorbImageScale> imageScales = new((data.Length - 24) / 28);
        HashSet<uint> pictureNumbers = [];
        for (int offset = 24; offset < data.Length; offset += 28)
        {
            uint number = ReadUInt32(data, offset);
            if (!pictureNumbers.Add(number))
            {
                throw new InvalidDataException($"The Reso chunk contains duplicate picture entry {number}.");
            }

            if (FindResource(resources, "Pict", number) is null)
            {
                throw new InvalidDataException($"The Reso chunk refers to missing picture resource {number}.");
            }

            BlorbScaleRatio standard = ReadRatio(data, offset + 4, "standard", allowUnbounded: false);
            BlorbScaleRatio minimum = ReadRatio(data, offset + 12, "minimum", allowUnbounded: true);
            BlorbScaleRatio maximum = ReadRatio(data, offset + 20, "maximum", allowUnbounded: true);
            if (!minimum.IsUnbounded && !maximum.IsUnbounded && minimum.CompareTo(maximum) > 0)
            {
                throw new InvalidDataException($"The Reso chunk has a minimum scale above its maximum for picture {number}.");
            }

            imageScales.Add(new BlorbImageScale(number, standard, minimum, maximum));
        }

        return new BlorbResolution(
            standardWidth,
            standardHeight,
            minimumWidth,
            minimumHeight,
            maximumWidth,
            maximumHeight,
            imageScales);
    }

    private static IReadOnlyList<BlorbResourceDescription> ParseDescriptions(
        BlorbChunk? chunk,
        IReadOnlyList<BlorbResource> resources)
    {
        if (chunk is null)
        {
            return Array.Empty<BlorbResourceDescription>();
        }

        ReadOnlySpan<byte> data = chunk.Data.Span;
        if (data.Length < sizeof(uint))
        {
            throw new InvalidDataException("The RDes chunk is missing its entry count.");
        }

        uint count = ReadUInt32(data, 0);
        List<BlorbResourceDescription> descriptions = new();
        HashSet<(string Usage, uint Number)> keys = [];
        int offset = sizeof(uint);
        for (uint index = 0; index < count; index++)
        {
            if (data.Length - offset < 12)
            {
                throw new InvalidDataException("The RDes chunk ends before a complete description header.");
            }

            string usage = ReadIdentifier(data.Slice(offset, 4), "resource description usage");
            uint number = ReadUInt32(data, offset + 4);
            uint textLength = ReadUInt32(data, offset + 8);
            offset += 12;
            if (textLength > (uint)(data.Length - offset))
            {
                throw new InvalidDataException("A resource description extends beyond the RDes chunk.");
            }

            if (!keys.Add((usage, number)))
            {
                throw new InvalidDataException($"The RDes chunk contains duplicate '{usage}' resource number {number}.");
            }

            if (FindResource(resources, usage, number) is null)
            {
                throw new InvalidDataException($"The RDes chunk refers to missing '{usage}' resource number {number}.");
            }

            string description;
            try
            {
                description = StrictUtf8.GetString(data.Slice(offset, checked((int)textLength)));
            }
            catch (DecoderFallbackException exception)
            {
                throw new InvalidDataException("A resource description is not valid UTF-8.", exception);
            }

            descriptions.Add(new BlorbResourceDescription(usage, number, description));
            offset += checked((int)textLength);
        }

        if (offset != data.Length)
        {
            throw new InvalidDataException("The RDes chunk contains bytes after its declared entries.");
        }

        return descriptions.AsReadOnly();
    }

    private static IReadOnlyList<uint> ParseAdaptivePalette(BlorbChunk? chunk, IReadOnlyList<BlorbResource> resources)
    {
        if (chunk is null)
        {
            return Array.Empty<uint>();
        }

        if (resources.Any(resource =>
                string.Equals(resource.Usage, "Pict", StringComparison.Ordinal)
                && resource.Chunk.Id is not ("PNG " or "Rect")))
        {
            throw new InvalidDataException("When APal is present, all picture resources must use PNG or Rect chunks.");
        }

        ReadOnlySpan<byte> data = chunk.Data.Span;
        if (data.Length % sizeof(uint) != 0)
        {
            throw new InvalidDataException("The APal chunk length must be a multiple of four bytes.");
        }

        List<uint> pictureNumbers = new(data.Length / sizeof(uint));
        HashSet<uint> uniqueNumbers = [];
        for (int offset = 0; offset < data.Length; offset += sizeof(uint))
        {
            uint number = ReadUInt32(data, offset);
            if (!uniqueNumbers.Add(number))
            {
                throw new InvalidDataException($"The APal chunk contains duplicate picture resource {number}.");
            }

            if (FindResource(resources, "Pict", number) is null)
            {
                throw new InvalidDataException($"The APal chunk refers to missing picture resource {number}.");
            }

            pictureNumbers.Add(number);
        }

        return pictureNumbers.AsReadOnly();
    }

    private static IReadOnlyList<BlorbSoundLoop> ParseSoundLoops(BlorbChunk? chunk, IReadOnlyList<BlorbResource> resources)
    {
        if (chunk is null)
        {
            return Array.Empty<BlorbSoundLoop>();
        }

        ReadOnlySpan<byte> data = chunk.Data.Span;
        if (data.Length % 8 != 0)
        {
            throw new InvalidDataException("The Loop chunk length must be a multiple of eight bytes.");
        }

        List<BlorbSoundLoop> soundLoops = new(data.Length / 8);
        HashSet<uint> soundNumbers = [];
        for (int offset = 0; offset < data.Length; offset += 8)
        {
            uint number = ReadUInt32(data, offset);
            uint repeats = ReadUInt32(data, offset + 4);
            if (repeats > 1)
            {
                throw new InvalidDataException($"The Loop entry for sound {number} has an invalid repeat flag.");
            }

            if (!soundNumbers.Add(number))
            {
                throw new InvalidDataException($"The Loop chunk contains duplicate sound resource {number}.");
            }

            if (FindResource(resources, "Snd ", number) is null)
            {
                throw new InvalidDataException($"The Loop chunk refers to missing sound resource {number}.");
            }

            soundLoops.Add(new BlorbSoundLoop(number, repeats == 0));
        }

        return soundLoops.AsReadOnly();
    }

    private static BlorbScaleRatio ReadRatio(ReadOnlySpan<byte> data, int offset, string description, bool allowUnbounded)
    {
        uint numerator = ReadUInt32(data, offset);
        uint denominator = ReadUInt32(data, offset + 4);
        if (allowUnbounded && numerator == 0 && denominator == 0)
        {
            return BlorbScaleRatio.Unbounded;
        }

        if (numerator == 0 || denominator == 0)
        {
            throw new InvalidDataException($"The Reso {description} ratio must have both values nonzero or both values zero when allowed.");
        }

        return new BlorbScaleRatio(numerator, denominator);
    }

    private static BlorbResource? FindResource(IReadOnlyList<BlorbResource> resources, string usage, uint number) =>
        resources.FirstOrDefault(resource =>
            string.Equals(resource.Usage, usage, StringComparison.Ordinal) && resource.Number == number);

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, sizeof(uint)));

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

/// <summary>Blorb game identity fields from an IFhd chunk.</summary>
public sealed class BlorbGameIdentifier
{
    private readonly byte[] _serialNumber;
    private readonly byte[] _extensionData;

    internal BlorbGameIdentifier(
        ushort releaseNumber,
        ReadOnlySpan<byte> serialNumber,
        ushort checksum,
        uint initialProgramCounter,
        ReadOnlySpan<byte> extensionData)
    {
        ReleaseNumber = releaseNumber;
        _serialNumber = serialNumber.ToArray();
        Checksum = checksum;
        InitialProgramCounter = initialProgramCounter;
        _extensionData = extensionData.ToArray();
    }

    /// <summary>The release number from IFhd.</summary>
    public ushort ReleaseNumber { get; }

    /// <summary>The six serial-number bytes from IFhd.</summary>
    public ReadOnlyMemory<byte> SerialNumber => _serialNumber;

    /// <summary>The story checksum from IFhd.</summary>
    public ushort Checksum { get; }

    /// <summary>The IFhd PC field, which Blorb specifies as zero for Z-code resources.</summary>
    public uint InitialProgramCounter { get; }

    /// <summary>Bytes following the standard IFhd fields, retained for forward compatibility.</summary>
    public ReadOnlyMemory<byte> ExtensionData => _extensionData;
}

/// <summary>A resource's UTF-8 textual description from RDes.</summary>
public sealed record BlorbResourceDescription(string Usage, uint Number, string Text);

/// <summary>An RGB color in a Blorb palette.</summary>
public readonly record struct BlorbRgbColor(byte Red, byte Green, byte Blue);

/// <summary>The form of a Blorb palette hint.</summary>
public enum BlorbPaletteKind
{
    Colors,
    MinimumColorDepth,
}

/// <summary>A Plte palette list or minimum display color depth.</summary>
public sealed class BlorbPalette
{
    private readonly IReadOnlyList<BlorbRgbColor> _colors;

    private BlorbPalette(BlorbPaletteKind kind, byte minimumColorDepth, IEnumerable<BlorbRgbColor> colors)
    {
        Kind = kind;
        MinimumColorDepth = minimumColorDepth;
        _colors = Array.AsReadOnly(colors.ToArray());
    }

    /// <summary>The interpretation of this Plte chunk.</summary>
    public BlorbPaletteKind Kind { get; }

    /// <summary>The recommended minimum bits per pixel, or zero for an explicit RGB list.</summary>
    public byte MinimumColorDepth { get; }

    /// <summary>The explicit RGB list, empty when the palette is a color-depth hint.</summary>
    public IReadOnlyList<BlorbRgbColor> Colors => _colors;

    internal static BlorbPalette ForMinimumColorDepth(byte depth) =>
        new(BlorbPaletteKind.MinimumColorDepth, depth, Array.Empty<BlorbRgbColor>());

    internal static BlorbPalette ForColors(IEnumerable<BlorbRgbColor> colors) =>
        new(BlorbPaletteKind.Colors, 0, colors);
}

/// <summary>A rational image scaling ratio from Reso.</summary>
public readonly record struct BlorbScaleRatio(uint Numerator, uint Denominator)
{
    /// <summary>True when the ratio is the (0,0) unbounded sentinel.</summary>
    public bool IsUnbounded => Numerator == 0 && Denominator == 0;

    internal static BlorbScaleRatio Unbounded => new(0, 0);

    internal int CompareTo(BlorbScaleRatio other)
    {
        ulong left = (ulong)Numerator * other.Denominator;
        ulong right = (ulong)other.Numerator * Denominator;
        return left.CompareTo(right);
    }
}

/// <summary>Scaling ratios and limits for one scalable picture.</summary>
public sealed record BlorbImageScale(
    uint PictureNumber,
    BlorbScaleRatio Standard,
    BlorbScaleRatio Minimum,
    BlorbScaleRatio Maximum);

/// <summary>The standard, minimum, and maximum window geometry and picture scaling hints from Reso.</summary>
public sealed class BlorbResolution
{
    private readonly IReadOnlyList<BlorbImageScale> _imageScales;

    internal BlorbResolution(
        uint standardWidth,
        uint standardHeight,
        uint minimumWidth,
        uint minimumHeight,
        uint maximumWidth,
        uint maximumHeight,
        IEnumerable<BlorbImageScale> imageScales)
    {
        StandardWidth = standardWidth;
        StandardHeight = standardHeight;
        MinimumWidth = minimumWidth;
        MinimumHeight = minimumHeight;
        MaximumWidth = maximumWidth;
        MaximumHeight = maximumHeight;
        _imageScales = Array.AsReadOnly(imageScales.ToArray());
    }

    /// <summary>Author's standard window width in screen pixels.</summary>
    public uint StandardWidth { get; }

    /// <summary>Author's standard window height in screen pixels.</summary>
    public uint StandardHeight { get; }

    /// <summary>Minimum window width, or zero for no limit.</summary>
    public uint MinimumWidth { get; }

    /// <summary>Minimum window height, or zero for no limit.</summary>
    public uint MinimumHeight { get; }

    /// <summary>Maximum window width, or zero for no limit.</summary>
    public uint MaximumWidth { get; }

    /// <summary>Maximum window height, or zero for no limit.</summary>
    public uint MaximumHeight { get; }

    /// <summary>Scale entries in their order within Reso.</summary>
    public IReadOnlyList<BlorbImageScale> ImageScales => _imageScales;
}

/// <summary>A sound's Version 3 repeat hint from Loop.</summary>
public sealed record BlorbSoundLoop(uint SoundNumber, bool Repeats);
