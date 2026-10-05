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
    /// <summary>A rule matches when line, villain type and every hand/board condition match.</summary>
    public static bool Matches(Rule rule, string villainType, LineId line, SpotFacts facts)
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
