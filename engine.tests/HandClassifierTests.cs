using PokerDrills.Engine;
using static PokerDrills.Engine.Tests.TestHelpers;

namespace PokerDrills.Engine.Tests;

public class HandClassifierTests
{
    private static HandClass Classify(string hole, string board, ClassifierOptions? options = null) =>
        HandClassifier.Classify(Cards(hole), Cards(board), options).Class;

    private static DrawFlags Draws(string hole, string board) => HandClassifier.Classify(Cards(hole), Cards(board)).Draws;

    [Theory]
    // top pair and kickers (good kicker = J or better)
    [InlineData("AsKd", "Ah7c2d", HandClass.TopPairGoodKicker)]
    [InlineData("AsJd", "Ah7c2d", HandClass.TopPairGoodKicker)] // J is exactly the threshold
    [InlineData("AsTd", "Ah7c2d", HandClass.TopPairWeakKicker)] // T is one below
    [InlineData("KsQd", "Kh7c2d", HandClass.TopPairGoodKicker)]
    [InlineData("Ks9d", "Kh7c2d", HandClass.TopPairWeakKicker)]
    [InlineData("QsJd", "Qh9c4d", HandClass.TopPairGoodKicker)]
    [InlineData("QsTd", "Qh9c4d", HandClass.TopPairWeakKicker)]
    [InlineData("Qs9d", "Qh9c4d", HandClass.TwoPair)] // kicker pairs the board too
    // second / weak pairs
    [InlineData("Qs7d", "Kh7c2d", HandClass.SecondPair)]
    [InlineData("Qs2s", "Kh7c2d", HandClass.WeakPair)]
    [InlineData("9s8d", "9hKc4d2s", HandClass.SecondPair)] // turn overcard makes top pair second pair
    [InlineData("4s3d", "Kh9c4d2sJc", HandClass.WeakPair)] // river, fourth board rank
    // pocket pairs: overpair vs top pair vs set
    [InlineData("QsQd", "Jh7c2d", HandClass.Overpair)]
    [InlineData("AsAd", "Kh9c4d", HandClass.Overpair)]
    [InlineData("AsKd", "Kh9c4d", HandClass.TopPairGoodKicker)]
    [InlineData("KsKd", "Kh9c4d", HandClass.Set)]
    [InlineData("QsQd", "Kh7c2d", HandClass.SecondPair)] // between top and second board rank
    [InlineData("5s5d", "Kh7c2d", HandClass.WeakPair)] // below second board rank
    [InlineData("7s7d", "Kh7c2d", HandClass.Set)]
    [InlineData("JsJd", "Th7c2d3s", HandClass.Overpair)]
    // two pair
    [InlineData("Ks7d", "Kh7c2d", HandClass.TwoPair)]
    [InlineData("7s2s", "Kh7c2d", HandClass.TwoPair)]
    // air
    [InlineData("AsQd", "Kh7c2d", HandClass.Air)]
    // paired boards: board-only pairs do not count
    [InlineData("AsQd", "7h7c2d", HandClass.Air)]
    [InlineData("Ad2d", "7h7cKd", HandClass.Air)]
    [InlineData("Ks5d", "7h7cKd", HandClass.TopPairWeakKicker)]
    [InlineData("AsKd", "Kh5c5d", HandClass.TopPairGoodKicker)] // not two pair: 55 is the board's
    [InlineData("9s9d", "KhKc5d", HandClass.SecondPair)]
    [InlineData("3s3d", "Kh5c5d", HandClass.WeakPair)]
    [InlineData("AsAd", "KhKc5d", HandClass.Overpair)]
    [InlineData("Ah3c", "AdKs7h7c2d", HandClass.TopPairWeakKicker)]
    // trips vs set
    [InlineData("8s8d", "8hKc4d", HandClass.Set)]
    [InlineData("Ks8d", "8h8c4d", HandClass.Trips)]
    [InlineData("As7d", "7h7c2d", HandClass.Trips)]
    [InlineData("Js9d", "9h9cKd2s", HandClass.Trips)]
    [InlineData("AsKd", "7h7c7d", HandClass.Air)] // board trips are not hero's
    // full house and better
    [InlineData("Kd7s", "7h7cKc", HandClass.FullHousePlus)]
    [InlineData("5s5c", "KhKd5d", HandClass.FullHousePlus)] // set on a paired board
    [InlineData("QsQd", "7h7c7d", HandClass.FullHousePlus)]
    [InlineData("Ks7d", "KhKc7c", HandClass.FullHousePlus)]
    [InlineData("7s8d", "7h7c7d", HandClass.FullHousePlus)] // quads
    [InlineData("9h8h", "7h6h5hKc", HandClass.FullHousePlus)] // straight flush
    [InlineData("AsKd", "9h9c9d9s2c", HandClass.Air)] // board quads, hole card only a kicker
    [InlineData("AsKd", "9h9c9d5s5c", HandClass.Air)] // board full house
    // straights, including the wheel
    [InlineData("As2d", "3h4c5d", HandClass.Straight)]
    [InlineData("Ah9c", "2d3s4h5cKd", HandClass.Straight)]
    [InlineData("AsKd", "QhJcTd", HandClass.Straight)]
    [InlineData("Ts2d", "5h6c7d8s9h", HandClass.Straight)] // improves on the board straight
    [InlineData("AsKd", "5h6c7d8s9h", HandClass.Air)] // plays the board straight
    [InlineData("4s4d", "5h6c7d8s9h", HandClass.WeakPair)] // 4-8 is worse than the board's 5-9
    // flushes, including flush on the board
    [InlineData("AhKh", "2h7hJh", HandClass.Flush)]
    [InlineData("Ah7h", "7c2h9hKh", HandClass.Flush)] // flush beats the pair it also makes
    [InlineData("AsKd", "2h5h8hJhQh", HandClass.Air)] // board flush, no heart
    [InlineData("AhKd", "2h5h8hJhQh", HandClass.Flush)] // Ah improves the board flush
    [InlineData("2hKd", "4h5h8hJhQh", HandClass.Air)] // 2h is lower than every board heart
    public void Classifies_hand(string hole, string board, HandClass expected) =>
        Assert.Equal(expected, Classify(hole, board));

