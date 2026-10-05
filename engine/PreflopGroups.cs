namespace PokerDrills.Engine;

public enum HandGroup { Trash, Playable, Strong, Premium }

public static class PreflopGroups
{
    /// <summary>
    /// Premium QQ+, AK. Strong TT-JJ, AQ, AJs+, KQs. Playable 22-99, suited broadways,
    /// suited connectors 54s+, ATo-AJo, KQo. Everything else is Trash. Checked top-down.
    /// </summary>
    public static HandGroup Classify(IReadOnlyList<Card> hole)
    {
        if (hole.Count != 2) throw new ArgumentException("Exactly 2 hole cards required", nameof(hole));
        CardList.EnsureDistinct(hole);

        var hi = Math.Max(hole[0].Rank, hole[1].Rank);
        var lo = Math.Min(hole[0].Rank, hole[1].Rank);
        var suited = hole[0].Suit == hole[1].Suit;
        var pair = hi == lo;

        if ((pair && hi >= Ranks.Queen) || (hi == Ranks.Ace && lo == Ranks.King)) return HandGroup.Premium;

        if ((pair && hi >= Ranks.Ten)
            || (hi == Ranks.Ace && lo == Ranks.Queen)
            || (suited && hi == Ranks.Ace && lo == Ranks.Jack)
            || (suited && hi == Ranks.King && lo == Ranks.Queen))
        {
            return HandGroup.Strong;
        }

        if (pair // 22-99; bigger pairs returned above
            || (suited && lo >= Ranks.Ten) // suited broadways
            || (suited && hi - lo == 1 && lo >= Ranks.Four) // 54s+
            || (!suited && hi == Ranks.Ace && lo >= Ranks.Ten) // ATo-AJo
            || (!suited && hi == Ranks.King && lo == Ranks.Queen))
        {
            return HandGroup.Playable;
        }

        return HandGroup.Trash;
    }

    /// <summary>"AKs", "QQ", "T9o".</summary>
    public static string Notation(IReadOnlyList<Card> hole)
    {
        var hi = Math.Max(hole[0].Rank, hole[1].Rank);
        var lo = Math.Min(hole[0].Rank, hole[1].Rank);
        var text = $"{Ranks.ToChar(hi)}{Ranks.ToChar(lo)}";
        if (hi == lo) return text;
        return text + (hole[0].Suit == hole[1].Suit ? "s" : "o");
    }
}
