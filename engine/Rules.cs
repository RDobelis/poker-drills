namespace PokerDrills.Engine;

public enum DrawRequirement
{
    /// <summary>"draws": null — draws are not checked.</summary>
    Any,

    /// <summary>"draws": "none" — the hand must have no draw flags.</summary>
    None,
}

/// <summary>What hero did on an earlier street of a turn or river spot (villain checked to hero there).</summary>
public enum EarlierAction
{
    /// <summary>Hero checked back: the street checked through.</summary>
    Check,

    /// <summary>Hero bet and villain called.</summary>
    Bet,
}

public readonly record struct StrengthRange(HandClass Min, HandClass Max)
{
    public bool Contains(HandClass c) => c >= Min && c <= Max;
}

public sealed record Rule
{
    public required string Id { get; init; }
    public bool Placeholder { get; init; }
    public required string VillainType { get; init; }
    public required LineId Line { get; init; }

    /// <summary>Postflop rules: allowed hero hand classes.</summary>
    public StrengthRange? Hero { get; init; }

    /// <summary>Preflop rules: allowed hero hand groups.</summary>
    public IReadOnlyList<HandGroup>? HeroGroup { get; init; }

    public DrawRequirement Draws { get; init; }
    public BoardFlags BoardRequire { get; init; }
    public BoardFlags BoardExclude { get; init; }

    /// <summary>Player types that must all be among the players left to act behind hero.</summary>
    public IReadOnlyList<string> BehindRequire { get; init; } = [];

    /// <summary>Player types that may not be among the players left to act behind hero.</summary>
    public IReadOnlyList<string> BehindExclude { get; init; } = [];

    public bool HasBehindConditions => BehindRequire.Count > 0 || BehindExclude.Count > 0;

    /// <summary>Player types that must all be among the other opponents still in the hand (besides villain).</summary>
    public IReadOnlyList<string> OthersRequire { get; init; } = [];

    /// <summary>Player types that may not be among the other opponents still in the hand (besides villain).</summary>
    public IReadOnlyList<string> OthersExclude { get; init; } = [];

    public bool HasOthersConditions => OthersRequire.Count > 0 || OthersExclude.Count > 0;

    /// <summary>Turn and river rules: what hero must have done on earlier streets ("earlier": { "flop": "bet" }).</summary>
    public IReadOnlyDictionary<Street, EarlierAction> Earlier { get; init; } = new Dictionary<Street, EarlierAction>();

    public required string Correct { get; init; }
    public required string Reason { get; init; }
}

/// <summary>Player types of the seats left to act behind hero, and of the other opponents still in the hand.</summary>
public sealed record Seating(IReadOnlyCollection<string> Behind, IReadOnlyCollection<string> Others)
{
    public static Seating Empty { get; } = new([], []);
}

/// <summary>Everything a rule can test, derived from the cards of a spot.</summary>
public sealed record SpotFacts(HandClass? HandClass, DrawFlags Draws, BoardFlags BoardFlags, int? HighCard, HandGroup? HandGroup)
{
    public static SpotFacts Analyze(IReadOnlyList<Card> hole, IReadOnlyList<Card> board, ClassifierOptions options)
    {
        if (board.Count == 0) return new(null, DrawFlags.None, BoardFlags.None, null, PreflopGroups.Classify(hole));

        var hand = HandClassifier.Classify(hole, board, options);
        var texture = BoardAnalyzer.Analyze(board);
        return new(hand.Class, hand.Draws, texture.Flags, texture.HighCard, null);
    }
}

public static class RuleMatcher
{
    private static readonly IReadOnlyDictionary<Street, EarlierAction> Nothing = new Dictionary<Street, EarlierAction>();

    /// <summary>
    /// A rule matches when line, villain type, every hand/board condition, the seating (who is left to act
    /// behind hero, who else is still in the hand) and what hero did on earlier streets all match.
    /// </summary>
    public static bool Matches(Rule rule, string villainType, LineId line, SpotFacts facts, Seating seating,
        IReadOnlyDictionary<Street, EarlierAction>? earlier = null) =>
        MatchesCards(rule, villainType, line, facts) && MatchesSeating(rule, seating) && MatchesEarlier(rule, earlier ?? Nothing);

    /// <summary>Hero did what the rule asks on each earlier street it names.</summary>
    public static bool MatchesEarlier(Rule rule, IReadOnlyDictionary<Street, EarlierAction> earlier) =>
        rule.Earlier.All(e => earlier.TryGetValue(e.Key, out var did) && did == e.Value);

    /// <summary>Required types are present and excluded types absent, behind hero and among the other opponents.</summary>
    public static bool MatchesSeating(Rule rule, Seating seating) =>
        rule.BehindRequire.All(seating.Behind.Contains) && !rule.BehindExclude.Any(seating.Behind.Contains)
        && rule.OthersRequire.All(seating.Others.Contains) && !rule.OthersExclude.Any(seating.Others.Contains);

    /// <summary>Everything except the players behind: line, villain type, hand and board.</summary>
    public static bool MatchesCards(Rule rule, string villainType, LineId line, SpotFacts facts)
    {
        if (rule.Line != line || rule.VillainType != villainType) return false;

        if (rule.HeroGroup is { } groups) return facts.HandGroup is { } g && groups.Contains(g);

        if (rule.Hero is not { } range || facts.HandClass is not { } handClass || !range.Contains(handClass)) return false;
        if (rule.Draws == DrawRequirement.None && facts.Draws != DrawFlags.None) return false;
        if ((facts.BoardFlags & rule.BoardRequire) != rule.BoardRequire) return false;
        if ((facts.BoardFlags & rule.BoardExclude) != 0) return false;
        return true;
    }
}
