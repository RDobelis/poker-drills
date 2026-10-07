using System.Globalization;
using System.Text;

namespace PokerDrills.Engine;

public sealed record GeneratorOptions
{
    public ulong Seed { get; init; } = 42;
    public int PerRule { get; init; } = 200;
    public int MaxAttempts { get; init; } = 200_000;
    public int WarnBelow { get; init; } = 50;
    public int IdentifySamplesPerType { get; init; } = 100;

    /// <summary>Hand drills ending at each anchor rule (see <see cref="DrillGenerator.HandAnchors"/>).</summary>
    public int HandsPerRule { get; init; } = 50;
    public ClassifierOptions Classifier { get; init; } = ClassifierOptions.Default;
}

public sealed record RuleReport(string RuleId, string Kind, int Produced, int Attempts, IReadOnlyList<string> Warnings, int Conflicts);

/// <summary>Drills of <see cref="RuleId"/> that <see cref="OtherRuleId"/> also matches, with a different answer.</summary>
public sealed record Conflict(string RuleId, string OtherRuleId, string Correct, string OtherCorrect, int Count, Drill Example);

public sealed record GenerationResult(DrillFile File, IReadOnlyList<RuleReport> Reports, IReadOnlyList<Conflict> Conflicts);

/// <summary>Hero's play on a street villain checks to hero before the decision, and the rule that chose it (null: the default).</summary>
public sealed record StreetDecision(Street Street, StreetPlay Play, Rule? Rule, string Why);

public static class DrillGenerator
{
    // 2: drills carry step-by-step `actions` for the table view. 3: `players` = a type and HUD for every seat.
    // 4: `behind` = seats left to act after hero; rules can react to them. 5: `others` = other opponents in the hand.
    // 6: `hand` drills with `steps` (several decisions in one hand).
    public const int SchemaVersion = 6;
    public const string IdentifyLine = "Identify";
    public const string IdentifyQuestion = "Which player type is this?";
    public const string HandLine = "Hand";
    public const string HandQuestion = "Play the hand street by street.";

    public static GenerationResult Generate(ContentSet content, GeneratorOptions options)
    {
        var drills = new List<Drill>();
        var produced = new List<(string RuleId, string Kind, int Count, int Attempts)>();

        foreach (var rule in content.Rules.OrderBy(r => r.Id, StringComparer.Ordinal))
        {
            var (ruleDrills, attempts) = GenerateForRule(rule, content, options);
            drills.AddRange(ruleDrills);
            produced.Add((rule.Id, DrillKinds.Action, ruleDrills.Count, attempts));
        }

        foreach (var type in content.Types)
        {
            var (typeDrills, samples) = GenerateIdentify(type, content.Types, options);
            drills.AddRange(typeDrills);
            produced.Add((IdentifyRuleId(type), DrillKinds.Identify, typeDrills.Count, samples));
        }

        foreach (var type in content.Types.Where(t => HandAnchors(t.Id, content.Rules).Count > 0))
        {
            var (hands, attempts) = GenerateHands(type, content, options);
            drills.AddRange(hands);
            produced.Add((HandsRuleId(type), DrillKinds.Hand, hands.Count, attempts));
        }

        var conflicts = ConflictChecker.Check(drills, content.Rules, options.Classifier);

        var reports = produced.Select(p => new RuleReport(
            p.RuleId,
            p.Kind,
            p.Count,
            p.Attempts,
            p.Count < options.WarnBelow ? [$"only {p.Count} drills (warning threshold {options.WarnBelow})"] : [],
            conflicts.Where(c => c.RuleId == p.RuleId || c.OtherRuleId == p.RuleId).Sum(c => c.Count))).ToList();

        var file = new DrillFile(SchemaVersion, options.Seed, RuleSummaries(content), TypeSummaries(content), drills);
        return new GenerationResult(file, reports, conflicts);
    }

