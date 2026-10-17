namespace GrueGPT.Core.Stories;

/// <summary>Implements the Version 3 random opcode's range and seed rules.</summary>
public sealed class Z3RandomNumberGenerator
{
    private readonly int? _initialSeed;
    private Random _random;

    public Z3RandomNumberGenerator(int? seed = null)
    {
        _initialSeed = seed;
        _random = CreateRandom(seed);
    }

    /// <summary>Returns the generator to random mode, as at the start of a story.</summary>
    public void Reset() => _random = CreateRandom(_initialSeed);

    /// <summary>Returns a value in 1..range, or reseeds and returns zero.</summary>
    public ushort Next(ushort rangeOperand)
    {
        short range = unchecked((short)rangeOperand);
        if (range < 0)
        {
            _random = new Random(Math.Abs((int)range));
            return 0;
        }

        if (range == 0)
        {
            _random = new Random(Random.Shared.Next());
            return 0;
        }

        return (ushort)_random.Next(1, range + 1);
    }

    private static Random CreateRandom(int? seed) => seed is int value ? new Random(value) : new Random();
}
