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
            var behindTypes = DrillGenerator.BehindTypes(d);
            Assert.True(RuleMatcher.Matches(rule, d.VillainType, rule.Line, facts, behindTypes), $"{d.Id} does not match {rule.Id}");
            Assert.Equal(behindTypes.Count, d.Behind.Count);
            Assert.Equal(template.CanHavePlayersBehind, d.Behind.Count > 0); // blinds are always left to act preflop

            Assert.Equal(rule.Line.ToString(), d.Line);
            Assert.Equal(rule.Correct, d.Correct);
            Assert.Equal(rule.Reason, d.Reason);
            Assert.Equal(template.OptionIds, d.Options.Select(o => o.Id));
            Assert.Contains(d.Correct, d.Options.Select(o => o.Id));
            Assert.Equal(facts.HandClass?.ToString(), d.Facts!.HandClass);

            // Every seat but hero's has a player; villain keeps the rule's type and HUD; each HUD fits only its type.
            Assert.Equal(5, d.Players.Count);
            Assert.DoesNotContain(d.Players, p => p.Seat == d.HeroPosition);
            var villainPlayer = Assert.Single(d.Players, p => p.Seat == d.VillainPosition);
            Assert.Equal(d.VillainType, villainPlayer.Type);
            Assert.Equal(d.VillainStats, villainPlayer.Stats);
            foreach (var p in d.Players)
            {
                Assert.Equal([p.Type], StatSampler.TypesContaining(content.Types, p.Stats).Select(t => t.Id));
            }

            // Hero's hand fits the preflop action (no 93o opened UTG).
            var heroSeat = Enum.Parse<Position>(d.HeroPosition!);
            var villainSeat = Enum.Parse<Position>(d.VillainPosition!);
            switch (rule.Line)
            {
                case LineId.Pre_IsoVsLimper:
                    break; // any hand: the preflop decision is the drill
                case LineId.SRP_HeroOOP_FacingFlopCbet:
                    Assert.True(content.Ranges.CanCallInBigBlind(villainSeat, hole), $"{d.Id}: BB calls {d.Facts.Hand} vs {villainSeat}");
                    break;
                default:
                    Assert.True(content.Ranges.CanOpen(heroSeat, hole), $"{d.Id}: hero opens {d.Facts.Hand} from {heroSeat}");
                    break;
            }

            var villainTypes = StatSampler.TypesContaining(content.Types, d.VillainStats).Select(t => t.Id);
            Assert.Equal([d.VillainType], villainTypes);
            // Chip conservation. Postflop: 100 + 100 + dead SB. Preflop: 100 + 100 + both live blinds + 1bb per extra limper.
            var extraLimpers = d.Actions.Count(a => a.Kind == "Limp" && a.Seat != d.VillainPosition);
            var expectedChips = template.DecisionStreet == Street.Preflop ? 201.5 + extraLimpers : 200.5;
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
    public void Seating_never_changes_the_cards_of_postflop_drills()
    {
        // Preflop spots can gain extra limpers from the seating, and "behind" rules choose their seating,
        // so the check is about the postflop rules: who sits around the table never changes their cards or ids.
        var content = TestHelpers.Content;
        var stableRules = content.Rules
            .Where(r => !r.HasBehindConditions && LineTemplate.For(r.Line).DecisionStreet != Street.Preflop)
            .Select(r => r.Id)
            .ToHashSet();
        var maniacTable = content with { Types = content.Types.Select(t => t with { TableShare = t.Id == "Maniac" ? 100 : 1 }).ToList() };
        var a = DrillGenerator.Generate(content, Small).File.Drills.Where(d => stableRules.Contains(d.RuleId)).ToList();
        var b = DrillGenerator.Generate(maniacTable, Small).File.Drills.Where(d => stableRules.Contains(d.RuleId)).ToList();
        Assert.NotEmpty(a);
        Assert.Equal(a.Select(d => d.Id), b.Select(d => d.Id));
        Assert.Equal(a.Select(Spot), b.Select(Spot));
        Assert.NotEqual(a.SelectMany(d => d.Players.Select(p => p.Type)), b.SelectMany(d => d.Players.Select(p => p.Type)));

        static string Spot(Drill d) => string.Join(",", d.HeroCards.Concat(d.Board).Concat(d.ActionHistory));
    }

    [Fact]
    public void Other_players_act_by_type()
    {
        var content = TestHelpers.Content;
        var drills = FullRun.Value.File.Drills.Where(d => d.Kind == DrillKinds.Action).ToList();
        var totalShare = (double)content.Types.Sum(t => t.TableShare);
        double Share(string type) => content.Types.Single(t => t.Id == type).TableShare / totalShare;
        static double Rate(IReadOnlyCollection<string> types, string type) => types.Count(t => t == type) / (double)types.Count;
        static string TypeAt(Drill d, string seat) => d.Players.Single(p => p.Seat == seat).Type;

        // Nobody but hero and villain raises or calls before hero's decision: those would be different spots.
        foreach (var d in drills)
        {
            Assert.DoesNotContain(d.Actions, a =>
                a.Street == "Preflop" && a.Seat != d.HeroPosition && a.Seat != d.VillainPosition && a.Kind is "Raise" or "Call");
        }

        // Players folding before anyone raised are mostly tight: maniacs fold far less often than they sit down.
        var firstInFolders = drills.SelectMany(d => FirstInFolds(d).Select(seat => TypeAt(d, seat))).ToList();
        Assert.True(Rate(firstInFolders, "Maniac") < Share("Maniac") * 0.8, $"maniac share among folders {Rate(firstInFolders, "Maniac")}");
        Assert.True(Rate(firstInFolders, "Nit") > Share("Nit") * 1.05, $"nit share among folders {Rate(firstInFolders, "Nit")}");

        // Extra limpers in "villain limps" spots are mostly loose-passive.
        var extraLimpers = drills
            .SelectMany(d => d.Actions.Where(a => a.Kind == "Limp" && a.Seat != d.VillainPosition).Select(a => TypeAt(d, a.Seat)))
            .ToList();
        Assert.True(extraLimpers.Count > 20, $"only {extraLimpers.Count} extra limpers");
        Assert.True(Rate(extraLimpers, "CallingStation") > Share("CallingStation") * 1.5);
        Assert.True(Rate(extraLimpers, "Nit") < Share("Nit"));
    }

    private static IEnumerable<string> FirstInFolds(Drill d)
    {
        var raised = false;
        foreach (var a in d.Actions.Where(a => a.Street == "Preflop"))
        {
            if (a.Kind == "Fold" && !raised) yield return a.Seat;
            if (a.Kind == "Raise") raised = true;
        }
    }

    [Fact]
    public void Rules_about_players_behind_get_exactly_that_seating()
    {
        var drills = FullRun.Value.File.Drills;
        var withManiac = drills.Where(d => d.RuleId == "station-iso-playable-maniac-behind").ToList();
        var withoutManiac = drills.Where(d => d.RuleId == "station-iso-playable").ToList();
        Assert.True(withManiac.Count >= 150 && withoutManiac.Count >= 150);
        Assert.All(withManiac, d => Assert.Contains("Maniac", DrillGenerator.BehindTypes(d)));
        Assert.All(withoutManiac, d => Assert.DoesNotContain("Maniac", DrillGenerator.BehindTypes(d)));
        Assert.Contains("left to act behind you: Maniac",
            FullRun.Value.File.Rules.Single(r => r.Id == "station-iso-playable-maniac-behind").Conditions);
    }

    [Fact]
    public void Conflict_checker_sees_players_behind()
    {
        var content = TestHelpers.Content;
        Rule Iso(string id, string correct, string[] require, string[] exclude) => new()
        {
            Id = id,
            VillainType = "CallingStation",
            Line = LineId.Pre_IsoVsLimper,
            HeroGroup = [HandGroup.Playable],
            BehindRequire = require,
            BehindExclude = exclude,
            Correct = correct,
            Reason = "test",
        };
        var general = Iso("test-general", "Iso5", [], []);
        var maniacBehind = Iso("test-maniac", "Fold", ["Maniac"], []);
        var generalWithoutManiac = Iso("test-general", "Iso5", [], ["Maniac"]);

        var clash = DrillGenerator.Generate(content with { Rules = [general, maniacBehind] }, Small);
        Assert.Contains(clash.Conflicts, c => c.RuleId == "test-general" && c.OtherRuleId == "test-maniac");

        var fixedByExclude = DrillGenerator.Generate(content with { Rules = [generalWithoutManiac, maniacBehind] }, Small);
        Assert.Empty(fixedByExclude.Conflicts);
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
        Assert.True(RuleMatcher.Matches(check, conflict.Example.VillainType, check.Line, facts, DrillGenerator.BehindTypes(conflict.Example)));
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
