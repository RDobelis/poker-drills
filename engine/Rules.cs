namespace PokerDrills.Engine;

public enum DrawRequirement
{
    /// <summary>"draws": null — draws are not checked.</summary>
    Any,

    /// <summary>"draws": "none" — the hand must have no draw flags.</summary>
    None,
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

    public required string Correct { get; init; }
    public required string Reason { get; init; }
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
    /// <summary>
    /// A rule matches when line, villain type, every hand/board condition and the players left to act
    /// behind hero (<paramref name="behindTypes"/>: their player types) all match.
    /// </summary>
    public static bool Matches(Rule rule, string villainType, LineId line, SpotFacts facts, IReadOnlyCollection<string> behindTypes) =>
        MatchesCards(rule, villainType, line, facts) && MatchesBehind(rule, behindTypes);

    /// <summary>Every required type is behind hero and no excluded type is.</summary>
    public static bool MatchesBehind(Rule rule, IReadOnlyCollection<string> behindTypes) =>
        rule.BehindRequire.All(behindTypes.Contains) && !rule.BehindExclude.Any(behindTypes.Contains);

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
