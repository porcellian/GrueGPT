namespace GrueGPT.Core.Stories;

/// <summary>Identifies why a story file could not be loaded.</summary>
public enum StoryLoadErrorCode
{
    FileTooSmall,
    FileTooLarge,
    UnsupportedVersion,
    TruncatedFile,
    InvalidHeader,
    ChecksumMismatch
}
