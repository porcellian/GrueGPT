namespace GrueGPT.Core.Runtime;

/// <summary>Current player-facing status for the initial Z-machine v3 UI.</summary>
/// <param name="Location">The current room or location name.</param>
/// <param name="Score">The signed story score.</param>
/// <param name="Moves">The number of moves made.</param>
public sealed record StoryStatus(string Location, short Score, ushort Moves);
