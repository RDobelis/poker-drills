using PokerDrills.Engine;

namespace PokerDrills.Engine.Tests;

public class PlayerTypeAndContentTests
{
    private static IReadOnlyList<PlayerType> Types => TestHelpers.Content.Types;

    [Fact]
    public void Real_content_loads()
    {
        var content = TestHelpers.Content;
        Assert.Equal(["Nit", "CallingStation", "Maniac", "Overfolder", "Reg"], content.Types.Select(t => t.Id));
        Assert.Equal(11, content.Rules.Count);
        var maniacBehind = content.Rules.Single(r => r.Id == "station-iso-playable-maniac-behind");
        Assert.Equal(["Maniac"], maniacBehind.BehindRequire);
        Assert.Empty(maniacBehind.BehindExclude);

        var rule = content.Rules.Single(r => r.Id == "station-river-value");
        Assert.Equal(LineId.SRP_HeroIP_RiverVillainChecks, rule.Line);
        Assert.Equal(new StrengthRange(HandClass.TopPairWeakKicker, HandClass.FullHousePlus), rule.Hero);
        Assert.Equal(BoardFlags.FourToFlush | BoardFlags.FourToStraight, rule.BoardExclude);
        Assert.Equal(DrawRequirement.Any, rule.Draws);
        Assert.Equal("Bet75", rule.Correct);

        var stab = content.Rules.Single(r => r.Id == "overfolder-flop-stab");
        Assert.Equal(DrawRequirement.None, stab.Draws);
        Assert.Equal(BoardFlags.Dry, stab.BoardRequire);

        var iso = content.Rules.Single(r => r.Id == "station-iso-big");
        Assert.Equal([HandGroup.Premium, HandGroup.Strong], iso.HeroGroup);
    }

    [Fact]
    public void Samples_stay_inside_ranges_and_are_consistent()
    {
        var rng = new Rng(8);
        foreach (var type in Types)
        {
            for (var i = 0; i < 1000; i++)
            {
                var s = StatSampler.Sample(type, rng);
                Assert.True(type.Contains(s), $"{type.Id}: {s}");
                Assert.True(s.Pfr <= s.Vpip && s.ThreeBet <= s.Pfr, s.ToString());
                Assert.Equal(Math.Round(s.Af, 1), s.Af);
            }
        }
    }

    [Fact]
    public void Unambiguous_sampling_rejects_lines_inside_two_types()
    {
        var a = Types.Single(t => t.Id == "Reg");
        var clone = a with { Id = "RegClone" }; // identical ranges: every line is ambiguous
        Assert.Throws<InvalidOperationException>(() => StatSampler.SampleUnambiguous(a, [a, clone], new Rng(1)));

        var line = StatSampler.SampleUnambiguous(a, Types, new Rng(1));
        Assert.Equal(["Reg"], StatSampler.TypesContaining(Types, line).Select(t => t.Id));
    }

    private static Rule Parse(string json, string? expectedId = null) => ContentLoader.ParseRule(json, Types, expectedId);

    private const string ValidRule = """
        { "id": "x", "villainType": "Nit", "line": "SRP_HeroIP_FacingRiverBet",
          "hero": { "minStrength": "SecondPair", "maxStrength": "Overpair" },
          "draws": null, "boardRequire": [], "boardExclude": [], "correct": "Fold", "reason": "r" }
        """;

    [Fact]
    public void Valid_rule_parses() => Assert.Equal("Fold", Parse(ValidRule).Correct);

    [Theory]
    [InlineData("\"correct\": \"Fold\"", "\"correct\": \"Bet75\"", "correct")]
    [InlineData("\"villainType\": \"Nit\"", "\"villainType\": \"Fish\"", "villainType")]
    [InlineData("\"line\": \"SRP_HeroIP_FacingRiverBet\"", "\"line\": \"Nope\"", "line")]
    [InlineData("\"minStrength\": \"SecondPair\"", "\"minStrength\": \"Flush\"", "stronger")]
    [InlineData("\"boardRequire\": []", "\"boardRequire\": [\"Wet\"]", "boardRequire")]
    [InlineData("\"boardRequire\": [], \"boardExclude\": []", "\"boardRequire\": [\"Paired\"], \"boardExclude\": [\"Paired\"]", "both required and excluded")]
    [InlineData("\"draws\": null", "\"draws\": \"some\"", "draws")]
    [InlineData("\"reason\": \"r\"", "\"reason\": \"r\", \"boardExlude\": []", "boardExlude")] // typo'd property
    [InlineData("\"id\": \"x\"", "\"id\": \"Bad Id\"", "kebab-case")]
    public void Invalid_rule_reports_the_problem(string find, string replace, string expectedInMessage)
    {
        var json = ValidRule.Replace(find, replace);
        Assert.NotEqual(ValidRule, json);
        var e = Assert.Throws<ContentException>(() => Parse(json));
        Assert.Contains(expectedInMessage, e.Message);
    }

