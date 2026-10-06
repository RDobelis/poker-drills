namespace PokerDrills.Engine;

/// <summary>
/// A set of starting hands in standard notation, comma separated:
/// "TT" (one pair), "22+" (22 up to AA), "22-99", "AKs" / "AKo" / "AK" (both),
/// "A2s+" (A2s up to AKs), "KTo+" (KTo up to KQo), "K9s-K6s".
/// </summary>
public sealed class HandRange
{
    private readonly HashSet<(int Hi, int Lo, bool Suited)> _hands;

    private HandRange(string text, HashSet<(int Hi, int Lo, bool Suited)> hands)
    {
        Text = text;
        _hands = hands;
    }

    public string Text { get; }

    /// <summary>Two-card combos in the range: 6 per pair, 4 per suited hand, 12 per offsuit hand.</summary>
    public int Combos => _hands.Sum(h => h.Hi == h.Lo ? 6 : h.Suited ? 4 : 12);

    /// <summary>All 169 starting hands (1326 combos).</summary>
    public static HandRange All { get; } = Parse("22+, 32+, 42+, 52+, 62+, 72+, 82+, 92+, T2+, J2+, Q2+, K2+, A2+");

    public bool Contains(IReadOnlyList<Card> hole)
    {
        if (hole.Count != 2) throw new ArgumentException("Exactly 2 hole cards required", nameof(hole));
        var hi = Math.Max(hole[0].Rank, hole[1].Rank);
        var lo = Math.Min(hole[0].Rank, hole[1].Rank);
        return _hands.Contains((hi, lo, hi != lo && hole[0].Suit == hole[1].Suit));
    }

    public static HandRange Parse(string text)
    {
        var hands = new HashSet<(int, int, bool)>();
        foreach (var token in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var hand in ParseToken(token)) hands.Add(hand);
        }
        if (hands.Count == 0) throw new FormatException("Range is empty");
        return new HandRange(text, hands);
    }

    public override string ToString() => Text;

    /// <summary>Suffix: 's' suited, 'o' offsuit, 'b' both, 'p' pair.</summary>
    private readonly record struct Spec(int Hi, int Lo, char Suffix);

    private static List<(int, int, bool)> ParseToken(string token)
    {
        var specs = new List<Spec>();
        var dash = token.IndexOf('-');
        if (dash >= 0)
        {
            var a = ParseSpec(token[..dash], token);
            var b = ParseSpec(token[(dash + 1)..], token);
            if (a.Suffix != b.Suffix) throw Bad(token, "both ends must be the same kind of hand");
            if (a.Suffix == 'p')
            {
                for (var r = Math.Min(a.Hi, b.Hi); r <= Math.Max(a.Hi, b.Hi); r++) specs.Add(new Spec(r, r, 'p'));
            }
            else
            {
                if (a.Hi != b.Hi) throw Bad(token, "both ends must share the first card, e.g. K9s-K6s");
                for (var lo = Math.Min(a.Lo, b.Lo); lo <= Math.Max(a.Lo, b.Lo); lo++) specs.Add(a with { Lo = lo });
            }
        }
        else if (token.EndsWith('+'))
        {
            var s = ParseSpec(token[..^1], token);
            if (s.Suffix == 'p')
            {
                for (var r = s.Hi; r <= Ranks.Ace; r++) specs.Add(new Spec(r, r, 'p'));
            }
            else
            {
                for (var lo = s.Lo; lo < s.Hi; lo++) specs.Add(s with { Lo = lo });
            }
        }
        else
        {
            specs.Add(ParseSpec(token, token));
        }

        var hands = new List<(int, int, bool)>();
        foreach (var s in specs)
        {
            if (s.Suffix is 'p' or 'o' or 'b') hands.Add((s.Hi, s.Lo, false));
            if (s.Suffix is 's' or 'b') hands.Add((s.Hi, s.Lo, true));
        }
        return hands;
    }

    private static Spec ParseSpec(string text, string token)
    {
        if (text.Length is < 2 or > 3) throw Bad(token, "expected a hand like 'AKs', 'T9o', 'QQ' or 'AK'");
        int hi, lo;
        try
        {
            hi = Ranks.Parse(text[0]);
            lo = Ranks.Parse(text[1]);
        }
        catch (FormatException)
        {
            throw Bad(token, "unknown rank");
        }

        if (hi == lo)
        {
            if (text.Length == 3) throw Bad(token, "pairs take no s/o suffix");
            return new Spec(hi, lo, 'p');
        }
        if (hi < lo) throw Bad(token, "write the higher card first, e.g. 'K9s'");
        if (text.Length == 2) return new Spec(hi, lo, 'b');
        return char.ToLowerInvariant(text[2]) switch
        {
            's' => new Spec(hi, lo, 's'),
            'o' => new Spec(hi, lo, 'o'),
            _ => throw Bad(token, "suffix must be 's' or 'o'"),
        };
    }

    private static FormatException Bad(string token, string why) => new($"'{token}': {why}");
}

/// <summary>
/// Which hands hero can plausibly hold in a spot: opening ranges by seat, and big blind
/// flat-calling ranges by the opener's seat.
/// </summary>
public sealed record PreflopRanges(
    IReadOnlyDictionary<Position, HandRange> Open,
    IReadOnlyDictionary<Position, HandRange> BigBlindCall)
{
    /// <summary>No restriction: every hand from every seat.</summary>
    public static PreflopRanges AnyHand { get; } = new(
        Enum.GetValues<Position>().ToDictionary(p => p, _ => HandRange.All),
        Enum.GetValues<Position>().ToDictionary(p => p, _ => HandRange.All));

    public bool CanOpen(Position seat, IReadOnlyList<Card> hole) =>
        Open.TryGetValue(seat, out var range) && range.Contains(hole);

    public bool CanCallInBigBlind(Position opener, IReadOnlyList<Card> hole) =>
        BigBlindCall.TryGetValue(opener, out var range) && range.Contains(hole);
}
