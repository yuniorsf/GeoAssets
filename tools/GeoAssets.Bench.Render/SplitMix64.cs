namespace GeoAssets.Bench.Render;

/// <summary>
/// SplitMix64 — a tiny, fully specified pseudo-random sequence, so the same seed yields the same dataset on every
/// machine and .NET version (<see cref="System.Random"/>'s algorithm is an implementation detail). Not for security.
/// </summary>
internal sealed class SplitMix64(ulong seed)
{
    private ulong _state = seed;

    public ulong NextUInt64()
    {
        var z = _state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>A double in [0, 1) from the top 53 bits.</summary>
    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / (1UL << 53));

    /// <summary>A double in [<paramref name="min"/>, <paramref name="max"/>).</summary>
    public double NextDouble(double min, double max) => min + (NextDouble() * (max - min));

    /// <summary>An integer in [<paramref name="min"/>, <paramref name="max"/>] (inclusive).</summary>
    public int NextInt(int min, int max) => min + (int)(NextUInt64() % (ulong)(max - min + 1));

    /// <summary>A standard normal value (Box–Muller).</summary>
    public double NextGaussian()
    {
        var u1 = 1.0 - NextDouble();
        var u2 = NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
}
