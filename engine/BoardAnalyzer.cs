namespace PokerDrills.Engine;

[Flags]
public enum BoardFlags
{
    None = 0,
    Paired = 1,
    Monotone = 2,
    TwoTone = 4,
    Rainbow = 8,
    FourToFlush = 16,
    FourToStraight = 32,
    Dry = 64,
}

public readonly record struct BoardTexture(BoardFlags Flags, int HighCard)
{
    public bool Has(BoardFlags flag) => (Flags & flag) == flag;
}

public static class BoardAnalyzer
{
    public static IReadOnlyList<BoardFlags> AllFlags { get; } =
        Enum.GetValues<BoardFlags>().Where(f => f != BoardFlags.None).ToArray();

    /// <summary>
    /// Suit texture uses the whole visible board: 3+ of a suit = Monotone, max 2 = TwoTone,
    /// all different = Rainbow. Exactly one of the three is set.
    /// </summary>
    public static BoardTexture Analyze(IReadOnlyList<Card> board)
    {
        if (board.Count is < 3 or > 5) throw new ArgumentException("Board needs 3 to 5 cards", nameof(board));
        CardList.EnsureDistinct(board);

        var flags = BoardFlags.None;
        if (board.GroupBy(c => c.Rank).Any(g => g.Count() >= 2)) flags |= BoardFlags.Paired;

        var maxSuit = board.GroupBy(c => c.Suit).Max(g => g.Count());
        flags |= maxSuit switch
        {
            >= 3 => BoardFlags.Monotone,
            2 => BoardFlags.TwoTone,
            _ => BoardFlags.Rainbow,
        };
        if (maxSuit >= 4) flags |= BoardFlags.FourToFlush;

        var connected = RankMask.MaxInAnyWindow(RankMask.Of(board));
        if (connected >= 4) flags |= BoardFlags.FourToStraight;

        if ((flags & BoardFlags.Rainbow) != 0 && (flags & BoardFlags.Paired) == 0 && connected <= 2)
        {
            flags |= BoardFlags.Dry;
        }

        return new BoardTexture(flags, board.Max(c => c.Rank));
    }

    public static IReadOnlyList<string> Names(BoardFlags flags) =>
        AllFlags.Where(f => (flags & f) != 0).Select(f => f.ToString()).ToArray();
}