    [Fact]
    public void Good_kicker_threshold_is_configurable()
    {
        var tenIsGood = new ClassifierOptions { GoodKickerMinRank = Ranks.Ten };
        var queenIsGood = new ClassifierOptions { GoodKickerMinRank = Ranks.Queen };
        Assert.Equal(HandClass.TopPairGoodKicker, Classify("QsTd", "Qh9c4d", tenIsGood));
        Assert.Equal(HandClass.TopPairWeakKicker, Classify("AsJd", "Ah7c2d", queenIsGood));
    }

    [Theory]
    [InlineData("Ah5h", "Kh9h2c", DrawFlags.FlushDraw)]
    [InlineData("Ah2c", "Kh9h4h", DrawFlags.FlushDraw)] // one-card flush draw on a monotone flop
    [InlineData("AsKd", "2h5h8hJh", DrawFlags.None)] // four hearts on board, hero has none
    [InlineData("8s7d", "6h5c2d", DrawFlags.OpenEnded)]
    [InlineData("9s7d", "Jh8c5d", DrawFlags.OpenEnded)] // double gutshot counts as 8 outs
    [InlineData("7s8d", "5h6c7dKs", DrawFlags.OpenEnded)] // pair plus open-ender on the turn
    [InlineData("8s7d", "5h4c2d", DrawFlags.Gutshot)]
    [InlineData("AsKd", "QhJc5d", DrawFlags.Gutshot)]
    [InlineData("As2d", "3h4c9d", DrawFlags.Gutshot)] // wheel draw
    [InlineData("9h8h", "7h6c2h", DrawFlags.FlushDraw | DrawFlags.OpenEnded)]
    [InlineData("KhQh", "JhTh2c", DrawFlags.FlushDraw | DrawFlags.OpenEnded)]
    [InlineData("AsKd", "5h6c7d8s", DrawFlags.None)] // the board's own straight draw is not hero's
    [InlineData("KsQd", "Kh7c2d", DrawFlags.None)]
    [InlineData("AhKh", "2h7hJh", DrawFlags.None)] // already a flush
    [InlineData("As2d", "3h4c5d", DrawFlags.None)] // already a straight
    [InlineData("Ah6h", "Kh9h2c3sJd", DrawFlags.None)] // river: no draws
    public void Detects_draws(string hole, string board, DrawFlags expected) =>
        Assert.Equal(expected, Draws(hole, board));

    [Fact]
    public void Rejects_bad_input()
    {
        Assert.Throws<ArgumentException>(() => HandClassifier.Classify(Cards("AsKd"), Cards("AsQc2d")));
        Assert.Throws<ArgumentException>(() => HandClassifier.Classify(Cards("As"), Cards("Kh7c2d")));
        Assert.Throws<ArgumentException>(() => HandClassifier.Classify(Cards("AsKd"), Cards("Kh7c")));
    }

    [Fact]
    public void Random_deals_never_throw_and_river_has_no_draws()
    {
        var deck = new Deck(new Rng(5));
        for (var i = 0; i < 5000; i++)
        {
            deck.Reset();
            var hole = deck.Deal(2);
            var board = deck.Deal(3 + i % 3);
            var result = HandClassifier.Classify(hole, board);
            if (board.Length == 5) Assert.Equal(DrawFlags.None, result.Draws);
            // a made hand that uses a hole card is never weaker than the evaluator's pair categories imply
            if (result.Class >= HandClass.Straight)
            {
                Assert.True(HandEvaluator.EvaluateBest(hole.Concat(board).ToArray()).Category >= HandCategory.Straight);
            }
        }
    }
}