    /// <summary>
    /// Deals random spots until <see cref="GeneratorOptions.PerRule"/> distinct matches or the attempt cap.
    /// A deal is rejected when it doesn't match the rule, or when hero's hand doesn't fit the preflop
    /// range for the sampled seat (e.g. 93o is never opened UTG).
    /// </summary>
    public static (List<Drill> Drills, int Attempts) GenerateForRule(Rule rule, ContentSet content, GeneratorOptions options)
    {
        var template = LineTemplate.For(rule.Line);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        return DealSpots(rule, content, options, "rule", options.PerRule,
            d => ActionDrill(UniqueId($"{rule.Id}-{Rng.Fnv1a32(d.Key):x8}", ids), rule, template, d));
    }

    /// <summary>A dealt and seated spot that matches a rule, its earlier streets played by the rules.</summary>
    private sealed record Dealt(
        IReadOnlyList<Card> Hole,
        IReadOnlyList<Card> Board,
        SpotFacts Facts,
        Spot Spot,
        StatLine Stats,
        IReadOnlyList<SeatPlayer> Players,
        IReadOnlyList<Position> Behind,
        IReadOnlyList<Position> Others,
        IReadOnlyList<StreetDecision> Earlier,
        string Key);

    /// <summary>
    /// Deals spots for <paramref name="rule"/> until <paramref name="target"/> of them are made into drills by
    /// <paramref name="make"/> (null = rejected) or the attempt cap. <paramref name="stream"/> names the random
    /// streams, so single drills ("rule") and hands ("hand") of the same rule are dealt independently.
    /// </summary>
    private static (List<Drill> Drills, int Attempts) DealSpots(Rule rule, ContentSet content, GeneratorOptions options,
        string stream, int target, Func<Dealt, Drill?> make)
    {
        var types = content.Types;
        var template = LineTemplate.For(rule.Line);
        var villain = types.Single(t => t.Id == rule.VillainType);
        var rng = Rng.ForLabel(options.Seed, $"{stream}:{rule.Id}");
        var prefix = stream == "rule" ? "" : stream + "-"; // single drills keep their original stream names
        var deck = new Deck(rng);
        var seenSpots = new HashSet<string>(StringComparer.Ordinal);
        var drills = new List<Drill>();
        var attempts = 0;

        while (drills.Count < target && attempts < options.MaxAttempts)
        {
            attempts++;
            deck.Reset();
            var hole = deck.Deal(2);
            var board = deck.Deal(template.BoardCardCount);

            var facts = SpotFacts.Analyze(hole, board, options.Classifier);
            if (!RuleMatcher.MatchesCards(rule, villain.Id, rule.Line, facts)) continue;

            var spot = template.TryBuild(rng, hole, board, content.Ranges);
            if (spot is null) continue;

            // Seat the other players and let them act by type. The seating has its own random stream (one per
            // attempt), so it never changes the cards; seatings whose players would break the line are redrawn.
            var seatRng = Rng.ForLabel(options.Seed, $"{prefix}seats:{rule.Id}:{attempts}");
            if (SeatTable(template, spot, villain, types, seatRng) is not { } seated) continue;
            spot = seated.Spot;
            var seatTypes = seated.Types;
            var behind = TableSeating.SeatsBehind(spot.HeroPosition, spot.Actions);
            var others = TableSeating.OtherOpponents(spot.HeroPosition, spot.VillainPosition, spot.Actions);
            var seating = new Seating(behind.Select(p => seatTypes[p].Id).ToList(), others.Select(p => seatTypes[p].Id).ToList());
            if (!RuleMatcher.MatchesSeating(rule, seating)) continue;

            // For rules about the seating, who sits behind / who else is in the hand is part of what makes a spot distinct.
            var key = SpotKey(hole, board, spot);
            if (rule.HasBehindConditions) key += "|" + string.Join(",", behind.Select(p => $"{p}:{seatTypes[p].Id}"));
            if (rule.HasOthersConditions) key += "|in:" + string.Join(",", others.Select(p => $"{p}:{seatTypes[p].Id}"));
            if (!seenSpots.Add(key)) continue;

            var stats = StatSampler.SampleUnambiguous(villain, types, rng);

            // Turn and river spots: the earlier streets follow the established rules. Hero plays each street as the
            // matching rule (or the default) says; villain calls a bet only as often as their fold-to-c-bet allows.
            // Villain's calls come from their own stream, so the seating never changes which spots are reached.
            var earlier = new List<StreetDecision>();
            if (template is SrpLaterStreetLine later)
            {
                var pathRng = Rng.ForLabel(options.Seed, $"{prefix}path:{rule.Id}:{attempts}");
                var callChance = 1 - stats.FoldToCbet / 100.0;
                var played = later.FollowRules(spot.HeroPosition, board,
                    (street, seen) =>
                    {
                        var decision = HeroPlay(street, hole, seen, villain.Id, content.Rules, options.Classifier, EarlierOf(earlier));
                        earlier.Add(decision);
                        return decision.Play;
                    },
                    () => pathRng.NextDouble() < callChance);
                if (played is null) continue;
                if (!RuleMatcher.MatchesEarlier(rule, EarlierOf(earlier))) continue; // e.g. a barrel rule needs a flop bet
                spot = played;
            }

            var players = SeatPlayers(spot, seatTypes, seated.Stats, stats);
            if (make(new Dealt(hole, board, facts, spot, stats, players, behind, others, earlier, key)) is { } drill)
            {
                drills.Add(drill);
            }
        }

        return (drills, attempts);
    }

