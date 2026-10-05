using PokerDrills.Engine;
using static PokerDrills.Engine.Tests.TestHelpers;

namespace PokerDrills.Engine.Tests;

public class HandEvaluatorTests
{
    private static HandValue Eval(string cards) => HandEvaluator.Evaluate5(Cards(cards));

    [Theory]
    [InlineData("AsKsQsJsTs", HandCategory.StraightFlush)]
    [InlineData("5d4d3d2dAd", HandCategory.StraightFlush)] // steel wheel
    [InlineData("9c9d9h9s2c", HandCategory.FourOfAKind)]
    [InlineData("9c9d9h2s2c", HandCategory.FullHouse)]
    [InlineData("Ac9c7c4c2c", HandCategory.Flush)]
    [InlineData("As2d3h4c5s", HandCategory.Straight)] // wheel
    [InlineData("AsKdQhJcTs", HandCategory.Straight)] // broadway
    [InlineData("9c9d9h5s2c", HandCategory.ThreeOfAKind)]
    [InlineData("9c9d5h5s2c", HandCategory.TwoPair)]
    [InlineData("9c9d7h5s2c", HandCategory.Pair)]
    [InlineData("Kc9d7h5s2c", HandCategory.HighCard)]
    [InlineData("QsKdAh2c3s", HandCategory.HighCard)] // straights do not wrap around
    public void Evaluate5_finds_category(string cards, HandCategory expected) =>
        Assert.Equal(expected, Eval(cards).Category);

    [Fact]
    public void Wheel_is_the_lowest_straight() => Assert.True(Eval("As2d3h4c5s") < Eval("2s3d4h5c6s"));

    [Fact]
    public void Wheel_straight_flush_is_lowest_straight_flush() => Assert.True(Eval("Ah2h3h4h5h") < Eval("2c3c4c5c6c"));

    [Fact]
    public void Pair_kicker_decides() => Assert.True(Eval("AsAdKc7h2s") > Eval("AhAcQd7s2d"));

    [Fact]
    public void Two_pair_compares_second_pair_then_kicker()
    {
        Assert.True(Eval("KsKd9c9d2h") > Eval("KhKc8s8dAh"));
        Assert.True(Eval("KsKd9c9dAh") > Eval("KhKc9s9hQh"));
    }

    [Fact]
    public void Flush_compares_all_five_cards() => Assert.True(Eval("AhJh9h6h3h") > Eval("AdJd9d6d2d"));

    [Fact]
    public void Full_house_compares_trips_first() => Assert.True(Eval("3s3d3h2c2d") > Eval("2s2h2cAcAd"));

    [Fact]
    public void Quads_beat_full_house_and_straight_flush_beats_quads()
    {
        Assert.True(Eval("2s2d2h2cAs") > Eval("AcAdAhKcKd"));
        Assert.True(Eval("6s5s4s3s2s") > Eval("AcAdAhAsKd"));
    }

    [Fact]
    public void Identical_ranks_in_different_suits_tie() => Assert.Equal(Eval("AsKdQh9c7s").Score, Eval("AhKcQd9s7c").Score);

    [Fact]
    public void EvaluateBest_picks_best_five_of_seven()
    {
        var best = HandEvaluator.EvaluateBest(Cards("AhKh QhJhTh2c3d"));
        Assert.Equal(HandCategory.StraightFlush, best.Category);
        Assert.Equal(Ranks.Ace, best.Tiebreak(0));
    }

    [Fact]
    public void EvaluateBest_full_house_uses_best_trips_and_pair()
    {
        var best = HandEvaluator.EvaluateBest(Cards("7c7d 7h2s2cKdKs"));
        Assert.Equal(HandCategory.FullHouse, best.Category);
        Assert.Equal(Ranks.Seven, best.Tiebreak(0));
        Assert.Equal(Ranks.King, best.Tiebreak(1));
    }

    [Fact]
    public void EvaluateBest_six_high_straight_beats_wheel_with_same_cards()
    {
        var best = HandEvaluator.EvaluateBest(Cards("As6d 2h3c4s5dKh"));
        Assert.Equal(HandCategory.Straight, best.Category);
        Assert.Equal(Ranks.Six, best.Tiebreak(0));
    }

    [Fact]
    public void Combinations_counts_and_uniqueness()
    {
        Assert.Equal(21, HandEvaluator.Combinations(7, 5).Select(c => string.Join(",", c)).Distinct().Count());
        Assert.Equal(6, HandEvaluator.Combinations(6, 5).Count());
        Assert.Single(HandEvaluator.Combinations(5, 5));
    }

    [Fact]
    public void Duplicate_cards_are_rejected() =>
        Assert.Throws<ArgumentException>(() => HandEvaluator.EvaluateBest(Cards("AsAs2c3d4h")));
}
