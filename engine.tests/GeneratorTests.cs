using PokerDrills.Engine;

namespace PokerDrills.Engine.Tests;

public class GeneratorTests
{
    private static readonly GeneratorOptions Small = new() { Seed = 42, PerRule = 60, MaxAttempts = 50_000 };

    // Full-size run shared by the tests that check the real deliverable.
    private static readonly Lazy<GenerationResult> FullRun =
        new(() => DrillGenerator.Generate(TestHelpers.Content, new GeneratorOptions { Seed = 42 }));

    [Fact]
    public void Every_action_drill_matches_its_rule_when_re_analysed_from_its_cards()
    {
        var content = TestHelpers.Content;
        var result = DrillGenerator.Generate(content, Small);
        var rules = content.Rules.ToDictionary(r => r.Id);

        var actionDrills = result.File.Drills.Where(d => d.Kind == DrillKinds.Action).ToList();
        Assert.Equal(content.Rules.Count * Small.PerRule, actionDrills.Count);

        foreach (var d in actionDrills)
        {
            var rule = rules[d.RuleId];
            var template = LineTemplate.For(rule.Line);
            var hole = d.HeroCards.Select(Card.Parse).ToArray();
            var board = d.Board.Select(Card.Parse).ToArray();

            Assert.Equal(template.BoardCardCount, board.Length);
            CardList.EnsureDistinct(hole.Concat(board));
            var facts = SpotFacts.Analyze(hole, board, ClassifierOptions.Default);
            Assert.True(RuleMatcher.Matches(rule, d.VillainType, rule.Line, facts), $"{d.Id} does not match {rule.Id}");

            Assert.Equal(rule.Line.ToString(), d.Line);
            Assert.Equal(rule.Correct, d.Correct);
            Assert.Equal(rule.Reason, d.Reason);
            Assert.Equal(template.OptionIds, d.Options.Select(o => o.Id));
            Assert.Contains(d.Correct, d.Options.Select(o => o.Id));
            Assert.Equal(facts.HandClass?.ToString(), d.Facts!.HandClass);

            var villainTypes = StatSampler.TypesContaining(content.Types, d.VillainStats).Select(t => t.Id);
            Assert.Equal([d.VillainType], villainTypes);
            // Chip conservation. Postflop: 100 + 100 + dead SB. Preflop: 100 + 100 + both live blinds.
            var expectedChips = template.DecisionStreet == Street.Preflop ? 201.5 : 200.5;
            Assert.Equal(expectedChips, d.Stacks!.Hero + d.Stacks.Villain + d.Pot!.Value, 6);
        }
    }

    [Fact]
    public void No_duplicate_hand_board_spots_or_ids()
    {
        var drills = DrillGenerator.Generate(TestHelpers.Content, Small).File.Drills;
        Assert.Equal(drills.Count, drills.Select(d => d.Id).Distinct().Count());
        foreach (var group in drills.Where(d => d.Kind == DrillKinds.Action && d.Board.Count > 0).GroupBy(d => d.RuleId))
        {
            var keys = group.Select(d => string.Join("", d.HeroCards.Order()) + "|" + string.Join("", d.Board.Order()));
            Assert.Equal(group.Count(), keys.Distinct().Count());
        }
    }

    [Fact]
    public void Identify_drills_are_unambiguous_and_offer_every_type()
    {
        var content = TestHelpers.Content;
        var drills = DrillGenerator.Generate(content, Small).File.Drills.Where(d => d.Kind == DrillKinds.Identify).ToList();
        Assert.NotEmpty(drills);
        foreach (var d in drills)
        {
            Assert.Equal([d.VillainType], StatSampler.TypesContaining(content.Types, d.VillainStats).Select(t => t.Id));
            Assert.Equal(content.Types.Select(t => t.Id), d.Options.Select(o => o.Id));
            Assert.Equal(d.VillainType, d.Correct);
            Assert.Equal(DrillGenerator.IdentifyQuestion, d.Question);
            Assert.Empty(d.HeroCards);
        }
        Assert.Equal("identify-calling-station", DrillGenerator.IdentifyRuleId(content.Types.Single(t => t.Id == "CallingStation")));
    }

