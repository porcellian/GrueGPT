namespace GrueGPT.Core.SaveFiles;

/// <summary>A single IFF chunk in a Quetzal save file.</summary>
public sealed class QuetzalChunk
{
    private readonly byte[] _data;

    /// <summary>Creates a chunk with a valid four-character IFF identifier.</summary>
    public QuetzalChunk(string id, ReadOnlySpan<byte> data)
    {
        IffIdentifier.ValidateChunkId(id, nameof(id));
        Id = id;
        _data = data.ToArray();
    }

    /// <summary>The case-sensitive four-character chunk identifier.</summary>
    public string Id { get; }

    /// <summary>The chunk's unpadded data.</summary>
    public ReadOnlyMemory<byte> Data => _data;
}

/// <summary>An IFZS FORM and its ordered chunks.</summary>
public sealed class QuetzalFile
{
    private readonly IReadOnlyList<QuetzalChunk> _chunks;

    /// <summary>Creates an IFZS file containing the provided chunks in order.</summary>
    public QuetzalFile(IEnumerable<QuetzalChunk> chunks)
    {
        ArgumentNullException.ThrowIfNull(chunks);

        QuetzalChunk[] copy = chunks.ToArray();
        if (copy.Any(static chunk => chunk is null))
        {
            throw new ArgumentException("A Quetzal file cannot contain a null chunk.", nameof(chunks));
        }

        _chunks = Array.AsReadOnly(copy);
    }

    /// <summary>The chunks in their original file order, including unknown and repeated IDs.</summary>
    public IReadOnlyList<QuetzalChunk> Chunks => _chunks;
}

internal static class IffIdentifier
{
    public static void ValidateChunkId(string id, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(id, parameterName);
        if (!IsValidChunkId(id))
        {
            throw new ArgumentException(
                "An IFF chunk ID must contain four printable ASCII characters, with any spaces trailing.",
                parameterName);
        }
    }

    public static bool IsValidChunkId(string id)
    {
        if (id.Length != 4)
        {
            return false;
        }

        bool foundSpace = false;
        foreach (char value in id)
        {
            if (value < 0x20 || value > 0x7E)
            {
                return false;
            }

            if (value == ' ')
            {
                foundSpace = true;
            }
            else if (foundSpace)
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsValidFormType(string formType)
    {
        if (formType.Length != 4)
        {
            return false;
        }

        bool foundSpace = false;
        foreach (char value in formType)
        {
            if (value == ' ')
            {
                foundSpace = true;
            }
            else if (foundSpace || value < 'A' || value > 'Z')
            {
                return false;
            }
        }

        return true;
    }
}
