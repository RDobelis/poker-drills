using PokerDrills.Engine;

namespace PokerDrills.Engine.Tests;

public class GeneratorTests
{
    private static readonly GeneratorOptions Small = new() { Seed = 42, PerRule = 60, MaxAttempts = 50_000, HandsPerRule = 15 };

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
            var seating = DrillGenerator.SeatingOf(d);
            var earlier = DrillGenerator.EarlierOf(d.Actions, d.HeroPosition);
            Assert.True(RuleMatcher.Matches(rule, d.VillainType, rule.Line, facts, seating, earlier), $"{d.Id} does not match {rule.Id}");
            Assert.Equal(seating.Behind.Count, d.Behind.Count);
            Assert.Equal(seating.Others.Count, d.Others.Count);
            Assert.Equal(template.CanHavePlayersBehind, d.Behind.Count > 0); // the blinds are left to act in iso spots
            if (!template.CanHaveOtherOpponents) Assert.Empty(d.Others);
            if (rule.Line == LineId.SRP_3Way_FlopCheckedToHero) Assert.Equal(["SB"], d.Others);

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
            // Chip conservation: hero + villain stacks + pot = their 200bb + whatever everyone else put in
            // (dead blinds, extra limpers, the small blind's call in 3-way pots).
            var othersIn = d.Actions
                .Where(a => a.Seat != d.HeroPosition && a.Seat != d.VillainPosition)
                .GroupBy(a => (a.Seat, a.Street))
                .Sum(g => g.Max(a => a.To));
            Assert.Equal(200 + othersIn, d.Stacks!.Hero + d.Stacks.Villain + d.Pot!.Value, 6);
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
        // Preflop spots can gain extra limpers from the seating, multiway spots need a cold-caller, and rules with
        // seating conditions choose their seating, so the check is about the heads-up postflop rules: who sits around
        // the table never changes their cards or ids.
        var content = TestHelpers.Content;
        var stableRules = content.Rules
            .Where(r => !r.HasBehindConditions && !r.HasOthersConditions
                && LineTemplate.For(r.Line) is { DecisionStreet: not Street.Preflop, CanHaveOtherOpponents: false })
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

        // Only hero, villain and the other opponents the spot is built around (the 3-way small blind) raise or call.
        foreach (var d in drills)
        {
            Assert.DoesNotContain(d.Actions, a =>
                a.Street == "Preflop" && a.Seat != d.HeroPosition && a.Seat != d.VillainPosition && !d.Others.Contains(a.Seat)
                && a.Kind is "Raise" or "Call");
        }

        // The small blind who cold-calls in 3-way pots is usually a station.
        var coldCallers = drills.Where(d => d.Line == "SRP_3Way_FlopCheckedToHero").Select(d => TypeAt(d, "SB")).ToList();
        Assert.True(Rate(coldCallers, "CallingStation") > Share("CallingStation") * 1.5, $"station share {Rate(coldCallers, "CallingStation")}");

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
    public void Earlier_streets_of_turn_and_river_spots_follow_the_rules()
    {
        var content = TestHelpers.Content;
        var later = FullRun.Value.File.Drills
            .Where(d => d.Kind == DrillKinds.Action && LineTemplate.For(Enum.Parse<LineId>(d.Line)) is SrpLaterStreetLine)
            .ToList();
        Assert.Contains(later, d => d.Board.Count == 4);
        Assert.Contains(later, d => d.Board.Count == 5);
        foreach (var d in later)
        {
            var template = (SrpLaterStreetLine)LineTemplate.For(Enum.Parse<LineId>(d.Line));
            var hole = d.HeroCards.Select(Card.Parse).ToArray();
            var board = d.Board.Select(Card.Parse).ToArray();
            var pot = Stakes.SrpPot;
            var before = new Dictionary<Street, EarlierAction>();
            foreach (var street in template.EarlierStreets)
            {
                var seen = board.Take(LineTemplate.BoardCountAt(street)).ToList();
                var expected = DrillGenerator.HeroPlay(street, hole, seen, d.VillainType, content.Rules, ClassifierOptions.Default, before).Play;
                var heroAction = d.Actions.Single(a => a.Seat == d.HeroPosition && a.Street == street.ToString());
                before[street] = expected == StreetPlay.CheckThrough ? EarlierAction.Check : EarlierAction.Bet;
                if (expected == StreetPlay.CheckThrough)
                {
                    Assert.Equal("Check", heroAction.Kind);
                    continue;
                }
                var bet = Stakes.Bet(pot, expected == StreetPlay.Bet33Call ? 33 : 75);
                Assert.Equal("Bet", heroAction.Kind);
                Assert.Equal((double)bet, heroAction.To, 6);
                Assert.Contains(d.Actions, a => a.Seat == d.VillainPosition && a.Street == street.ToString() && a.Kind == "Call");
                pot += 2 * bet;
            }
            Assert.Equal(template.EarlierStreets.Count, d.Facts!.Path.Count);
        }
    }

