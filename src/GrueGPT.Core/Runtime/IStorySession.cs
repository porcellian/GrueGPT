namespace GrueGPT.Core.Runtime;

/// <summary>
/// A running story and its mutable Z-machine execution state.
/// </summary>
/// <remarks>
/// The session communicates only through <see cref="IInterpreterIO"/> and
/// does not depend on console or GUI APIs. It runs until the story ends, its
/// host closes input, or cancellation is requested.
/// </remarks>
public interface IStorySession
{
    /// <summary>Runs the story using the supplied host input and output boundary.</summary>
    ValueTask RunAsync(IInterpreterIO io, CancellationToken cancellationToken = default);
}
