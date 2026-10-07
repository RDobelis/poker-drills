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
    public ClassifierOptions Classifier { get; init; } = ClassifierOptions.Default;
}

public sealed record RuleReport(string RuleId, string Kind, int Produced, int Attempts, IReadOnlyList<string> Warnings, int Conflicts);

/// <summary>Drills of <see cref="RuleId"/> that <see cref="OtherRuleId"/> also matches, with a different answer.</summary>
public sealed record Conflict(string RuleId, string OtherRuleId, string Correct, string OtherCorrect, int Count, Drill Example);

public sealed record GenerationResult(DrillFile File, IReadOnlyList<RuleReport> Reports, IReadOnlyList<Conflict> Conflicts);

public static class DrillGenerator
{
    // 2: drills carry step-by-step `actions` for the table view. 3: `players` = a type and HUD for every seat.
    // 4: `behind` = seats left to act after hero; rules can react to them. 5: `others` = other opponents in the hand.
    public const int SchemaVersion = 5;
    public const string IdentifyLine = "Identify";
    public const string IdentifyQuestion = "Which player type is this?";

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
        var types = content.Types;
        var template = LineTemplate.For(rule.Line);
        var villain = types.Single(t => t.Id == rule.VillainType);
        var rng = Rng.ForLabel(options.Seed, "rule:" + rule.Id);
        var deck = new Deck(rng);
        var seenSpots = new HashSet<string>(StringComparer.Ordinal);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var drills = new List<Drill>();
        var attempts = 0;

        while (drills.Count < options.PerRule && attempts < options.MaxAttempts)
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
            var seatRng = Rng.ForLabel(options.Seed, $"seats:{rule.Id}:{attempts}");
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
            var id = UniqueId($"{rule.Id}-{Rng.Fnv1a32(key):x8}", ids);
            var players = SeatPlayers(spot, seatTypes, seated.Stats, stats);
            drills.Add(ActionDrill(id, rule, template, stats, spot, hole, board, facts, players, behind, others));
        }

        return (drills, attempts);
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
    public static string IdentifyRuleId(PlayerType type)
    {
        var sb = new StringBuilder(ContentLoader.IdentifyRulePrefix);
        for (var i = 0; i < type.Id.Length; i++)
        {
            var c = type.Id[i];
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
        return string.Join("; ", parts);
    }

    public static IReadOnlyList<string> DrawNames(DrawFlags draws) =>
        Enum.GetValues<DrawFlags>().Where(f => f != DrawFlags.None && (draws & f) != 0).Select(f => f.ToString()).ToArray();

    private static Drill ActionDrill(string id, Rule rule, LineTemplate template, StatLine stats, Spot spot,
        IReadOnlyList<Card> hole, IReadOnlyList<Card> board, SpotFacts facts, IReadOnlyList<SeatPlayer> players,
        IReadOnlyList<Position> behind, IReadOnlyList<Position> others) => new()
    {
        Players = players,
        Behind = behind.Select(p => p.ToString()).ToList(),
        Others = others.Select(p => p.ToString()).ToList(),
        Id = id,
        RuleId = rule.Id,
        Kind = DrillKinds.Action,
        Line = rule.Line.ToString(),
        VillainType = rule.VillainType,
        VillainStats = stats,
        HeroPosition = spot.HeroPosition.ToString(),
        VillainPosition = spot.VillainPosition.ToString(),
        Stacks = new Stacks((double)spot.HeroStack, (double)spot.VillainStack),
        Pot = (double)spot.Pot,
        ToCall = (double)spot.ToCall,
        ActionHistory = spot.ActionHistory,
        Actions = spot.Actions
            .Select(a => new DrillAction(a.Street.ToString(), a.Seat.ToString(), a.Kind.ToString(), (double)a.To))
            .ToList(),
        HeroCards = hole.Select(c => c.ToString()).ToList(),
        Board = board.Select(c => c.ToString()).ToList(),
        Question = template.Question,
        Options = spot.Options,
        Correct = rule.Correct,
        Reason = rule.Reason,
        Facts = new DrillFacts(
            facts.HandClass?.ToString(),
            DrawNames(facts.Draws),
            BoardAnalyzer.Names(facts.BoardFlags),
            facts.HighCard is { } high ? Ranks.ToChar(high).ToString(CultureInfo.InvariantCulture) : null,
            facts.HandGroup?.ToString(),
            PreflopGroups.Notation(hole)),
    };

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
            .ToList();

    private static IReadOnlyList<TypeSummary> TypeSummaries(ContentSet content) =>
        content.Types.Select(t => new TypeSummary(t.Id, t.Name, t.SeatLabel, t.Description,
            StatKeys.All.ToDictionary(k => k, k => new[] { t.Ranges[k].Min, t.Ranges[k].Max }))).ToList();
}

public static class ConflictChecker
{
    /// <summary>
    /// Re-analyses every action drill from its cards and seating and tests it against every other rule on the
    /// same line. Another rule matching (same villain type, hand, board and players behind) with a different
    /// correct answer is a conflict. One entry per (rule, other rule) pair, with a count and an example.
    /// </summary>
    public static IReadOnlyList<Conflict> Check(IEnumerable<Drill> drills, IReadOnlyList<Rule> rules, ClassifierOptions options)
    {
        var rulesByLine = rules.GroupBy(r => r.Line).ToDictionary(g => g.Key, g => g.ToList());
        var found = new Dictionary<(string Rule, string Other), (int Count, Drill Example, string OtherCorrect)>();

        foreach (var drill in drills.Where(d => d.Kind == DrillKinds.Action))
        {
            var line = Enum.Parse<LineId>(drill.Line);
            if (!rulesByLine.TryGetValue(line, out var candidates)) continue;

            var hole = drill.HeroCards.Select(Card.Parse).ToArray();
            var board = drill.Board.Select(Card.Parse).ToArray();
            var facts = SpotFacts.Analyze(hole, board, options);
            var seating = DrillGenerator.SeatingOf(drill);

            foreach (var other in candidates)
            {
                if (other.Id == drill.RuleId || other.Correct == drill.Correct) continue;
                if (!RuleMatcher.Matches(other, drill.VillainType, line, facts, seating)) continue;

                var key = (drill.RuleId, other.Id);
                found[key] = found.TryGetValue(key, out var f)
                    ? (f.Count + 1, f.Example, f.OtherCorrect)
                    : (1, drill, other.Correct);
            }
        }

        return found
            .OrderBy(kv => kv.Key.Rule, StringComparer.Ordinal)
            .ThenBy(kv => kv.Key.Other, StringComparer.Ordinal)
            .Select(kv => new Conflict(kv.Key.Rule, kv.Key.Other, kv.Value.Example.Correct, kv.Value.OtherCorrect,
                kv.Value.Count, kv.Value.Example))
            .ToList();
    }
}
