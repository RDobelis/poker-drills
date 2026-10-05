using PokerDrills.Engine;
using static PokerDrills.Engine.Tests.TestHelpers;

namespace PokerDrills.Engine.Tests;

public class CardsAndRngTests
{
    [Theory]
    [InlineData("As", Ranks.Ace, Suit.Spades)]
    [InlineData("Td", Ranks.Ten, Suit.Diamonds)]
    [InlineData("2c", Ranks.Two, Suit.Clubs)]
    [InlineData("kh", Ranks.King, Suit.Hearts)]
    public void Parse_reads_rank_and_suit(string text, int rank, Suit suit)
    {
        var card = Card.Parse(text);
        Assert.Equal(rank, card.Rank);
        Assert.Equal(suit, card.Suit);
    }

    [Theory]
    [InlineData("1s")]
    [InlineData("Ax")]
    [InlineData("A")]
    [InlineData("Asd")]
    public void Parse_rejects_bad_text(string text) => Assert.Throws<FormatException>(() => Card.Parse(text));

    [Fact]
    public void ParseMany_accepts_compact_and_spaced_text()
    {
        Assert.Equal(Cards("AsKd"), Cards("As Kd"));
        Assert.Equal("As Kd 2c", CardList.Format(Cards("As,Kd,2c")));
    }

    [Fact]
    public void Index_round_trips_for_all_52_cards()
    {
        var all = Deck.AllCards;
        Assert.Equal(52, all.Distinct().Count());
        for (var i = 0; i < 52; i++) Assert.Equal(i, Card.FromIndex(i).Index);
    }

    [Fact]
    public void Rng_same_seed_same_sequence_different_seed_differs()
    {
        var a = new Rng(42);
        var b = new Rng(42);
        var c = new Rng(43);
        var seqA = Enumerable.Range(0, 20).Select(_ => a.NextUInt64()).ToList();
        Assert.Equal(seqA, Enumerable.Range(0, 20).Select(_ => b.NextUInt64()));
        Assert.NotEqual(seqA, Enumerable.Range(0, 20).Select(_ => c.NextUInt64()));
    }

    [Theory] // published FNV-1a test vectors; drill ids and per-rule seeds depend on these
    [InlineData("", 0x811c9dc5U, 0xcbf29ce484222325UL)]
    [InlineData("a", 0xe40c292cU, 0xaf63dc4c8601ec8cUL)]
    [InlineData("foobar", 0xbf9cf968U, 0x85944171f73967e8UL)]
    public void Fnv1a_matches_reference_vectors(string text, uint hash32, ulong hash64)
    {
        Assert.Equal(hash32, Rng.Fnv1a32(text));
        Assert.Equal(hash64, Rng.Fnv1a64(text));
    }

    [Fact]
    public void Rng_NextInt_stays_in_bounds_and_hits_every_value()
    {
        var rng = new Rng(7);
        var seen = new HashSet<int>();
        for (var i = 0; i < 2000; i++)
        {
            var v = rng.NextInt(3, 9);
            Assert.InRange(v, 3, 9);
            seen.Add(v);
        }
        Assert.Equal(7, seen.Count);
    }

    [Fact]
    public void Deck_deals_52_distinct_cards_then_throws()
    {
        var deck = new Deck(new Rng(1));
        var dealt = deck.Deal(52);
        Assert.Equal(52, dealt.Distinct().Count());
        Assert.Throws<InvalidOperationException>(() => deck.Deal());
    }

    [Fact]
    public void Deck_same_seed_same_deal()
    {
        var a = new Deck(new Rng(99)).Deal(7);
        var b = new Deck(new Rng(99)).Deal(7);
        var c = new Deck(new Rng(100)).Deal(7);
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }
}
