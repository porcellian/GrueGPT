namespace GrueGPT.Core.Runtime;

/// <summary>Current player-facing status for the initial Z-machine v3 UI.</summary>
/// <param name="Location">The current room or location name.</param>
/// <param name="Score">The signed story score, or the hour when <paramref name="IsTimeGame"/> is true.</param>
/// <param name="Moves">The number of moves, or the minute when <paramref name="IsTimeGame"/> is true.</param>
/// <param name="IsTimeGame">Whether the values in <paramref name="Score"/> and <paramref name="Moves"/> represent time.</param>
public sealed record StoryStatus(string Location, short Score, ushort Moves, bool IsTimeGame = false);
