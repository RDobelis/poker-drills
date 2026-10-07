using System.Text;

namespace PokerDrills.Engine;

/// <summary>
/// xoshiro256** seeded through SplitMix64. Unlike System.Random its output is fixed by this code,
/// so the same seed gives the same drills on every machine and runtime.
/// </summary>
public sealed class Rng
{
    private ulong _s0, _s1, _s2, _s3;

    public Rng(ulong seed)
    {
        var x = seed;
        _s0 = SplitMix64(ref x);
        _s1 = SplitMix64(ref x);
        _s2 = SplitMix64(ref x);
        _s3 = SplitMix64(ref x);
    }

    /// <summary>An independent stream for a named purpose, e.g. one per rule.</summary>
    public static Rng ForLabel(ulong seed, string label) => new(seed ^ Fnv1a64(label));

    public ulong NextUInt64()
    {
        var result = ulong.RotateLeft(_s1 * 5, 7) * 9;
        var t = _s1 << 17;
        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = ulong.RotateLeft(_s3, 45);
        return result;
    }

    /// <summary>Uniform in [0, maxExclusive), unbiased (rejection sampling).</summary>
    public int NextInt(int maxExclusive)
    {
        if (maxExclusive <= 0) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
        var bound = (ulong)maxExclusive;
        var threshold = (0UL - bound) % bound; // 2^64 mod bound
        while (true)
        {
            var r = NextUInt64();
            if (r >= threshold) return (int)(r % bound);
        }
    }

    /// <summary>Uniform in [minInclusive, maxInclusive].</summary>
    public int NextInt(int minInclusive, int maxInclusive)
    {
        if (maxInclusive < minInclusive) throw new ArgumentOutOfRangeException(nameof(maxInclusive));
        return minInclusive + NextInt(maxInclusive - minInclusive + 1);
    }

    public T Pick<T>(IReadOnlyList<T> items) => items[NextInt(items.Count)];

    /// <summary>Uniform in [0, 1) with 53 random bits.</summary>
    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / (1UL << 53));

    public static ulong Fnv1a64(string text)
    {
        var hash = 14695981039346656037UL;
        foreach (var b in Encoding.UTF8.GetBytes(text))
        {
            hash ^= b;
            hash *= 1099511628211UL;
        }
        return hash;
    }

    public static uint Fnv1a32(string text)
    {
        var hash = 2166136261U;
        foreach (var b in Encoding.UTF8.GetBytes(text))
        {
            hash ^= b;
            hash *= 16777619U;
        }
        return hash;
    }

    private static ulong SplitMix64(ref ulong x)
    {
        x += 0x9E3779B97F4A7C15UL;
        var z = x;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}
