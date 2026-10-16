namespace GrueGPT.Core.Stories;

/// <summary>Validated fields used to identify and initialize a Z-machine v3 story.</summary>
public sealed record StoryHeader(
    byte Version,
    byte Flags1,
    ushort ReleaseNumber,
    ushort HighMemoryBase,
    ushort InitialProgramCounter,
    ushort DictionaryAddress,
    ushort ObjectTableAddress,
    ushort GlobalVariablesAddress,
    ushort StaticMemoryBase,
    ushort Flags2,
    ushort AbbreviationsAddress,
    ushort Checksum,
    bool HasChecksum,
    int FileLength,
    string SerialNumber);
