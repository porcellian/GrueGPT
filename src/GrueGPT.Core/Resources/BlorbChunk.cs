namespace GrueGPT.Core.Resources;

/// <summary>A chunk stored inside an IFRS Blorb FORM.</summary>
public sealed class BlorbChunk
{
    private readonly byte[] _data;

    internal BlorbChunk(string id, uint offset, ReadOnlySpan<byte> data)
    {
        Id = id;
        Offset = offset;
        _data = data.ToArray();
    }

    /// <summary>The case-sensitive four-character chunk identifier.</summary>
    public string Id { get; }

    /// <summary>The offset of the chunk header from the start of the Blorb file.</summary>
    public uint Offset { get; }

    /// <summary>The chunk data, excluding its header and alignment byte.</summary>
    public ReadOnlyMemory<byte> Data => _data;
}