    [Theory]
    [InlineData("\"behind\": { \"require\": [\"Shark\"] }", "behind.require: 'Shark'")]
    [InlineData("\"behind\": { \"require\": [\"Maniac\"], \"exclude\": [\"maniac\"] }", "both required and excluded")]
    public void Behind_conditions_are_validated(string behind, string expected)
    {
        var json = $$"""
            { "id": "p", "villainType": "CallingStation", "line": "Pre_IsoVsLimper", "heroGroup": ["Playable"],
              {{behind}}, "correct": "Fold", "reason": "r" }
            """;
        Assert.Contains(expected, Assert.Throws<ContentException>(() => Parse(json)).Message);
    }

    [Fact]
    public void Behind_conditions_only_apply_preflop()
    {
        var json = ValidRule.Replace("\"reason\": \"r\"", "\"reason\": \"r\", \"behind\": { \"exclude\": [\"Maniac\"] }");
        Assert.Contains("only apply to preflop", Assert.Throws<ContentException>(() => Parse(json)).Message);
    }

    [Fact]
    public void Rule_id_must_match_file_name()
    {
        var e = Assert.Throws<ContentException>(() => Parse(ValidRule, "other-name"));
        Assert.Contains("file name", e.Message);
    }

    [Fact]
    public void Preflop_rule_needs_hero_group_not_hero()
    {
        const string json = """
            { "id": "p", "villainType": "Nit", "line": "Pre_IsoVsLimper",
              "hero": { "minStrength": "Air", "maxStrength": "Air" }, "correct": "Fold", "reason": "r" }
            """;
        var e = Assert.Throws<ContentException>(() => Parse(json));
        Assert.Contains("heroGroup", e.Message);
    }

    [Fact]
    public void Preflop_ranges_are_validated()
    {
        const string json = """
            { "open": { "UTG": "22+", "MP": "22+", "CO": "A2x", "BTN": "22+", "HJ": "22+" },
              "bigBlindCall": { "UTG": "22+", "MP": "22+", "CO": "22+" } }
            """;
        var e = Assert.Throws<ContentException>(() => ContentLoader.ParseRanges(json));
        Assert.Contains("open.CO", e.Message);
        Assert.Contains("unknown position 'HJ'", e.Message);
        Assert.Contains("missing a range for BTN", e.Message);
    }

    [Fact]
    public void Table_shares_are_read_and_validated()
    {
        Assert.Equal(30, Types.Single(t => t.Id == "Reg").TableShare);
        Assert.Equal("Station", Types.Single(t => t.Id == "CallingStation").SeatLabel); // shortName set
        Assert.Equal("Reg", Types.Single(t => t.Id == "Reg").SeatLabel); // falls back to name

        const string ranges =
            "\"ranges\": { \"VPIP\": [10, 20], \"PFR\": [5, 10], \"3Bet\": [1, 3], \"WTSD\": [20, 30], \"AF\": [1, 2], \"FoldToCbet\": [40, 50] }";
        Assert.Equal(1, ContentLoader.ParseTypes($$"""{ "types": [ { "id": "A", {{ranges}} } ] }""")[0].TableShare);

        var negative = Assert.Throws<ContentException>(() =>
            ContentLoader.ParseTypes($$"""{ "types": [ { "id": "A", "tableShare": -1, {{ranges}} } ] }"""));
        Assert.Contains("tableShare must be 0 or more", negative.Message);

        var allZero = Assert.Throws<ContentException>(() =>
            ContentLoader.ParseTypes($$"""{ "types": [ { "id": "A", "tableShare": 0, {{ranges}} } ] }"""));
        Assert.Contains("tableShare above 0", allZero.Message);
    }

    [Fact]
    public void Types_with_empty_range_are_rejected()
    {
        const string json = """
            { "types": [ { "id": "Odd", "name": "Odd", "description": "",
              "ranges": { "VPIP": [10.2, 10.8], "PFR": [1, 2], "3Bet": [0, 1], "WTSD": [1, 2], "AF": [1, 2], "FoldToCbet": [1, 2] } } ] }
            """;
        var e = Assert.Throws<ContentException>(() => ContentLoader.ParseTypes(json));
        Assert.Contains("VPIP", e.Message);
    }
}
