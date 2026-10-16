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

    /// <summary>Waits for a complete player command without echoing it; null means input is closed.</summary>
    ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken = default);

    /// <summary>Reports a fatal story-execution error to the host.</summary>
    ValueTask ReportErrorAsync(string message, CancellationToken cancellationToken = default);
}

/// <summary>Optional transcript and command-file capabilities for a Version 3 host.</summary>
public interface IZ3StreamIO
{
    /// <summary>Writes text to the selected transcript destination.</summary>
    ValueTask WriteTranscriptAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>Reads the next command from the selected command-file input stream.</summary>
    ValueTask<string?> ReadPlaybackCommandAsync(CancellationToken cancellationToken = default);

    /// <summary>Records one completed command to the selected command-file output stream.</summary>
    ValueTask WriteRecordedCommandAsync(string command, CancellationToken cancellationToken = default);
}
