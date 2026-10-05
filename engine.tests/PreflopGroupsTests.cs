using PokerDrills.Engine;
using static PokerDrills.Engine.Tests.TestHelpers;

namespace PokerDrills.Engine.Tests;

public class PreflopGroupsTests
{
    [Theory]
    [InlineData("AsAd", HandGroup.Premium)]
    [InlineData("KsKd", HandGroup.Premium)]
    [InlineData("QsQd", HandGroup.Premium)]
    [InlineData("AsKs", HandGroup.Premium)]
    [InlineData("AsKd", HandGroup.Premium)]
    [InlineData("JsJd", HandGroup.Strong)]
    [InlineData("TsTd", HandGroup.Strong)]
    [InlineData("AsQd", HandGroup.Strong)]
    [InlineData("AsQs", HandGroup.Strong)]
    [InlineData("AsJs", HandGroup.Strong)]
    [InlineData("KsQs", HandGroup.Strong)]
    [InlineData("AsJd", HandGroup.Playable)] // AJo
    [InlineData("AsTd", HandGroup.Playable)] // ATo
    [InlineData("KsQd", HandGroup.Playable)] // KQo
    [InlineData("9s9d", HandGroup.Playable)]
    [InlineData("2s2d", HandGroup.Playable)]
    [InlineData("KsJs", HandGroup.Playable)] // suited broadway
    [InlineData("QsTs", HandGroup.Playable)]
    [InlineData("JsTs", HandGroup.Playable)]
    [InlineData("AsTs", HandGroup.Playable)]
    [InlineData("5s4s", HandGroup.Playable)] // lowest suited connector
    [InlineData("Ts9s", HandGroup.Playable)]
    [InlineData("4s3s", HandGroup.Trash)] // below 54s
    [InlineData("5s4d", HandGroup.Trash)] // offsuit connector
    [InlineData("As9d", HandGroup.Trash)]
    [InlineData("KsJd", HandGroup.Trash)] // KJo
    [InlineData("As5s", HandGroup.Trash)]
    [InlineData("6s4s", HandGroup.Trash)] // one-gapper
    [InlineData("Ks9s", HandGroup.Trash)]
    [InlineData("7s2d", HandGroup.Trash)]
    public void Groups(string hole, HandGroup expected) => Assert.Equal(expected, PreflopGroups.Classify(Cards(hole)));

    [Fact]
    public void Group_combo_counts_over_all_1326_hands()
    {
        var counts = new Dictionary<HandGroup, int>();
        foreach (var combo in HandEvaluator.Combinations(52, 2))
        {
            var g = PreflopGroups.Classify([Card.FromIndex(combo[0]), Card.FromIndex(combo[1])]);
            counts[g] = counts.GetValueOrDefault(g) + 1;
        }
        Assert.Equal(34, counts[HandGroup.Premium]); // QQ+ 18, AK 16
        Assert.Equal(36, counts[HandGroup.Strong]); // TT-JJ 12, AQ 16, AJs 4, KQs 4
        Assert.Equal(1326, counts.Values.Sum());
    }

    [Theory]
    [InlineData("AsKs", "AKs")]
    [InlineData("KdAs", "AKo")]
    [InlineData("7c7d", "77")]
    public void Notation(string hole, string expected) => Assert.Equal(expected, PreflopGroups.Notation(Cards(hole)));
}