    [Fact]
    public void Hero_plays_earlier_streets_by_rule_or_default()
    {
        var rules = TestHelpers.Content.Rules;
        var air = Card.ParseMany("9c4s");
        var dryFlop = Card.ParseMany("Ks7d2h");
        var options = ClassifierOptions.Default;
        StreetDecision Play(Street street, IReadOnlyList<Card> hole, IReadOnlyList<Card> board, string type,
            EarlierAction? flop = null) =>
            DrillGenerator.HeroPlay(street, hole, board, type, rules, options,
                flop is { } f ? new Dictionary<Street, EarlierAction> { [Street.Flop] = f } : null);

        var stab = Play(Street.Flop, air, dryFlop, "Overfolder");
        Assert.Equal((StreetPlay.Bet33Call, "overfolder-flop-stab"), (stab.Play, stab.Rule?.Id));
        Assert.Equal("Flop: bet 33%, called (rule overfolder-flop-stab)", DrillGenerator.Describe(stab));
        var station = Play(Street.Flop, air, dryFlop, "CallingStation");
        Assert.Equal((StreetPlay.CheckThrough, "station-flop-no-stab"), (station.Play, station.Rule?.Id));

        var nit = Play(Street.Flop, air, dryFlop, "Nit");
        Assert.Equal(StreetPlay.CheckThrough, nit.Play); // the default checks air
        Assert.Null(nit.Rule);
        Assert.Equal("Flop: check (default for Air)", DrillGenerator.Describe(nit));
        Assert.Equal(StreetPlay.Bet33Call, Play(Street.Flop, Card.ParseMany("Ah5h"), Card.ParseMany("Kh9h2c"), "Nit").Play); // flush draw
        Assert.Equal(StreetPlay.Bet75Call, Play(Street.Turn, Card.ParseMany("AsKd"), Card.ParseMany("Kc7h2s9d"), "Nit").Play); // TPGK

        // Turn rules decide the turn, and what happened on the flop picks the rule: an overfolder who called a flop
        // bet gets barrelled big, one who checked the flop through gets a small stab.
        var turn = Card.ParseMany("Ks7d2h3c");
        var barrel = Play(Street.Turn, air, turn, "Overfolder", EarlierAction.Bet);
        Assert.Equal((StreetPlay.Bet75Call, "overfolder-turn-barrel"), (barrel.Play, barrel.Rule?.Id));
        var stabTurn = Play(Street.Turn, air, turn, "Overfolder", EarlierAction.Check);
        Assert.Equal((StreetPlay.Bet33Call, "overfolder-turn-stab"), (stabTurn.Play, stabTurn.Rule?.Id));
        Assert.Null(Play(Street.Turn, air, turn, "Overfolder").Rule); // flop unknown: neither rule applies
    }

    [Fact]
    public void Turn_rules_about_the_flop_only_get_spots_where_hero_did_that()
    {
        var drills = FullRun.Value.File.Drills;
        string HeroOnFlop(Drill d) => d.Actions.Single(a => a.Seat == d.HeroPosition && a.Street == "Flop").Kind;
        var barrels = drills.Where(d => d.RuleId == "overfolder-turn-barrel").ToList();
        var stabs = drills.Where(d => d.RuleId == "overfolder-turn-stab").ToList();
        Assert.True(barrels.Count >= 150 && stabs.Count >= 150);
        Assert.All(barrels, d => Assert.Equal("Bet", HeroOnFlop(d)));
        Assert.All(stabs, d => Assert.Equal("Check", HeroOnFlop(d)));
        Assert.All(drills.Where(d => d.Steps is not null).SelectMany(h => h.Steps!).Where(s => s.RuleId == "overfolder-turn-barrel"),
            s => Assert.Contains(s.Actions, a => a.Street == "Flop" && a.Kind == "Bet"));

        var rules = FullRun.Value.File.Rules;
        Assert.EndsWith("you bet the flop and got called", rules.Single(r => r.Id == "overfolder-turn-barrel").Conditions);
        Assert.EndsWith("the flop checked through", rules.Single(r => r.Id == "overfolder-turn-stab").Conditions);
    }

    [Fact]
    public void Hands_end_at_turn_and_river_rules_whose_type_has_rules_for_earlier_streets()
    {
        var rules = TestHelpers.Content.Rules;
        Assert.Equal(["station-river-no-bluff", "station-river-value", "station-turn-no-bluff", "station-turn-value"],
            DrillGenerator.HandAnchors("CallingStation", rules).Select(r => r.Id));
        Assert.Equal(["overfolder-turn-barrel"], DrillGenerator.HandAnchors("Overfolder", rules).Select(r => r.Id));
        Assert.Empty(DrillGenerator.HandAnchors("Nit", rules)); // a river rule, but nothing for the flop or turn
        Assert.Empty(DrillGenerator.HandAnchors("Maniac", rules)); // its flop rule is out of position (facing a c-bet)

        var result = FullRun.Value;
        Assert.Equal(["hands-calling-station", "hands-overfolder"],
            result.File.Rules.Where(r => r.Kind == DrillKinds.Hand).Select(r => r.Id));
        foreach (var report in result.Reports.Where(r => r.Kind == DrillKinds.Hand))
        {
            Assert.Equal(DrillGenerator.HandAnchors(report.RuleId == "hands-overfolder" ? "Overfolder" : "CallingStation", rules).Count * 50,
                report.Produced);
            Assert.Empty(report.Warnings);
        }
    }

