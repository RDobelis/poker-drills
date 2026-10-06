using PokerDrills.Engine;
using static PokerDrills.Engine.Tests.TestHelpers;

namespace PokerDrills.Engine.Tests;

public class RangeTests
{
    [Theory]
    [InlineData("TT", 6)]
    [InlineData("22+", 78)] // 13 pairs
    [InlineData("77-99", 18)]
    [InlineData("AKs", 4)]
    [InlineData("AKo", 12)]
    [InlineData("AK", 16)]
    [InlineData("A2s+", 48)] // A2s..AKs
    [InlineData("ATo+", 48)] // ATo, AJo, AQo, AKo
    [InlineData("K9s-K6s", 16)]
    [InlineData("A2s-AQs", 44)]
    [InlineData("22+, AK, AKs", 94)] // overlaps are counted once
    public void Parse_counts_combos(string text, int combos) => Assert.Equal(combos, HandRange.Parse(text).Combos);

    [Fact]
    public void All_covers_every_starting_hand() => Assert.Equal(1326, HandRange.All.Combos);

    [Theory]
    [InlineData("A2s+", "AhKh", true)]
    [InlineData("A2s+", "AhKd", false)]
    [InlineData("KTo+", "KdTc", true)]
    [InlineData("KTo+", "Kd9c", false)]
    [InlineData("22-99", "9c9d", true)]
    [InlineData("22-99", "TcTd", false)]
    [InlineData("T9s", "9hTh", true)] // card order in the hand does not matter
    public void Contains(string range, string hole, bool expected) =>
        Assert.Equal(expected, HandRange.Parse(range).Contains(Cards(hole)));

    [Theory]
    [InlineData("")]
    [InlineData("A2x")]
    [InlineData("9Ks")] // lower card first
    [InlineData("AAs")] // suffix on a pair
    [InlineData("K9s-Q6s")] // different first card
    [InlineData("22-A9s")] // mixed kinds
    [InlineData("1Ks")]
    public void Parse_rejects_bad_notation(string text) => Assert.Throws<FormatException>(() => HandRange.Parse(text));

    [Fact]
    public void Real_ranges_widen_by_position_and_exclude_junk()
    {
        var r = TestHelpers.Content.Ranges;
        var seats = new[] { Position.UTG, Position.MP, Position.CO, Position.BTN };
        for (var i = 1; i < seats.Length; i++)
        {
            Assert.True(r.Open[seats[i]].Combos > r.Open[seats[i - 1]].Combos, $"open {seats[i]}");
            Assert.True(r.BigBlindCall[seats[i]].Combos > r.BigBlindCall[seats[i - 1]].Combos, $"bb call vs {seats[i]}");
        }

        foreach (var seat in seats)
        {
            Assert.False(r.CanOpen(seat, Cards("9s3d")), $"93o opened from {seat}");
            Assert.False(r.CanCallInBigBlind(seat, Cards("9s3d")), $"93o called vs {seat}");
            Assert.True(r.CanOpen(seat, Cards("AsAd")));
            Assert.False(r.CanCallInBigBlind(seat, Cards("AsAd")), "AA 3-bets, it does not flat");
        }
        Assert.True(r.CanOpen(Position.BTN, Cards("Kd8c")));
        Assert.False(r.CanOpen(Position.BTN, Cards("Kd4c")));
    }
}
