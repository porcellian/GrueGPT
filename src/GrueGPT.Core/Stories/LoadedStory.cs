namespace GrueGPT.Core.Stories;

/// <summary>A validated, raw Z-machine v3 story image and its header metadata.</summary>
public sealed class LoadedStory
{
    internal LoadedStory(byte[] image, StoryHeader header)
    {
        Image = image;
        Header = header;
    }

    public StoryHeader Header { get; }

    /// <summary>
    /// Story bytes up to the logical file length in the header. Physical padding
    /// beyond that length is excluded from the image.
    /// </summary>
    public ReadOnlyMemory<byte> Image { get; }
}
