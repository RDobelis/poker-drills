namespace PokerDrills.Engine;

public enum Suit { Clubs = 0, Diamonds = 1, Hearts = 2, Spades = 3 }

/// <summary>Ranks are plain ints 2..14. The ace is 14 and also plays low in A-2-3-4-5.</summary>
public static class Ranks
{
    public const int Two = 2, Three = 3, Four = 4, Five = 5, Six = 6, Seven = 7, Eight = 8, Nine = 9,
        Ten = 10, Jack = 11, Queen = 12, King = 13, Ace = 14;

    private const string Chars = "23456789TJQKA";

    public static char ToChar(int rank) => rank is >= Two and <= Ace
        ? Chars[rank - Two]
        : throw new ArgumentOutOfRangeException(nameof(rank), rank, "Rank must be 2..14");

    public static int Parse(char c)
    {
        var i = Chars.IndexOf(char.ToUpperInvariant(c));
        return i >= 0 ? i + Two : throw new FormatException($"Unknown rank '{c}'");
    }
}

public readonly record struct Card(int Rank, Suit Suit)
{
    private const string SuitChars = "cdhs";

    public int Rank { get; } = Rank is >= Ranks.Two and <= Ranks.Ace
        ? Rank
        : throw new ArgumentOutOfRangeException(nameof(Rank), Rank, "Rank must be 2..14");

    /// <summary>0..51, unique per card.</summary>
    public int Index => (Rank - Ranks.Two) * 4 + (int)Suit;

    public static Card FromIndex(int index) => index is >= 0 and < 52
        ? new Card(index / 4 + Ranks.Two, (Suit)(index % 4))
        : throw new ArgumentOutOfRangeException(nameof(index));

    /// <summary>Parses "As", "Td", "9c".</summary>
    public static Card Parse(string text)
    {
        if (text is not { Length: 2 }) throw new FormatException($"Card must be 2 characters, got '{text}'");
        var suit = SuitChars.IndexOf(char.ToLowerInvariant(text[1]));
        if (suit < 0) throw new FormatException($"Unknown suit in '{text}'");
        return new Card(Ranks.Parse(text[0]), (Suit)suit);
    }

    /// <summary>Parses "AsKd", "As Kd" or "As,Kd". Empty input gives an empty array.</summary>
    public static Card[] ParseMany(string text)
    {
        var compact = new string(text.Where(c => !char.IsWhiteSpace(c) && c != ',').ToArray());
        if (compact.Length % 2 != 0) throw new FormatException($"Odd number of characters in '{text}'");
        var cards = new Card[compact.Length / 2];
        for (var i = 0; i < cards.Length; i++) cards[i] = Parse(compact.Substring(i * 2, 2));
        return cards;
    }

    public override string ToString() => $"{Ranks.ToChar(Rank)}{SuitChars[(int)Suit]}";
}

public static class CardList
{
    public static void EnsureDistinct(IEnumerable<Card> cards)
    {
        var seen = new HashSet<Card>();
        foreach (var card in cards)
        {
            if (!seen.Add(card)) throw new ArgumentException($"Duplicate card {card}");
        }
    }

    public static string Format(IEnumerable<Card> cards) => string.Join(" ", cards);
}

/// <summary>
/// Rank bitmasks: bit r is set for rank r; an ace also sets bit 1 so wheel windows work.
/// </summary>
public static class RankMask
{
    public static int Bit(int rank) => rank == Ranks.Ace ? (1 << Ranks.Ace) | (1 << 1) : 1 << rank;

    public static int Of(IEnumerable<Card> cards)
    {
        var mask = 0;
        foreach (var c in cards) mask |= Bit(c.Rank);
        return mask;
    }

    /// <summary>Five consecutive ranks ending at <paramref name="high"/> (5..14; 5 = the wheel).</summary>
    public static int Window(int high) => 0b11111 << (high - 4);

    /// <summary>Highest straight in the mask, or 0.</summary>
    public static int HighestStraight(int mask)
    {
        for (var high = Ranks.Ace; high >= Ranks.Five; high--)
        {
            var w = Window(high);
            if ((mask & w) == w) return high;
        }
        return 0;
    }

    /// <summary>Most distinct ranks found inside any 5-rank window.</summary>
    public static int MaxInAnyWindow(int mask)
    {
        var best = 0;
        for (var high = Ranks.Five; high <= Ranks.Ace; high++)
        {
            best = Math.Max(best, System.Numerics.BitOperations.PopCount((uint)(mask & Window(high))));
        }
        return best;
    }
}
