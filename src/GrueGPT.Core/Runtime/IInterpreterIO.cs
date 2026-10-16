using GrueGPT.Core.Stories;

namespace GrueGPT.Core.Runtime;

/// <summary>
/// Front-end boundary used by a story session to exchange story output,
/// status updates, and complete input lines with its host.
/// </summary>
/// <remarks>
/// Implementations may target a terminal, a desktop UI, or a test harness.
/// Story text is delivered in order; the host owns rendering and may consume
/// full Version 3 screen snapshots through <see cref="IZ3ScreenIO"/>. A null
/// input line means the host has closed its input source.
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

/// <summary>Optional callback for hosts that render the Version 3 character-cell screen model.</summary>
public interface IZ3ScreenIO
{
    /// <summary>Publishes the latest status, window contents, prompt, and style metadata.</summary>
    ValueTask PresentScreenAsync(Z3ScreenSnapshot snapshot, CancellationToken cancellationToken = default);
}

/// <summary>Optional host file-selection and persistence boundary for Version 3 Quetzal saves.</summary>
public interface IZ3SaveRestoreIO
{
    /// <summary>Stores a complete Quetzal file; false means the user cancelled or the write failed.</summary>
    ValueTask<bool> SaveGameAsync(ReadOnlyMemory<byte> saveFile, CancellationToken cancellationToken = default);

    /// <summary>Reads a selected Quetzal file; null means the user cancelled or the read failed.</summary>
    ValueTask<byte[]?> LoadGameAsync(CancellationToken cancellationToken = default);
}
