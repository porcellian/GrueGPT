namespace GrueGPT.Core.Resources;

/// <summary>A resource index entry resolved to its chunk.</summary>
public sealed class BlorbResource
{
    internal BlorbResource(string usage, uint number, uint startOffset, BlorbChunk chunk)
    {
        Usage = usage;
        Number = number;
        StartOffset = startOffset;
        Chunk = chunk;
    }

    /// <summary>The four-character resource usage, such as <c>Exec</c> or <c>Pict</c>.</summary>
    public string Usage { get; }

    /// <summary>The resource number assigned by the game.</summary>
    public uint Number { get; }

    /// <summary>The indexed chunk-header offset from the beginning of the file.</summary>
    public uint StartOffset { get; }

    /// <summary>The chunk selected by this index entry.</summary>
    public BlorbChunk Chunk { get; }
}
