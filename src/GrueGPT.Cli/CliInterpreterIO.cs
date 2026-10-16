using GrueGPT.Core.Runtime;

namespace GrueGPT.Cli;

internal sealed class CliInterpreterIO(TextReader input, TextWriter output, TextWriter error) : IInterpreterIO, IZ3SaveRestoreIO
{
    public async ValueTask WriteTextAsync(string text, CancellationToken cancellationToken = default)
    {
        await output.WriteAsync(text.AsMemory(), cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask UpdateStatusAsync(StoryStatus status, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(status);
        string gameStats = status.IsTimeGame
            ? $"Time: {status.Score:D2}:{status.Moves:D2}"
            : $"Score: {status.Score}  Moves: {status.Moves}";
        string statusLine = string.IsNullOrEmpty(status.Location)
            ? gameStats
            : $"{status.Location}  {gameStats}";
        await error.WriteLineAsync(statusLine.AsMemory(), cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken = default)
    {
        await output.WriteAsync("> ".AsMemory(), cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        return await input.ReadLineAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask ReportErrorAsync(string message, CancellationToken cancellationToken = default)
    {
        await error.WriteLineAsync(message.AsMemory(), cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<bool> SaveGameAsync(ReadOnlyMemory<byte> saveFile, CancellationToken cancellationToken = default)
    {
        string? path = await ReadSavePathAsync("Save file path (empty to cancel): ", cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            await File.WriteAllBytesAsync(path, saveFile.ToArray(), cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception) when (IsFileError(exception))
        {
            await ReportErrorAsync($"Unable to write save file '{path}': {exception.Message}", cancellationToken).ConfigureAwait(false);
            return false;
        }
    }

    public async ValueTask<byte[]?> LoadGameAsync(CancellationToken cancellationToken = default)
    {
        string? path = await ReadSavePathAsync("Restore file path (empty to cancel): ", cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsFileError(exception))
        {
            await ReportErrorAsync($"Unable to read save file '{path}': {exception.Message}", cancellationToken).ConfigureAwait(false);
            return null;
        }
    }

    private async ValueTask<string?> ReadSavePathAsync(string prompt, CancellationToken cancellationToken)
    {
        await error.WriteAsync(prompt.AsMemory(), cancellationToken).ConfigureAwait(false);
        await error.FlushAsync(cancellationToken).ConfigureAwait(false);
        return await input.ReadLineAsync(cancellationToken).ConfigureAwait(false);
    }

    private static bool IsFileError(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;
}
