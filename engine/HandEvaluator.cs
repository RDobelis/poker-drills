namespace PokerDrills.Engine;

public enum HandCategory { HighCard, Pair, TwoPair, ThreeOfAKind, Straight, Flush, FullHouse, FourOfAKind, StraightFlush }

/// <summary>
/// A 5-card hand value. <see cref="Score"/> packs the category (bits 20+) and up to five
/// tiebreak ranks (4 bits each), so a higher score is a better hand and equal scores tie.
/// </summary>
public readonly record struct HandValue(HandCategory Category, int Score) : IComparable<HandValue>
{
    public int CompareTo(HandValue other) => Score.CompareTo(other.Score);

    /// <summary>Tiebreak rank at position 0..4 (0 = most significant, e.g. the quad or trips rank).</summary>
    public int Tiebreak(int position) => (Score >> (16 - 4 * position)) & 0xF;

    public static bool operator >(HandValue a, HandValue b) => a.Score > b.Score;
    public static bool operator <(HandValue a, HandValue b) => a.Score < b.Score;
    public static bool operator >=(HandValue a, HandValue b) => a.Score >= b.Score;
    public static bool operator <=(HandValue a, HandValue b) => a.Score <= b.Score;
}

public static class HandEvaluator
{
    public static HandValue Evaluate5(IReadOnlyList<Card> cards)
    {
        if (cards.Count != 5) throw new ArgumentException("Exactly 5 cards required", nameof(cards));
        Span<Card> span = stackalloc Card[5];
        for (var i = 0; i < 5; i++) span[i] = cards[i];
        return Evaluate5(span);
    }

    public static HandValue Evaluate5(ReadOnlySpan<Card> cards)
    {
        if (cards.Length != 5) throw new ArgumentException("Exactly 5 cards required", nameof(cards));

        Span<int> counts = stackalloc int[15];
        var flush = true;
        var mask = 0;
        foreach (var c in cards)
        {
            counts[c.Rank]++;
            mask |= RankMask.Bit(c.Rank);
            if (c.Suit != cards[0].Suit) flush = false;
        }

        // Ranks ordered by (count desc, rank desc): the standard tiebreak order.
        Span<int> order = stackalloc int[5];
        var distinct = 0;
        for (var n = 4; n >= 1; n--)
        {
            for (var r = Ranks.Ace; r >= Ranks.Two; r--)
            {
                if (counts[r] == n) order[distinct++] = r;
            }
        }

        var first = counts[order[0]];
        var second = distinct > 1 ? counts[order[1]] : 0;
        var straightHigh = distinct == 5 ? RankMask.HighestStraight(mask) : 0;

        if (straightHigh > 0 && flush) return Make(HandCategory.StraightFlush, [straightHigh]);
        if (first == 4) return Make(HandCategory.FourOfAKind, order[..distinct]);
        if (first == 3 && second == 2) return Make(HandCategory.FullHouse, order[..distinct]);
        if (flush) return Make(HandCategory.Flush, order[..distinct]);
        if (straightHigh > 0) return Make(HandCategory.Straight, [straightHigh]);
        if (first == 3) return Make(HandCategory.ThreeOfAKind, order[..distinct]);
        if (first == 2 && second == 2) return Make(HandCategory.TwoPair, order[..distinct]);
        if (first == 2) return Make(HandCategory.Pair, order[..distinct]);
        return Make(HandCategory.HighCard, order[..distinct]);
    }

    /// <summary>Best 5-card hand out of 5 to 7 cards.</summary>
    public static HandValue EvaluateBest(IReadOnlyList<Card> cards)
    {
        if (cards.Count is < 5 or > 7) throw new ArgumentException("5 to 7 cards required", nameof(cards));
        CardList.EnsureDistinct(cards);
        HandValue? best = null;
        Span<Card> five = stackalloc Card[5];
        foreach (var combo in Combinations(cards.Count, 5))
        {
            for (var i = 0; i < 5; i++) five[i] = cards[combo[i]];
            var value = Evaluate5(five);
            if (best is null || value > best.Value) best = value;
        }
        return best!.Value;
    }

    /// <summary>
    /// All k-subsets of 0..n-1 as sorted index arrays, in lexicographic order.
    /// The same array instance is reused between iterations; copy it to keep it.
    /// </summary>
    public static IEnumerable<int[]> Combinations(int n, int k)
    {
        if (k < 0 || k > n) yield break;
        var idx = new int[k];
        for (var i = 0; i < k; i++) idx[i] = i;
        while (true)
        {
            yield return idx;
            var p = k - 1;
            while (p >= 0 && idx[p] == n - k + p) p--;
            if (p < 0) yield break;
            idx[p]++;
            for (var j = p + 1; j < k; j++) idx[j] = idx[j - 1] + 1;
        }
    }

    private static HandValue Make(HandCategory category, ReadOnlySpan<int> tiebreaks)
    {
        var score = (int)category << 20;
        for (var i = 0; i < tiebreaks.Length; i++) score |= tiebreaks[i] << (16 - 4 * i);
        return new HandValue(category, score);
    }
}
