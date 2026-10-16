namespace GrueGPT.Core.Stories;

/// <summary>An invalid or unsupported story file was supplied to the loader.</summary>
public sealed class StoryLoadException : Exception
{
    public StoryLoadException(StoryLoadErrorCode code, string message)
        : base(message)
    {
        Code = code;
    }

    public StoryLoadErrorCode Code { get; }
}
