namespace PokerDrills.Engine;

/// <summary>52-card deck dealt with a lazy Fisher-Yates shuffle driven by <see cref="Rng"/>.</summary>
public sealed class Deck
{
    public const int Size = 52;

    private readonly Card[] _cards = new Card[Size];
    private readonly Rng _rng;
    private int _dealt;

    public static IReadOnlyList<Card> AllCards { get; } = Enumerable.Range(0, Size).Select(Card.FromIndex).ToArray();

    public Deck(Rng rng)
    {
        _rng = rng;
        Reset();
    }

    public int Remaining => Size - _dealt;

    /// <summary>Returns every card. The order is restored, so dealing depends only on the RNG state.</summary>
    public void Reset()
    {
        for (var i = 0; i < Size; i++) _cards[i] = AllCards[i];
        _dealt = 0;
    }

    public Card Deal()
    {
        if (_dealt >= Size) throw new InvalidOperationException("Deck is empty");
        var j = _dealt + _rng.NextInt(Size - _dealt);
        (_cards[_dealt], _cards[j]) = (_cards[j], _cards[_dealt]);
        return _cards[_dealt++];
    }

    public Card[] Deal(int count)
    {
        if (count < 0 || count > Remaining) throw new ArgumentOutOfRangeException(nameof(count));
        var cards = new Card[count];
        for (var i = 0; i < count; i++) cards[i] = Deal();
        return cards;
    }
}