    /// <summary>
    /// Rules a hand drill can end at: turn and river rules (villain checked to hero on every earlier street) whose
    /// villain type also has a rule for one of those earlier streets, so the hand asks at least two decisions. That
    /// earlier rule's answer must fit the anchor's <see cref="Rule.Earlier"/> condition for its street (a barrel rule
    /// that needs a flop bet can't follow a flop rule that checks).
    /// </summary>
    public static IReadOnlyList<Rule> HandAnchors(string villainType, IReadOnlyList<Rule> rules) =>
        rules
            .Where(r => r.VillainType == villainType && LineTemplate.For(r.Line) is SrpLaterStreetLine later
                && rules.Any(e => e.VillainType == villainType && later.EarlierStreets.Any(s => CheckedToHeroLine(s) == e.Line
                    && (!r.Earlier.TryGetValue(s, out var needed) || needed == ActionOf(PlayFor(e.Correct))))))
            .OrderBy(r => r.Id, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Whole hands against one villain type. For each anchor rule, deals spots that end at it and keeps those where
    /// a rule also decided an earlier street. Each street a rule decided becomes a decision, in order, and the anchor
    /// is the last one; streets no rule covers are played by the default and shown as part of the story.
    /// </summary>
    public static (List<Drill> Drills, int Attempts) GenerateHands(PlayerType type, ContentSet content, GeneratorOptions options)
    {
        var ruleId = HandsRuleId(type);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var hands = new List<Drill>();
        var attempts = 0;
        foreach (var anchor in HandAnchors(type.Id, content.Rules))
        {
            var (made, tried) = DealSpots(anchor, content, options, "hand", options.HandsPerRule,
                d => d.Earlier.Any(e => e.Rule is not null)
                    ? HandDrill(UniqueId($"{ruleId}-{Rng.Fnv1a32(d.Key):x8}", ids), ruleId, anchor, d, options.Classifier)
                    : null);
            hands.AddRange(made);
            attempts += tried;
        }
        return (hands, attempts);
    }

    /// <summary>
    /// A type for every seat except hero's: villain gets the rule's type, the others are drawn by
    /// <see cref="PlayerType.TableShare"/>.
    /// </summary>
    public static Dictionary<Position, PlayerType> DrawSeatTypes(Spot spot, PlayerType villain, IReadOnlyList<PlayerType> types, Rng rng)
    {
        var seats = new Dictionary<Position, PlayerType>();
        foreach (var seat in Enum.GetValues<Position>())
        {
            if (seat == spot.HeroPosition) continue;
            seats[seat] = seat == spot.VillainPosition ? villain : StatSampler.PickByTableShare(types, rng);
        }
        return seats;
    }

    private const int MaxSeatings = 500; // a cold-caller in the small blind is rare: ~3% of seatings fit

    /// <summary>
    /// Draws who sits where (types and HUD lines) and lets them act by type; redraws when somebody would break
    /// the line (e.g. a maniac opening before hero). Null if no seating fits within <see cref="MaxSeatings"/> tries.
    /// </summary>
    public static (Spot Spot, Dictionary<Position, PlayerType> Types, Dictionary<Position, StatLine> Stats)? SeatTable(
        LineTemplate template, Spot spot, PlayerType villain, IReadOnlyList<PlayerType> types, Rng rng)
    {
        for (var i = 0; i < MaxSeatings; i++)
        {
            var seatTypes = DrawSeatTypes(spot, villain, types, rng);
            var stats = seatTypes
                .Where(s => s.Key != spot.VillainPosition)
                .ToDictionary(s => s.Key, s => StatSampler.SampleUnambiguous(s.Value, types, rng));
            if (template.ApplySeating(spot, stats, rng) is { } played) return (played, seatTypes, stats);
        }
        return null;
    }

    /// <summary>The seated players with their HUD lines (villain's comes from the rule's own stream).</summary>
    public static IReadOnlyList<SeatPlayer> SeatPlayers(Spot spot, IReadOnlyDictionary<Position, PlayerType> seatTypes,
        IReadOnlyDictionary<Position, StatLine> otherStats, StatLine villainStats) =>
        seatTypes
            .OrderBy(s => s.Key)
            .Select(s => new SeatPlayer(s.Key.ToString(), s.Value.Id,
                s.Key == spot.VillainPosition ? villainStats : otherStats[s.Key]))
            .ToList();

    /// <summary>The line whose rules decide hero's play when villain checks to hero on <paramref name="street"/>.</summary>
    public static LineId? CheckedToHeroLine(Street street) => street switch
    {
        Street.Flop => LineId.SRP_HeroIP_FlopVillainChecks,
        Street.Turn => LineId.SRP_HeroIP_TurnVillainChecks,
        _ => null,
    };

    /// <summary>
    /// Hero's play on an earlier street when villain checks: the answer of the rule that covers that street
    /// (<see cref="CheckedToHeroLine"/> rules for this villain type, given what hero did on the streets before;
    /// first match by id), else the default.
    /// </summary>
    public static StreetDecision HeroPlay(Street street, IReadOnlyList<Card> hole, IReadOnlyList<Card> boardSoFar,
        string villainType, IReadOnlyList<Rule> rules, ClassifierOptions classifier,
        IReadOnlyDictionary<Street, EarlierAction>? before = null)
    {
        var facts = SpotFacts.Analyze(hole, boardSoFar, classifier);
        var line = CheckedToHeroLine(street);
        var rule = rules
            .Where(r => r.Line == line)
            .OrderBy(r => r.Id, StringComparer.Ordinal)
            .FirstOrDefault(r => RuleMatcher.Matches(r, villainType, r.Line, facts, Seating.Empty, before));
        if (rule is not null) return new StreetDecision(street, PlayFor(rule.Correct), rule, $"rule {rule.Id}");
        var draws = facts.Draws == DrawFlags.None ? "" : " + " + string.Join(", ", DrawNames(facts.Draws));
        return new StreetDecision(street, DefaultPlay(facts), null, $"default for {facts.HandClass}{draws}");
    }

    /// <summary>
    /// Placeholder default for streets no rule covers: bet 75% with top pair good kicker or better, bet 33% with
    /// weaker pairs, flush draws and open-enders, check everything else.
    /// </summary>
    public static StreetPlay DefaultPlay(SpotFacts facts)
    {
        if (facts.HandClass >= HandClass.TopPairGoodKicker) return StreetPlay.Bet75Call;
        if (facts.HandClass >= HandClass.SecondPair || (facts.Draws & (DrawFlags.FlushDraw | DrawFlags.OpenEnded)) != 0)
            return StreetPlay.Bet33Call;
        return StreetPlay.CheckThrough;
    }

    private static StreetPlay PlayFor(string answer) => answer switch
    {
        "Bet33" => StreetPlay.Bet33Call,
        "Bet75" => StreetPlay.Bet75Call,
        _ => StreetPlay.CheckThrough,
    };

    private static EarlierAction ActionOf(StreetPlay play) => play == StreetPlay.CheckThrough ? EarlierAction.Check : EarlierAction.Bet;

    /// <summary>What hero did on the streets played so far, for rules with <see cref="Rule.Earlier"/> conditions.</summary>
    public static IReadOnlyDictionary<Street, EarlierAction> EarlierOf(IEnumerable<StreetDecision> played) =>
        played.ToDictionary(d => d.Street, d => ActionOf(d.Play));

    /// <summary>
    /// The same, read back from a spot's action steps: hero's first check or bet on each postflop street hero has
    /// acted on (at a turn or river decision these are exactly the earlier streets).
    /// </summary>
    public static IReadOnlyDictionary<Street, EarlierAction> EarlierOf(IEnumerable<DrillAction> actions, string? heroSeat) =>
        actions
            .Where(a => a.Seat == heroSeat && a.Street != nameof(Street.Preflop) && a.Kind is nameof(ActionKind.Check) or nameof(ActionKind.Bet))
            .GroupBy(a => a.Street)
            .ToDictionary(g => Enum.Parse<Street>(g.Key), g => g.First().Kind == nameof(ActionKind.Bet) ? EarlierAction.Bet : EarlierAction.Check);

    /// <summary>"Flop: bet 33%, called (rule overfolder-flop-stab)".</summary>
    public static string Describe(StreetDecision d)
    {
        var play = d.Play switch
        {
            StreetPlay.Bet33Call => "bet 33%, called",
            StreetPlay.Bet75Call => "bet 75%, called",
            _ => "check",
        };
        return $"{d.Street}: {play} ({d.Why})";
    }

    /// <summary>A drill's seating (types behind hero, types of the other opponents), recomputed from its own actions.</summary>
    public static Seating SeatingOf(Drill drill)
    {
        if (drill.Kind != DrillKinds.Action) return Seating.Empty;
        var actions = drill.Actions.Select(a => new ActionStep(
            Enum.Parse<Street>(a.Street), Enum.Parse<Position>(a.Seat), Enum.Parse<ActionKind>(a.Kind), (decimal)a.To)).ToList();
        var hero = Enum.Parse<Position>(drill.HeroPosition!);
        var villain = Enum.Parse<Position>(drill.VillainPosition!);
        string TypeAt(Position p) => drill.Players.Single(x => x.Seat == p.ToString()).Type;
        return new Seating(
            TableSeating.SeatsBehind(hero, actions).Select(TypeAt).ToList(),
            TableSeating.OtherOpponents(hero, villain, actions).Select(TypeAt).ToList());
    }

    /// <summary>
    /// Duplicate key: hand + board. Preflop spots have no board, so the seating and any extra limpers take its place.
    /// </summary>
    public static string SpotKey(IReadOnlyList<Card> hole, IReadOnlyList<Card> board, Spot spot)
    {
        var hand = string.Concat(hole.OrderByDescending(c => c.Index));
        if (board.Count == 0)
        {
            var extraLimpers = spot.Actions.Where(a => a.Kind == ActionKind.Limp && a.Seat != spot.VillainPosition).Select(a => $"+{a.Seat}");
            return $"{hand}|{spot.HeroPosition}v{spot.VillainPosition}{string.Concat(extraLimpers)}";
        }
        return $"{hand}|{string.Concat(board.OrderByDescending(c => c.Index))}";
    }

    /// <summary>Samples stat lines for a type and keeps those inside exactly one type's ranges.</summary>
    public static (List<Drill> Drills, int Samples) GenerateIdentify(PlayerType type, IReadOnlyList<PlayerType> types, GeneratorOptions options)
    {
        var rng = Rng.ForLabel(options.Seed, "identify:" + type.Id);
        var ruleId = IdentifyRuleId(type);
        var seen = new HashSet<StatLine>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var drills = new List<Drill>();
        IReadOnlyList<DrillOption> typeOptions = types.Select(t => new DrillOption(t.Id, t.Name)).ToList();

        for (var i = 0; i < options.IdentifySamplesPerType; i++)
        {
            var stats = StatSampler.Sample(type, rng);
            if (StatSampler.TypesContaining(types, stats).Count != 1) continue;
            if (!seen.Add(stats)) continue;

            drills.Add(new Drill
            {
                Id = UniqueId($"{ruleId}-{Rng.Fnv1a32(stats.ToString()):x8}", ids),
                RuleId = ruleId,
                Kind = DrillKinds.Identify,
                Line = IdentifyLine,
                VillainType = type.Id,
                VillainStats = stats,
                ActionHistory = [],
                Actions = [],
                Players = [],
                Behind = [],
                Others = [],
                HeroCards = [],
                Board = [],
                Question = IdentifyQuestion,
                Options = typeOptions,
                Correct = type.Id,
                Reason = IdentifyReason(type),
            });
        }

        return (drills, options.IdentifySamplesPerType);
    }

    /// <summary>"CallingStation" -> "identify-calling-station".</summary>
    public static string IdentifyRuleId(PlayerType type) => ContentLoader.IdentifyRulePrefix + Kebab(type.Id);

    /// <summary>"CallingStation" -> "hands-calling-station".</summary>
    public static string HandsRuleId(PlayerType type) => ContentLoader.HandsRulePrefix + Kebab(type.Id);

    private static string Kebab(string name)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c) && i > 0) sb.Append('-');
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    public static string IdentifyReason(PlayerType type)
    {
        var ranges = string.Join(", ", StatKeys.All.Select(k => $"{k} {type.Ranges[k]}"));
        return $"{type.Name}: {type.Description} Ranges: {ranges}.".Replace("  ", " ");
    }

    public static string DescribeConditions(Rule rule)
    {
        var parts = new List<string>();
        if (rule.HeroGroup is { } groups) parts.Add($"Hero hand group: {string.Join(", ", groups)}");
        if (rule.Hero is { } h) parts.Add(h.Min == h.Max ? $"Hero: {h.Min}" : $"Hero: {h.Min} to {h.Max}");
        if (rule.Draws == DrawRequirement.None) parts.Add("no draws");
        if (rule.BoardRequire != BoardFlags.None) parts.Add($"board must be {string.Join(", ", BoardAnalyzer.Names(rule.BoardRequire))}");
        if (rule.BoardExclude != BoardFlags.None) parts.Add($"board not {string.Join(", ", BoardAnalyzer.Names(rule.BoardExclude))}");
        if (rule.BehindRequire.Count > 0) parts.Add($"left to act behind you: {string.Join(" and ", rule.BehindRequire)}");
        if (rule.BehindExclude.Count > 0) parts.Add($"no {string.Join(" or ", rule.BehindExclude)} left to act behind you");
        if (rule.OthersRequire.Count > 0) parts.Add($"also in the hand: {string.Join(" and ", rule.OthersRequire)}");
        if (rule.OthersExclude.Count > 0) parts.Add($"no {string.Join(" or ", rule.OthersExclude)} also in the hand");
        foreach (var (street, action) in rule.Earlier.OrderBy(e => e.Key))
        {
            var name = street.ToString().ToLowerInvariant();
            parts.Add(action == EarlierAction.Bet ? $"you bet the {name} and got called" : $"the {name} checked through");
        }
        return string.Join("; ", parts);
    }

    public static IReadOnlyList<string> DrawNames(DrawFlags draws) =>
        Enum.GetValues<DrawFlags>().Where(f => f != DrawFlags.None && (draws & f) != 0).Select(f => f.ToString()).ToArray();

    private static Drill ActionDrill(string id, Rule rule, LineTemplate template, Dealt d) => new()
    {
        Players = d.Players,
        Behind = d.Behind.Select(p => p.ToString()).ToList(),
        Others = d.Others.Select(p => p.ToString()).ToList(),
        Id = id,
        RuleId = rule.Id,
        Kind = DrillKinds.Action,
        Line = rule.Line.ToString(),
        VillainType = rule.VillainType,
        VillainStats = d.Stats,
        HeroPosition = d.Spot.HeroPosition.ToString(),
        VillainPosition = d.Spot.VillainPosition.ToString(),
        Stacks = new Stacks((double)d.Spot.HeroStack, (double)d.Spot.VillainStack),
        Pot = (double)d.Spot.Pot,
        ToCall = (double)d.Spot.ToCall,
        ActionHistory = d.Spot.ActionHistory,
        Actions = DrillActions(d.Spot),
        HeroCards = Codes(d.Hole),
        Board = Codes(d.Board),
        Question = template.Question,
        Options = d.Spot.Options,
        Correct = rule.Correct,
        Reason = rule.Reason,
        Facts = FactsOf(d.Facts, d.Hole, d.Earlier.Select(Describe).ToList()),
    };

    /// <summary>
    /// One hand: a decision for every earlier street a rule decided (the spot as hero saw it then), then the anchor's.
    /// Between decisions the hand goes on the way the rules say, which is also how the student sees it continue.
    /// </summary>
    private static Drill HandDrill(string id, string ruleId, Rule anchor, Dealt d, ClassifierOptions classifier)
    {
        var hero = d.Spot.HeroPosition;
        var steps = new List<HandStep>();
        for (var i = 0; i < d.Earlier.Count; i++)
        {
            if (d.Earlier[i].Rule is not { } rule) continue;
            var street = d.Earlier[i].Street;
            var board = d.Board.Take(LineTemplate.BoardCountAt(street)).ToList();
            var spot = street == Street.Flop
                ? new FlopVillainChecksLine().Build(new FlopVillainChecksLine.Params(hero), board)
                : new TurnVillainChecksLine().Build(new SrpLaterStreetLine.Params(hero, d.Earlier[0].Play), board);
            steps.Add(Step(rule, spot, d.Hole, board, d.Earlier.Take(i), classifier));
        }
        steps.Add(Step(anchor, d.Spot, d.Hole, d.Board, d.Earlier, classifier));

        return new Drill
        {
            Id = id,
            RuleId = ruleId,
            Kind = DrillKinds.Hand,
            Line = HandLine,
            VillainType = anchor.VillainType,
            VillainStats = d.Stats,
            HeroPosition = hero.ToString(),
            VillainPosition = d.Spot.VillainPosition.ToString(),
            ActionHistory = [],
            Actions = [],
            Players = d.Players,
            Behind = [],
            Others = [],
            HeroCards = Codes(d.Hole),
            Board = Codes(d.Board),
            Question = HandQuestion,
            Options = [],
            Correct = "",
            Reason = "",
            Steps = steps,
        };
    }

    private static HandStep Step(Rule rule, Spot spot, IReadOnlyList<Card> hole, IReadOnlyList<Card> board,
        IEnumerable<StreetDecision> before, ClassifierOptions classifier) => new()
    {
        RuleId = rule.Id,
        Line = rule.Line.ToString(),
        Pot = (double)spot.Pot,
        ToCall = (double)spot.ToCall,
        Stacks = new Stacks((double)spot.HeroStack, (double)spot.VillainStack),
        ActionHistory = spot.ActionHistory,
        Actions = DrillActions(spot),
        Board = Codes(board),
        Question = LineTemplate.For(rule.Line).Question,
        Options = spot.Options,
        Correct = rule.Correct,
        Reason = rule.Reason,
        Facts = FactsOf(SpotFacts.Analyze(hole, board, classifier), hole, before.Select(Describe).ToList()),
    };

    private static DrillFacts FactsOf(SpotFacts facts, IReadOnlyList<Card> hole, IReadOnlyList<string> path) => new(
        facts.HandClass?.ToString(),
        DrawNames(facts.Draws),
        BoardAnalyzer.Names(facts.BoardFlags),
        facts.HighCard is { } high ? Ranks.ToChar(high).ToString(CultureInfo.InvariantCulture) : null,
        facts.HandGroup?.ToString(),
        PreflopGroups.Notation(hole),
        path);

    private static IReadOnlyList<DrillAction> DrillActions(Spot spot) =>
        spot.Actions.Select(a => new DrillAction(a.Street.ToString(), a.Seat.ToString(), a.Kind.ToString(), (double)a.To)).ToList();

    private static IReadOnlyList<string> Codes(IEnumerable<Card> cards) => cards.Select(c => c.ToString()).ToList();

    private static string UniqueId(string id, HashSet<string> used)
    {
        var candidate = id;
        for (var n = 2; !used.Add(candidate); n++) candidate = $"{id}-{n}";
        return candidate;
    }

    private static IReadOnlyList<RuleSummary> RuleSummaries(ContentSet content) =>
        content.Rules.OrderBy(r => r.Id, StringComparer.Ordinal)
            .Select(r => new RuleSummary(r.Id, DrillKinds.Action, r.VillainType, r.Line.ToString(), DescribeConditions(r),
                r.Correct, r.Reason, r.Placeholder))
            .Concat(content.Types.Select(t => new RuleSummary(IdentifyRuleId(t), DrillKinds.Identify, t.Id, IdentifyLine,
                "Stat line inside this type's ranges only", t.Id, IdentifyReason(t), t.Placeholder)))
            .Concat(content.Types.Where(t => HandAnchors(t.Id, content.Rules).Count > 0).Select(t => new RuleSummary(
                HandsRuleId(t), DrillKinds.Hand, t.Id, HandLine,
                $"One hand, street by street, ending at {string.Join(" or ", HandAnchors(t.Id, content.Rules).Select(r => r.Id))}; "
                + "every earlier street a rule covers is a decision too (at least one), the other streets follow the default play",
                "Each decision has its own rule",
                "Each decision is graded by its own rule. After a wrong answer the hand goes on along the correct line.",
                content.Rules.Any(r => r.VillainType == t.Id && r.Placeholder))))
            .ToList();

    private static IReadOnlyList<TypeSummary> TypeSummaries(ContentSet content) =>
        content.Types.Select(t => new TypeSummary(t.Id, t.Name, t.SeatLabel, t.Description,
            StatKeys.All.ToDictionary(k => k, k => new[] { t.Ranges[k].Min, t.Ranges[k].Max }))).ToList();
}

