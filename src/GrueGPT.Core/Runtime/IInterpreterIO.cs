namespace GrueGPT.Core.Runtime;

/// <summary>
/// Front-end boundary used by a story session to exchange story output,
/// status updates, and complete input lines with its host.
/// </summary>
/// <remarks>
/// Implementations may target a terminal, a desktop UI, or a test harness.
/// Story text is delivered in order; the host owns its display and scrolling.
/// A null input line means the host has closed its input source.
/// </remarks>
public interface IInterpreterIO
{
    /// <summary>Writes a segment of story-generated text to the host.</summary>
    ValueTask WriteTextAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>Publishes the current location, score, and move count.</summary>
    ValueTask UpdateStatusAsync(StoryStatus status, CancellationToken cancellationToken = default);

    /// <summary>Waits for a complete player command, or returns null when input is closed.</summary>
    ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken = default);
}