    [Fact]
    public void Same_seed_gives_identical_output_and_another_seed_differs()
    {
        var content = TestHelpers.Content;
        var a = DrillJson.Serialize(DrillGenerator.Generate(content, Small).File);
        var b = DrillJson.Serialize(DrillGenerator.Generate(content, Small).File);
        var c = DrillJson.Serialize(DrillGenerator.Generate(content, Small with { Seed = 43 }).File);
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void Output_round_trips_through_json()
    {
        var file = DrillGenerator.Generate(TestHelpers.Content, Small).File;
        var json = DrillJson.Serialize(file);
        var back = DrillJson.Deserialize(json);
        Assert.Equal(json, DrillJson.Serialize(back));
        Assert.Contains("\"3Bet\":", json);
        Assert.Contains("\"villainStats\":", json);
    }

    [Fact]
    public void Real_content_yields_at_least_150_per_rule_and_no_conflicts()
    {
        var result = FullRun.Value;
        Assert.Empty(result.Conflicts);
        foreach (var report in result.Reports.Where(r => r.Kind == DrillKinds.Action))
        {
            Assert.True(report.Produced >= 150, $"{report.RuleId}: {report.Produced}");
            Assert.True(report.Attempts <= 200_000);
            Assert.Empty(report.Warnings);
        }
    }

    [Fact]
    public void Conflict_checker_catches_overlapping_rules_with_different_answers()
    {
        var content = TestHelpers.Content;
        var value = Rule("test-value", "CallingStation", HandClass.TopPairWeakKicker, HandClass.FullHousePlus, "Bet75");
        var check = Rule("test-check", "CallingStation", HandClass.TopPairGoodKicker, HandClass.Overpair, "Check");
        var test = content with { Rules = [value, check] };

        var result = DrillGenerator.Generate(test, Small);

        Assert.NotEmpty(result.Conflicts);
        var conflict = result.Conflicts.First(c => c.RuleId == "test-value");
        Assert.Equal("test-check", conflict.OtherRuleId);
        Assert.Equal("Bet75", conflict.Correct);
        Assert.Equal("Check", conflict.OtherCorrect);
        Assert.True(conflict.Count > 0);
        // The example really is matched by both rules.
        var facts = SpotFacts.Analyze(conflict.Example.HeroCards.Select(Card.Parse).ToArray(),
            conflict.Example.Board.Select(Card.Parse).ToArray(), ClassifierOptions.Default);
        Assert.True(RuleMatcher.Matches(check, conflict.Example.VillainType, check.Line, facts));
        Assert.Contains(result.Reports, r => r.RuleId == "test-check" && r.Conflicts > 0);
    }

    [Fact]
    public void Overlap_with_same_answer_or_other_villain_type_is_not_a_conflict()
    {
        var content = TestHelpers.Content;
        var a = Rule("test-a", "CallingStation", HandClass.TopPairWeakKicker, HandClass.FullHousePlus, "Bet75");
        var sameAnswer = Rule("test-b", "CallingStation", HandClass.TopPairGoodKicker, HandClass.Overpair, "Bet75");
        var otherType = Rule("test-c", "Nit", HandClass.TopPairGoodKicker, HandClass.Overpair, "Check");

        var result = DrillGenerator.Generate(content with { Rules = [a, sameAnswer, otherType] }, Small);

        Assert.Empty(result.Conflicts);
    }

    private static Rule Rule(string id, string type, HandClass min, HandClass max, string correct) => new()
    {
        Id = id,
        VillainType = type,
        Line = LineId.SRP_HeroIP_RiverVillainChecks,
        Hero = new StrengthRange(min, max),
        Correct = correct,
        Reason = "test",
    };
}
