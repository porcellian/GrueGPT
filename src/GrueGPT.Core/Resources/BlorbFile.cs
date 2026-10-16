namespace GrueGPT.Core.Resources;

/// <summary>A parsed Blorb IFRS FORM and its ordered chunks and resources.</summary>
public sealed class BlorbFile
{
    private readonly IReadOnlyList<BlorbChunk> _chunks;
    private readonly IReadOnlyList<BlorbResource> _resources;

    internal BlorbFile(IEnumerable<BlorbChunk> chunks, IEnumerable<BlorbResource> resources)
    {
        _chunks = Array.AsReadOnly(chunks.ToArray());
        _resources = Array.AsReadOnly(resources.ToArray());
    }

    /// <summary>All chunks, including the leading RIdx chunk, in file order.</summary>
    public IReadOnlyList<BlorbChunk> Chunks => _chunks;

    /// <summary>Resources in their resource-index order.</summary>
    public IReadOnlyList<BlorbResource> Resources => _resources;

    /// <summary>Finds the first resource matching a usage and number.</summary>
    public BlorbResource? FindResource(string usage, uint number) =>
        _resources.FirstOrDefault(resource =>
            string.Equals(resource.Usage, usage, StringComparison.Ordinal) && resource.Number == number);

    /// <summary>Returns the embedded Z-code executable bytes from the single Exec/0 resource.</summary>
    /// <exception cref="InvalidDataException">No executable is present or it is not a ZCOD chunk.</exception>
    public ReadOnlyMemory<byte> GetZCodeExecutable()
    {
        BlorbResource? executable = FindResource("Exec", 0);
        if (executable is null)
        {
            throw new InvalidDataException("The Blorb file has no Exec resource with number zero.");
        }

        if (!string.Equals(executable.Chunk.Id, "ZCOD", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"The Blorb executable chunk is '{executable.Chunk.Id}', not the Z-code 'ZCOD' type.");
        }

        return executable.Chunk.Data;
    }
}
