namespace PokerDrills.Engine;

/// <summary>Hero hand strength, weakest to strongest. Order matters: rules use min/max ranges.</summary>
public enum HandClass
{
    Air,
    WeakPair,
    SecondPair,
    TopPairWeakKicker,
    TopPairGoodKicker,
    Overpair,
    TwoPair,
    Set,
    Trips,
    Straight,
    Flush,
    FullHousePlus,
}

[Flags]
public enum DrawFlags
{
    None = 0,
    FlushDraw = 1,
    OpenEnded = 2,
    Gutshot = 4,
}

public sealed record ClassifierOptions
{
    public static ClassifierOptions Default { get; } = new();

    /// <summary>Top pair has a good kicker when the other hole card is at least this rank (default J).</summary>
    public int GoodKickerMinRank { get; init; } = Ranks.Jack;
}

public readonly record struct HandClassification(HandClass Class, DrawFlags Draws);

/// <summary>
/// Classifies hero's postflop hand. Only hands that need a hole card count: a pair, trips or
/// straight made by the board alone is ignored. See PLAN.md for the exact definitions.
/// </summary>
public static class HandClassifier
{
    public static HandClassification Classify(IReadOnlyList<Card> hole, IReadOnlyList<Card> board, ClassifierOptions? options = null)
    {
        if (hole.Count != 2) throw new ArgumentException("Hero needs exactly 2 hole cards", nameof(hole));
        if (board.Count is < 3 or > 5) throw new ArgumentException("Board needs 3 to 5 cards", nameof(board));
        CardList.EnsureDistinct(hole.Concat(board));

        var handClass = ClassifyMadeHand(hole, board, options ?? ClassifierOptions.Default);
        var draws = board.Count < 5 ? DetectDraws(hole, board, handClass) : DrawFlags.None;
        return new HandClassification(handClass, draws);
    }

    private static HandClass ClassifyMadeHand(IReadOnlyList<Card> hole, IReadOnlyList<Card> board, ClassifierOptions options)
    {
        if (BestFiveCardHand(hole, board) is { } five)
        {
            return five.Category switch
            {
                HandCategory.Straight => HandClass.Straight,
                HandCategory.Flush => HandClass.Flush,
                _ => HandClass.FullHousePlus, // full house, quads, straight flush
            };
        }
        return ClassifyByRanks(hole, board, options);
    }

    /// <summary>
    /// Best straight-or-better that uses a hole card (for quads: a hole card is one of the four).
    /// With a full board it must beat the board on its own, otherwise hero just plays the board.
    /// </summary>
    private static HandValue? BestFiveCardHand(IReadOnlyList<Card> hole, IReadOnlyList<Card> board)
    {
        var cards = hole.Concat(board).ToArray(); // hole cards are indices 0 and 1
        HandValue? best = null;
        Span<Card> five = stackalloc Card[5];
        foreach (var combo in HandEvaluator.Combinations(cards.Length, 5))
        {
            if (combo[0] > 1) continue; // sorted indices: no hole card in this combo
            for (var i = 0; i < 5; i++) five[i] = cards[combo[i]];
            var value = HandEvaluator.Evaluate5(five);
            if (value.Category < HandCategory.Straight) continue;
            if (value.Category == HandCategory.FourOfAKind && hole.All(h => h.Rank != value.Tiebreak(0))) continue;
            if (best is null || value > best.Value) best = value;
        }

        if (best is null) return null;
        if (board.Count == 5 && best.Value <= HandEvaluator.Evaluate5(board)) return null;
        return best;
    }

    private static HandClass ClassifyByRanks(IReadOnlyList<Card> hole, IReadOnlyList<Card> board, ClassifierOptions options)
    {
        Span<int> onBoard = stackalloc int[15];
        foreach (var b in board) onBoard[b.Rank]++;

        int top = 0, second = 0;
        for (var r = Ranks.Ace; r >= Ranks.Two; r--)
        {
            if (onBoard[r] == 0) continue;
            if (top == 0) top = r;
            else { second = r; break; }
        }

        var h1 = hole[0].Rank;
        var h2 = hole[1].Rank;

        if (h1 == h2)
        {
            if (onBoard[h1] >= 2) return HandClass.FullHousePlus; // quads; normally caught above
            if (onBoard[h1] == 1) return HandClass.Set;
            if (h1 > top) return HandClass.Overpair;
            if (second != 0 && h1 > second) return HandClass.SecondPair;
            return HandClass.WeakPair;
        }

        int c1 = onBoard[h1], c2 = onBoard[h2];
        if (c1 >= 3 || c2 >= 3) return HandClass.FullHousePlus; // quads; normally caught above
        if (c1 == 2 || c2 == 2) return HandClass.Trips;
        if (c1 == 1 && c2 == 1) return HandClass.TwoPair;
        if (c1 == 0 && c2 == 0) return HandClass.Air;

        var paired = c1 == 1 ? h1 : h2;
        var kicker = c1 == 1 ? h2 : h1;
        if (paired == top)
        {
            return kicker >= options.GoodKickerMinRank ? HandClass.TopPairGoodKicker : HandClass.TopPairWeakKicker;
        }
        return paired == second ? HandClass.SecondPair : HandClass.WeakPair;
    }

    private static DrawFlags DetectDraws(IReadOnlyList<Card> hole, IReadOnlyList<Card> board, HandClass handClass)
    {
        var flags = DrawFlags.None;

        if (handClass < HandClass.Flush)
        {
            for (var s = Suit.Clubs; s <= Suit.Spades; s++)
            {
                var suit = s;
                var inHole = hole.Count(c => c.Suit == suit);
                var total = inHole + board.Count(c => c.Suit == suit);
                if (inHole >= 1 && total == 4) flags |= DrawFlags.FlushDraw;
            }
        }

        if (handClass < HandClass.Straight)
        {
            var heroMask = RankMask.Of(hole.Concat(board));
            var boardMask = RankMask.Of(board);
            var outRanks = 0;
            for (var r = Ranks.Two; r <= Ranks.Ace; r++)
            {
                var bit = RankMask.Bit(r);
                if ((heroMask & bit) != 0) continue;
                if (HasStraightNeedingHoleCard(heroMask | bit, boardMask | bit)) outRanks++;
            }
            if (outRanks >= 2) flags |= DrawFlags.OpenEnded;
            else if (outRanks == 1) flags |= DrawFlags.Gutshot;
        }

        return flags;
    }

    private static bool HasStraightNeedingHoleCard(int heroMask, int boardMask)
    {
        for (var high = Ranks.Five; high <= Ranks.Ace; high++)
        {
            var w = RankMask.Window(high);
            if ((heroMask & w) == w && (boardMask & w) != w) return true;
        }
        return false;
    }
}
