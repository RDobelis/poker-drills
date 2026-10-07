using System.Globalization;
using System.Text.Json.Serialization;

namespace PokerDrills.Engine;

public static class StatKeys
{
    public const string Vpip = "VPIP", Pfr = "PFR", ThreeBet = "3Bet", Wtsd = "WTSD", Af = "AF", FoldToCbet = "FoldToCbet";

    public static IReadOnlyList<string> All { get; } = [Vpip, Pfr, ThreeBet, Wtsd, Af, FoldToCbet];
}

public readonly record struct StatRange(double Min, double Max)
{
    public bool Contains(double value) => value >= Min && value <= Max;

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Min:0.##}-{Max:0.##}");
}

/// <param name="TableShare">Relative weight of this type when filling the other seats at the table.</param>
/// <param name="ShortName">Label on a table seat, where space is tight (defaults to the name).</param>
public sealed record PlayerType(
    string Id,
    string Name,
    string Description,
    bool Placeholder,
    IReadOnlyDictionary<string, StatRange> Ranges,
    int TableShare = 1,
    string? ShortName = null)
{
    public string SeatLabel => string.IsNullOrWhiteSpace(ShortName) ? Name : ShortName;

    public bool Contains(StatLine stats) => StatKeys.All.All(k => Ranges[k].Contains(stats[k]));
}

/// <summary>One HUD stat line. Percentages are integers; AF has one decimal.</summary>
public sealed record StatLine(
    [property: JsonPropertyName("VPIP")] int Vpip,
    [property: JsonPropertyName("PFR")] int Pfr,
    [property: JsonPropertyName("3Bet")] int ThreeBet,
    [property: JsonPropertyName("WTSD")] int Wtsd,
    [property: JsonPropertyName("AF")] double Af,
    [property: JsonPropertyName("FoldToCbet")] int FoldToCbet)
{
    public double this[string key] => key switch
    {
        StatKeys.Vpip => Vpip,
        StatKeys.Pfr => Pfr,
        StatKeys.ThreeBet => ThreeBet,
        StatKeys.Wtsd => Wtsd,
        StatKeys.Af => Af,
        StatKeys.FoldToCbet => FoldToCbet,
        _ => throw new KeyNotFoundException(key),
    };

    // Invariant: also used as a hash key for drill ids, which must not depend on the machine's locale.
    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"VPIP {Vpip} / PFR {Pfr} / 3Bet {ThreeBet} / WTSD {Wtsd} / AF {Af:0.0} / FoldToCbet {FoldToCbet}");
}

public static class StatSampler
{
    private const int MaxTries = 10_000;

    /// <summary>
    /// Uniform stat line inside the type's ranges, with PFR &lt;= VPIP and 3Bet &lt;= PFR
    /// (anything else is not a real HUD line).
    /// </summary>
    public static StatLine Sample(PlayerType type, Rng rng)
    {
        for (var i = 0; i < MaxTries; i++)
        {
            var s = new StatLine(
                Int(type, StatKeys.Vpip, rng),
                Int(type, StatKeys.Pfr, rng),
                Int(type, StatKeys.ThreeBet, rng),
                Int(type, StatKeys.Wtsd, rng),
                Tenths(type, StatKeys.Af, rng),
                Int(type, StatKeys.FoldToCbet, rng));
            if (s.Pfr <= s.Vpip && s.ThreeBet <= s.Pfr) return s;
        }
        throw new InvalidOperationException($"Could not sample a consistent stat line for {type.Id}; check its ranges");
    }

    /// <summary>A type drawn in proportion to <see cref="PlayerType.TableShare"/>.</summary>
    public static PlayerType PickByTableShare(IReadOnlyList<PlayerType> types, Rng rng)
    {
        var total = types.Sum(t => t.TableShare);
        if (total <= 0) throw new InvalidOperationException("No player type has a tableShare above 0");
        var x = rng.NextInt(total);
        foreach (var t in types)
        {
            if (x < t.TableShare) return t;
            x -= t.TableShare;
        }
        throw new InvalidOperationException("unreachable: x is below the total share");
    }

    public static IReadOnlyList<PlayerType> TypesContaining(IEnumerable<PlayerType> types, StatLine stats) =>
        types.Where(t => t.Contains(stats)).ToList();

    /// <summary>Samples until the line lies inside this type's ranges and no other type's.</summary>
    public static StatLine SampleUnambiguous(PlayerType type, IReadOnlyList<PlayerType> allTypes, Rng rng)
    {
        for (var i = 0; i < MaxTries; i++)
        {
            var s = Sample(type, rng);
            if (TypesContaining(allTypes, s).Count == 1) return s;
        }
        throw new InvalidOperationException($"Stat ranges of {type.Id} overlap other types too much to sample");
    }

    internal static (int Min, int Max) IntBounds(StatRange r) => ((int)Math.Ceiling(r.Min), (int)Math.Floor(r.Max));

    internal static (int Min, int Max) TenthBounds(StatRange r) =>
        ((int)Math.Ceiling(Math.Round(r.Min * 10, 6)), (int)Math.Floor(Math.Round(r.Max * 10, 6)));

    private static int Int(PlayerType t, string key, Rng rng)
    {
        var (min, max) = IntBounds(t.Ranges[key]);
        return rng.NextInt(min, max);
    }

    private static double Tenths(PlayerType t, string key, Rng rng)
    {
        var (min, max) = TenthBounds(t.Ranges[key]);
        return rng.NextInt(min, max) / 10.0;
    }
}