public static class ConflictChecker
{
    /// <summary>
    /// Re-analyses every decision (action drills, and each step of a hand) from its cards and seating and tests it
    /// against every other rule on the same line. Another rule matching (same villain type, hand, board and players
    /// behind) with a different correct answer is a conflict. One entry per (rule, other rule) pair, with a count and
    /// an example.
    /// </summary>
    public static IReadOnlyList<Conflict> Check(IEnumerable<Drill> drills, IReadOnlyList<Rule> rules, ClassifierOptions options)
    {
        var rulesByLine = rules.GroupBy(r => r.Line).ToDictionary(g => g.Key, g => g.ToList());
        var found = new Dictionary<(string Rule, string Other), (int Count, Drill Example, string Correct, string OtherCorrect)>();

        foreach (var drill in drills)
        {
            foreach (var (ruleId, line, correct, boardCodes, seating, actions) in Decisions(drill))
            {
                if (!rulesByLine.TryGetValue(line, out var candidates)) continue;

                var hole = drill.HeroCards.Select(Card.Parse).ToArray();
                var board = boardCodes.Select(Card.Parse).ToArray();
                var facts = SpotFacts.Analyze(hole, board, options);
                var earlier = DrillGenerator.EarlierOf(actions, drill.HeroPosition);

                foreach (var other in candidates)
                {
                    if (other.Id == ruleId || other.Correct == correct) continue;
                    if (!RuleMatcher.Matches(other, drill.VillainType, line, facts, seating, earlier)) continue;

                    var key = (ruleId, other.Id);
                    found[key] = found.TryGetValue(key, out var f)
                        ? (f.Count + 1, f.Example, f.Correct, f.OtherCorrect)
                        : (1, drill, correct, other.Correct);
                }
            }
        }

        return found
            .OrderBy(kv => kv.Key.Rule, StringComparer.Ordinal)
            .ThenBy(kv => kv.Key.Other, StringComparer.Ordinal)
            .Select(kv => new Conflict(kv.Key.Rule, kv.Key.Other, kv.Value.Correct, kv.Value.OtherCorrect,
                kv.Value.Count, kv.Value.Example))
            .ToList();
    }

    /// <summary>What a drill asks: an action drill's one decision, or each step of a hand (heads-up, nobody behind).</summary>
    private static IEnumerable<(string RuleId, LineId Line, string Correct, IReadOnlyList<string> Board, Seating Seating,
        IReadOnlyList<DrillAction> Actions)> Decisions(Drill drill)
    {
        if (drill.Kind == DrillKinds.Action)
        {
            yield return (drill.RuleId, Enum.Parse<LineId>(drill.Line), drill.Correct, drill.Board, DrillGenerator.SeatingOf(drill),
                drill.Actions);
        }
        foreach (var step in drill.Steps ?? [])
        {
            yield return (step.RuleId, Enum.Parse<LineId>(step.Line), step.Correct, step.Board, Seating.Empty, step.Actions);
        }
    }
}
