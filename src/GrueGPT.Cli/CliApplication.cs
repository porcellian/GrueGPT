using GrueGPT.Core.Stories;

namespace GrueGPT.Cli;

/// <summary>Loads and runs one Version 3 story using the shared interpreter core.</summary>
public static class CliApplication
{
    private const string Usage = "Usage: GrueGPT.Cli <story-file> [--restore <save-file>]";

    /// <summary>Runs the CLI with injectable text streams for automated tests.</summary>
    public static async Task<int> RunAsync(
        IReadOnlyList<string> args,
        TextReader input,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        if (args.Count == 1 && args[0] is "--help" or "-h")
        {
            await output.WriteLineAsync(Usage).ConfigureAwait(false);
            return 0;
        }

        if (!TryParseArguments(args, out string? storyPath, out string? restorePath, out string? argumentError))
        {
            await error.WriteLineAsync(argumentError).ConfigureAwait(false);
            await error.WriteLineAsync(Usage).ConfigureAwait(false);
            return 2;
        }

        LoadedStory story;
        try
        {
            await using FileStream storyFile = new(storyPath!, FileMode.Open, FileAccess.Read, FileShare.Read);
            story = new Z3StoryLoader().Load(storyFile);
        }
        catch (Exception exception) when (IsFileOrStoryError(exception))
        {
            await error.WriteLineAsync($"Unable to load story '{storyPath}': {exception.Message}").ConfigureAwait(false);
            return 1;
        }

        Z3ExecutionState state = new(story);
        if (restorePath is not null)
        {
            try
            {
                byte[] saveFile = await File.ReadAllBytesAsync(restorePath, cancellationToken).ConfigureAwait(false);
                Z3RestoreResult result = Z3SaveRestoreService.TryRestore(state, saveFile);
                if (!result.Succeeded)
                {
                    await error.WriteLineAsync($"Unable to restore save file '{restorePath}': {result.Error}").ConfigureAwait(false);
                    return 1;
                }
            }
            catch (Exception exception) when (IsFileOrStoryError(exception))
            {
                await error.WriteLineAsync($"Unable to read save file '{restorePath}': {exception.Message}").ConfigureAwait(false);
                return 1;
            }
        }

        CliInterpreterIO io = new(input, output, error);
        Z3OpcodeExecutor executor = new(state, io);
        try
        {
            await executor.RunAsync(cancellationToken).ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // The executor reports its instruction and failure through the host boundary.
            return 1;
        }
    }

    private static bool TryParseArguments(
        IReadOnlyList<string> args,
        out string? storyPath,
        out string? restorePath,
        out string? error)
    {
        storyPath = null;
        restorePath = null;
        error = null;
        if (args.Count == 0 || string.IsNullOrWhiteSpace(args[0]) || args[0].StartsWith("-", StringComparison.Ordinal))
        {
            error = "A story file path is required.";
            return false;
        }

        storyPath = args[0];
        for (int index = 1; index < args.Count; index++)
        {
            if (args[index] != "--restore" || restorePath is not null || index + 1 >= args.Count
                || string.IsNullOrWhiteSpace(args[index + 1]) || args[index + 1].StartsWith("-", StringComparison.Ordinal))
            {
                error = $"Invalid argument '{args[index]}'.";
                return false;
            }

            restorePath = args[++index];
        }

        return true;
    }

    private static bool IsFileOrStoryError(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or StoryLoadException;
}