    [Fact]
    public void Hands_ask_every_rule_decided_street_and_go_on_along_the_correct_line()
    {
        var content = TestHelpers.Content;
        var rules = content.Rules.ToDictionary(r => r.Id);
        var hands = FullRun.Value.File.Drills.Where(d => d.Kind == DrillKinds.Hand).ToList();
        Assert.NotEmpty(hands);
        foreach (var h in hands)
        {
            var steps = h.Steps!;
            var hole = h.HeroCards.Select(Card.Parse).ToArray();
            var fullBoard = h.Board.Select(Card.Parse).ToArray();
            Assert.True(steps.Count >= 2, h.Id);
            Assert.Equal(h.Board, steps[^1].Board);
            Assert.Contains(rules[steps[^1].RuleId], DrillGenerator.HandAnchors(h.VillainType, content.Rules));
            Assert.Equal(5, h.Players.Count);
            Assert.Equal(h.VillainStats, h.Players.Single(p => p.Seat == h.VillainPosition).Stats);

            for (var i = 0; i < steps.Count; i++)
            {
                var s = steps[i];
                var rule = rules[s.RuleId];
                var line = Enum.Parse<LineId>(s.Line);
                Assert.Equal(rule.Line, line);
                Assert.Equal(h.VillainType, rule.VillainType);
                Assert.Equal((rule.Correct, rule.Reason), (s.Correct, s.Reason));
                Assert.Equal(LineTemplate.For(line).OptionIds, s.Options.Select(o => o.Id));
                Assert.Equal(h.Board.Take(LineTemplate.For(line).BoardCardCount), s.Board);
                var facts = SpotFacts.Analyze(hole, s.Board.Select(Card.Parse).ToArray(), ClassifierOptions.Default);
                Assert.True(RuleMatcher.Matches(rule, h.VillainType, line, facts, Seating.Empty, DrillGenerator.EarlierOf(s.Actions, h.HeroPosition)),
                    $"{h.Id} step {i + 1} vs {rule.Id}");
                Assert.Equal(200.5, s.Stacks.Hero + s.Stacks.Villain + s.Pot, 6); // heads-up plus the dead small blind

                if (i == 0) continue;
                // The hand goes on from the previous decision, along that decision's correct answer.
                var prev = steps[i - 1];
                Assert.True(prev.Board.Count < s.Board.Count);
                Assert.Equal(prev.Actions, s.Actions.Take(prev.Actions.Count));
                var heroNext = s.Actions[prev.Actions.Count];
                Assert.Equal(h.HeroPosition, heroNext.Seat);
                Assert.Equal(prev.Correct == "Check" ? "Check" : "Bet", heroNext.Kind);
            }

            // The earlier decisions are exactly the streets where a rule decides hero's play.
            var last = (SrpLaterStreetLine)LineTemplate.For(Enum.Parse<LineId>(steps[^1].Line));
            var before = new Dictionary<Street, EarlierAction>();
            var ruled = new List<string>();
            foreach (var street in last.EarlierStreets)
            {
                var played = DrillGenerator.HeroPlay(street, hole, fullBoard.Take(LineTemplate.BoardCountAt(street)).ToList(),
                    h.VillainType, content.Rules, ClassifierOptions.Default, before);
                if (played.Rule is not null) ruled.Add(played.Rule.Id);
                before[street] = played.Play == StreetPlay.CheckThrough ? EarlierAction.Check : EarlierAction.Bet;
            }
            Assert.Equal(ruled, steps.SkipLast(1).Select(s => s.RuleId));
        }
    }

    [Fact]
    public void Rules_about_players_behind_get_exactly_that_seating()
    {
        var drills = FullRun.Value.File.Drills;
        var withManiac = drills.Where(d => d.RuleId == "station-iso-playable-maniac-behind").ToList();
        var withoutManiac = drills.Where(d => d.RuleId == "station-iso-playable").ToList();
        Assert.True(withManiac.Count >= 150 && withoutManiac.Count >= 150);
        Assert.All(withManiac, d => Assert.Contains("Maniac", DrillGenerator.SeatingOf(d).Behind));
        Assert.All(withoutManiac, d => Assert.DoesNotContain("Maniac", DrillGenerator.SeatingOf(d).Behind));
        Assert.All(drills.Where(d => d.RuleId == "overfolder-3way-dry-stab"),
            d => Assert.DoesNotContain("CallingStation", DrillGenerator.SeatingOf(d).Others));
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
        Assert.True(RuleMatcher.Matches(check, conflict.Example.VillainType, check.Line, facts, DrillGenerator.SeatingOf(conflict.Example)));
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
