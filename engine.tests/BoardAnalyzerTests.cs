using PokerDrills.Engine;
using static PokerDrills.Engine.Tests.TestHelpers;

namespace PokerDrills.Engine.Tests;

public class BoardAnalyzerTests
{
    private static BoardTexture Analyze(string board) => BoardAnalyzer.Analyze(Cards(board));

    [Theory]
    [InlineData("Kh7c7d", BoardFlags.Paired, true)]
    [InlineData("Kh7c2d9s9h", BoardFlags.Paired, true)]
    [InlineData("Kh7c2d", BoardFlags.Paired, false)]
    [InlineData("Kh7h2h", BoardFlags.Monotone, true)]
    [InlineData("Kh7h2dQh", BoardFlags.Monotone, true)] // 3+ of a suit
    [InlineData("Kh7h2d", BoardFlags.Monotone, false)]
    [InlineData("Kh7h2d", BoardFlags.TwoTone, true)]
    [InlineData("Kh7c2d", BoardFlags.TwoTone, false)]
    [InlineData("Kh7h2h", BoardFlags.TwoTone, false)]
    [InlineData("Kh7c2d", BoardFlags.Rainbow, true)]
    [InlineData("Kh7c2dQs", BoardFlags.Rainbow, true)]
    [InlineData("Kh7h2d", BoardFlags.Rainbow, false)]
    [InlineData("Kh7c2d9s3h", BoardFlags.Rainbow, false)] // five cards always repeat a suit
    [InlineData("Kh7h2h9h", BoardFlags.FourToFlush, true)]
    [InlineData("Kh7h2h9hAh", BoardFlags.FourToFlush, true)]
    [InlineData("Kh7h2h9c", BoardFlags.FourToFlush, false)]
    [InlineData("5h6c7d8s", BoardFlags.FourToStraight, true)]
    [InlineData("Ah2c3d4s", BoardFlags.FourToStraight, true)] // ace low
    [InlineData("AhKcQdJs", BoardFlags.FourToStraight, true)] // ace high
    [InlineData("9h5c7d8sKd", BoardFlags.FourToStraight, true)] // gapped: 5-7-8-9
    [InlineData("Ah2c3dKs", BoardFlags.FourToStraight, false)]
    [InlineData("5h6c7dKs", BoardFlags.FourToStraight, false)]
    [InlineData("5h5c6d7s", BoardFlags.FourToStraight, false)] // a pair counts once
    [InlineData("4h6c8dTs", BoardFlags.FourToStraight, false)] // spread over 7 ranks
    [InlineData("Kh7c2d", BoardFlags.Dry, true)]
    [InlineData("QhJc3d", BoardFlags.Dry, true)]
    [InlineData("Kh9c7d2s", BoardFlags.Dry, true)]
    [InlineData("Kh7c2h", BoardFlags.Dry, false)] // two-tone
    [InlineData("Kh7c7d", BoardFlags.Dry, false)] // paired
    [InlineData("9h8c7d", BoardFlags.Dry, false)] // three in a 5-rank window
    [InlineData("Ah2c5d", BoardFlags.Dry, false)] // the ace connects low
    [InlineData("AhQcTd", BoardFlags.Dry, false)] // the ace connects high
    public void Flag(string board, BoardFlags flag, bool expected) => Assert.Equal(expected, Analyze(board).Has(flag));

    [Theory]
    [InlineData("Kh7c2d", Ranks.King)]
    [InlineData("2h3c4d", Ranks.Four)]
    [InlineData("Ah2c3d9s", Ranks.Ace)]
    public void HighCard_is_top_board_rank(string board, int expected) => Assert.Equal(expected, Analyze(board).HighCard);

    [Fact]
    public void Exactly_one_suit_texture_flag_on_every_board()
    {
        var deck = new Deck(new Rng(11));
        for (var i = 0; i < 3000; i++)
        {
            deck.Reset();
            var flags = BoardAnalyzer.Analyze(deck.Deal(3 + i % 3)).Flags;
            var count = new[] { BoardFlags.Monotone, BoardFlags.TwoTone, BoardFlags.Rainbow }.Count(f => (flags & f) != 0);
            Assert.Equal(1, count);
            if ((flags & BoardFlags.Dry) != 0) Assert.True((flags & BoardFlags.Rainbow) != 0 && (flags & BoardFlags.Paired) == 0);
        }
    }

    [Fact]
    public void Rejects_bad_board_sizes()
    {
        Assert.Throws<ArgumentException>(() => BoardAnalyzer.Analyze(Cards("Kh7c")));
        Assert.Throws<ArgumentException>(() => BoardAnalyzer.Analyze(Cards("Kh7c2d3s4h5c")));
    }
}
