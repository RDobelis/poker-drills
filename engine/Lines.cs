using System.Globalization;

namespace PokerDrills.Engine;

/// <summary>6-max seats in preflop action order.</summary>
public enum Position { UTG, MP, CO, BTN, SB, BB }

public enum Street { Preflop, Flop, Turn, River }

public enum LineId
{
    Pre_IsoVsLimper,
    SRP_HeroIP_FlopVillainChecks,
    SRP_HeroOOP_FacingFlopCbet,
    SRP_HeroIP_RiverVillainChecks,
    SRP_HeroIP_FacingRiverBet,
}

public enum ActionKind { Post, Fold, Limp, Raise, Call, Check, Bet }

/// <summary>
/// One action at the table, in order. <see cref="To"/> is the seat's total in front of it on this street
/// after the action (0 for fold and check), so chips added = To minus the seat's previous total.
/// </summary>
public sealed record ActionStep(Street Street, Position Seat, ActionKind Kind, decimal To);

public static class TableSeating
{
    /// <summary>
    /// Seats still to act after hero at the decision: after hero in preflop order, not folded, and with no
    /// voluntary action yet (posting a blind doesn't count). Postflop this is empty: everyone else has folded
    /// and villain has already acted.
    /// </summary>
    public static IReadOnlyList<Position> SeatsBehind(Position hero, IEnumerable<ActionStep> actions)
    {
        var acted = actions.Where(a => a.Kind != ActionKind.Post).Select(a => a.Seat).ToHashSet();
        return Enum.GetValues<Position>().Where(p => p > hero && !acted.Contains(p)).ToList();
    }
}

/// <summary>Game constants and money helpers. All amounts are big blinds.</summary>
public static class Stakes
{
    public const decimal SmallBlind = 0.5m;
    public const decimal BigBlind = 1m;
    public const decimal StartingStack = 100m;
    public const decimal OpenSize = 2.5m;
    public const decimal RaiseMultiplier = 3m;

    /// <summary>Pot after a single-raised pot is called by the BB: open + call + dead SB.</summary>
    public const decimal SrpPot = OpenSize * 2 + SmallBlind;

    /// <summary><paramref name="percent"/>% of the pot, rounded to 0.1bb (midpoint away from zero).</summary>
    public static decimal Bet(decimal pot, int percent) =>
        Math.Round(pot * percent / 100m, 1, MidpointRounding.AwayFromZero);

    public static string Bb(decimal amount) => amount.ToString("0.##", CultureInfo.InvariantCulture) + "bb";
}

public sealed record DrillOption(string Id, string Label);

/// <summary>
/// A fully built decision point. Stacks are what each player has behind at the decision.
/// <see cref="Actions"/> is the same story as <see cref="ActionHistory"/>, step by step, for the table view.
/// </summary>
public sealed record Spot(
    LineId Line,
    Position HeroPosition,
    Position VillainPosition,
    decimal Pot,
    decimal ToCall,
    decimal HeroStack,
    decimal VillainStack,
    IReadOnlyList<string> ActionHistory,
    IReadOnlyList<ActionStep> Actions,
    IReadOnlyList<DrillOption> Options);

public abstract class LineTemplate
{
    public abstract LineId Id { get; }
    public abstract Street DecisionStreet { get; }
    public abstract IReadOnlyList<string> OptionIds { get; }
    public abstract string Question { get; }

    /// <summary>Whether players can still be left to act behind hero at the decision (only preflop spots).</summary>
    public bool CanHavePlayersBehind => DecisionStreet == Street.Preflop;

    public int BoardCardCount => DecisionStreet switch
    {
        Street.Preflop => 0,
        Street.Flop => 3,
        Street.Turn => 4,
        _ => 5,
    };

    /// <summary>Randomises positions and earlier-street action within the template's limits.</summary>
    public abstract Spot Build(Rng rng, IReadOnlyList<Card> board);

    /// <summary>
    /// Like <see cref="Build(Rng, IReadOnlyList{Card})"/>, but the seating must fit hero's hand: hero only
    /// opens hands in that seat's opening range and only flat-calls in the BB with hands in the calling range
    /// against the opener. The seat is drawn uniformly first; null means it doesn't fit and the deal is rejected,
    /// so weak hands mostly end up opened from late position, as in real games.
    /// </summary>
    public abstract Spot? TryBuild(Rng rng, IReadOnlyList<Card> hole, IReadOnlyList<Card> board, PreflopRanges ranges);

    /// <summary>
    /// Lets the players this line shows folding act according to their own HUD line (<see cref="PreflopBehaviour"/>).
    /// Returns the spot as they played it, or null when one of them would have done something the line can't show
    /// (here: anything but fold). <paramref name="others"/> has a HUD line for every seat except hero's and villain's.
    /// </summary>
    public virtual Spot? ApplySeating(Spot spot, IReadOnlyDictionary<Position, StatLine> others, Rng rng)
    {
        foreach (var (seat, situation) in Folders(spot))
        {
            if (PreflopBehaviour.Draw(others[seat], situation, rng) != PreflopAction.Fold) return null;
        }
        return spot;
    }

    /// <summary>The other players the line has folding preflop, and whether somebody had raised when they acted.</summary>
    protected static IEnumerable<(Position Seat, PreflopSituation Situation)> Folders(Spot spot)
    {
        var raised = false;
        foreach (var a in spot.Actions.Where(a => a.Street == Street.Preflop))
        {
            if (a.Kind == ActionKind.Fold && a.Seat != spot.HeroPosition && a.Seat != spot.VillainPosition)
            {
                yield return (a.Seat, raised ? PreflopSituation.FacingRaise : PreflopSituation.Unopened);
            }
            if (a.Kind == ActionKind.Raise) raised = true;
        }
    }

    public static IReadOnlyDictionary<LineId, LineTemplate> All { get; } = new LineTemplate[]
    {
        new IsoVsLimperLine(),
        new FlopVillainChecksLine(),
        new FacingFlopCbetLine(),
        new RiverVillainChecksLine(),
        new FacingRiverBetLine(),
    }.ToDictionary(t => t.Id);

    public static LineTemplate For(LineId id) => All[id];

    protected void RequireBoard(IReadOnlyList<Card> board)
    {
        if (board.Count != BoardCardCount)
        {
            throw new ArgumentException($"{Id} needs {BoardCardCount} board cards, got {board.Count}", nameof(board));
        }
    }

    // ---- shared text helpers ----

    protected static string Seat(string who, Position p) => $"{who} ({p})";

    /// <summary>"UTG folds." / "UTG, MP fold." / null when nobody folds.</summary>
    protected static string? Folds(IEnumerable<Position> seats)
    {
        var list = seats.ToList();
        return list.Count switch
        {
            0 => null,
            1 => $"{list[0]} folds.",
            _ => $"{string.Join(", ", list)} fold.",
        };
    }

    protected static IEnumerable<Position> Before(Position p) => Enum.GetValues<Position>().Where(x => x < p);

    protected static IEnumerable<Position> Between(Position a, Position b) =>
        Enum.GetValues<Position>().Where(x => x > a && x < b);

    protected static string Sentence(params string?[] parts) => string.Join(" ", parts.Where(p => p is not null));

    /// <summary>Preflop line for an open from <paramref name="opener"/> called by the BB.</summary>
    protected static string OpenCalledByBigBlind(Position opener, string openerName, string bbName) =>
        "Preflop: " + Sentence(
            Folds(Before(opener)),
            $"{Seat(openerName, opener)} raises to {Stakes.Bb(Stakes.OpenSize)}.",
            Folds(Between(opener, Position.BB)),
            $"{Seat(bbName, Position.BB)} calls.");

    protected static string StreetHeader(string street, IEnumerable<Card> cards, decimal pot) =>
        $"{street} [{CardList.Format(cards)}] ({Stakes.Bb(pot)}):";

    // ---- shared action-step helpers ----

    protected static List<ActionStep> PostBlinds() =>
    [
        new(Street.Preflop, Position.SB, ActionKind.Post, Stakes.SmallBlind),
        new(Street.Preflop, Position.BB, ActionKind.Post, Stakes.BigBlind),
    ];

    protected static IEnumerable<ActionStep> FoldSteps(IEnumerable<Position> seats) =>
        seats.Select(p => new ActionStep(Street.Preflop, p, ActionKind.Fold, 0m));

    /// <summary>Blinds, folds, an open from <paramref name="opener"/>, folds (SB included), BB calls.</summary>
    protected static List<ActionStep> OpenCalledByBigBlindSteps(Position opener)
    {
        var steps = PostBlinds();
        steps.AddRange(FoldSteps(Before(opener)));
        steps.Add(new ActionStep(Street.Preflop, opener, ActionKind.Raise, Stakes.OpenSize));
        steps.AddRange(FoldSteps(Between(opener, Position.BB)));
        steps.Add(new ActionStep(Street.Preflop, Position.BB, ActionKind.Call, Stakes.OpenSize));
        return steps;
    }

    protected static DrillOption BetOption(string id, int percent, decimal pot, decimal stack)
    {
        var amount = Stakes.Bet(pot, percent);
        return amount >= stack
            ? new DrillOption(id, $"All-in ({Stakes.Bb(stack)})")
            : new DrillOption(id, $"Bet {percent}% ({Stakes.Bb(amount)})");
    }

    /// <summary>Fold / Call / Raise (to 3x the bet, or all-in) against a bet.</summary>
    protected static IReadOnlyList<DrillOption> FacingBetOptions(decimal bet, decimal heroStack)
    {
        var raiseTo = Stakes.RaiseMultiplier * bet;
        var raise = raiseTo >= heroStack
            ? new DrillOption("Raise", $"All-in ({Stakes.Bb(heroStack)})")
            : new DrillOption("Raise", $"Raise to {Stakes.Bb(raiseTo)}");
        return [new("Fold", "Fold"), new("Call", $"Call {Stakes.Bb(bet)}"), raise];
    }
}

/// <summary>Villain limps from MP or CO; hero (CO or BTN, behind the limper) decides preflop.</summary>
public sealed class IsoVsLimperLine : LineTemplate
{
    public sealed record Params(Position Villain, Position Hero);

    public const decimal LimpSize = 1m;
    public const decimal SmallIso = 3m;
    public const decimal BigIso = 5m;

    public static IReadOnlyList<Params> Seatings { get; } =
        [new(Position.MP, Position.CO), new(Position.MP, Position.BTN), new(Position.CO, Position.BTN)];

    public override LineId Id => LineId.Pre_IsoVsLimper;
    public override Street DecisionStreet => Street.Preflop;
    public override IReadOnlyList<string> OptionIds { get; } = ["Fold", "Limp", "Iso3", "Iso5"];
    public override string Question => "Villain limps. What do you do?";

    public override Spot Build(Rng rng, IReadOnlyList<Card> board) => Build(rng.Pick(Seatings), board);

    /// <summary>Any hand: deciding what to do with it preflop is the drill itself.</summary>
    public override Spot? TryBuild(Rng rng, IReadOnlyList<Card> hole, IReadOnlyList<Card> board, PreflopRanges ranges) =>
        Build(rng, board);

    /// <summary>
    /// Players before hero act by type: a fold stays a fold, a limp makes an extra limper (iso sizes grow
    /// 1bb per extra limper), a raise can't be shown in this spot.
    /// </summary>
    public override Spot? ApplySeating(Spot spot, IReadOnlyDictionary<Position, StatLine> others, Rng rng)
    {
        var limpers = new List<Position>();
        foreach (var (seat, situation) in Folders(spot))
        {
            switch (PreflopBehaviour.Draw(others[seat], situation, rng))
            {
                case PreflopAction.Fold:
                    break;
                case PreflopAction.Limp:
                    limpers.Add(seat);
                    break;
                default:
                    return null;
            }
        }
        return limpers.Count == 0 ? spot : Build(new Params(spot.VillainPosition, spot.HeroPosition), [], limpers);
    }

    /// <param name="extraLimpers">Other players before hero who limp too (villain is the limper the rule is about).</param>
    public Spot Build(Params p, IReadOnlyList<Card>? board = null, IReadOnlyCollection<Position>? extraLimpers = null)
    {
        RequireBoard(board ?? []);
        if (!Seatings.Contains(p)) throw new ArgumentException($"Unsupported seating {p}", nameof(p));
        var limpers = extraLimpers ?? [];
        if (limpers.Any(s => s >= p.Hero || s == p.Villain))
        {
            throw new ArgumentException("Extra limpers must act before hero and must not be villain", nameof(extraLimpers));
        }

        var steps = PostBlinds();
        foreach (var seat in Before(p.Hero))
        {
            var limps = seat == p.Villain || limpers.Contains(seat);
            steps.Add(new ActionStep(Street.Preflop, seat, limps ? ActionKind.Limp : ActionKind.Fold, limps ? LimpSize : 0m));
        }

        var pot = Stakes.SmallBlind + Stakes.BigBlind + LimpSize * (1 + limpers.Count);
        var history = new[] { "Preflop: " + PreflopText(steps, p.Villain) };
        var extra = limpers.Count * LimpSize;
        IReadOnlyList<DrillOption> options =
        [
            new("Fold", "Fold"),
            new("Limp", $"Limp behind ({Stakes.Bb(LimpSize)})"),
            new("Iso3", $"Raise to {Stakes.Bb(SmallIso + extra)}"),
            new("Iso5", $"Raise to {Stakes.Bb(BigIso + extra)}"),
        ];
        return new Spot(Id, p.Hero, p.Villain, pot, LimpSize, Stakes.StartingStack, Stakes.StartingStack - LimpSize,
            history, steps, options);
    }

    /// <summary>"UTG folds. Villain (MP) limps 1bb. CO limps 1bb." from the preflop steps (blinds left out).</summary>
    private static string PreflopText(IEnumerable<ActionStep> steps, Position villain)
    {
        var parts = new List<string?>();
        var folds = new List<Position>();
        foreach (var s in steps.Where(s => s.Kind != ActionKind.Post))
        {
            if (s.Kind == ActionKind.Fold)
            {
                folds.Add(s.Seat);
                continue;
            }
            parts.Add(Folds(folds));
            folds.Clear();
            var who = s.Seat == villain ? Seat("Villain", s.Seat) : s.Seat.ToString();
            parts.Add($"{who} limps {Stakes.Bb(s.To)}.");
        }
        parts.Add(Folds(folds));
        return Sentence([.. parts]);
    }
}

/// <summary>Hero opens, villain calls in the BB and checks the flop.</summary>
public sealed class FlopVillainChecksLine : LineTemplate
{
    public sealed record Params(Position Hero);

    public static IReadOnlyList<Position> HeroSeats { get; } = [Position.UTG, Position.MP, Position.CO, Position.BTN];

    public override LineId Id => LineId.SRP_HeroIP_FlopVillainChecks;
    public override Street DecisionStreet => Street.Flop;
    public override IReadOnlyList<string> OptionIds { get; } = ["Check", "Bet33", "Bet75"];
    public override string Question => "Villain checks the flop. What do you do?";

    public override Spot Build(Rng rng, IReadOnlyList<Card> board) => Build(new Params(rng.Pick(HeroSeats)), board);

    public override Spot? TryBuild(Rng rng, IReadOnlyList<Card> hole, IReadOnlyList<Card> board, PreflopRanges ranges)
    {
        var seat = rng.Pick(HeroSeats);
        return ranges.CanOpen(seat, hole) ? Build(new Params(seat), board) : null;
    }

    public Spot Build(Params p, IReadOnlyList<Card> board)
    {
        RequireBoard(board);
        if (!HeroSeats.Contains(p.Hero)) throw new ArgumentException($"Unsupported hero seat {p.Hero}", nameof(p));

        var pot = Stakes.SrpPot;
        var stack = Stakes.StartingStack - Stakes.OpenSize;
        var history = new[]
        {
            OpenCalledByBigBlind(p.Hero, "Hero", "Villain"),
            $"{StreetHeader("Flop", board, pot)} Villain checks.",
        };
        var steps = OpenCalledByBigBlindSteps(p.Hero);
        steps.Add(new ActionStep(Street.Flop, Position.BB, ActionKind.Check, 0m));

        IReadOnlyList<DrillOption> options =
        [
            new("Check", "Check"),
            BetOption("Bet33", 33, pot, stack),
            BetOption("Bet75", 75, pot, stack),
        ];
        return new Spot(Id, p.Hero, Position.BB, pot, 0m, stack, stack, history, steps, options);
    }
}

/// <summary>Villain opens, hero calls in the BB, checks, and faces a 33% or 75% c-bet.</summary>
public sealed class FacingFlopCbetLine : LineTemplate
{
    public sealed record Params(Position Villain, int BetPercent);

    public static IReadOnlyList<Position> VillainSeats { get; } = [Position.UTG, Position.MP, Position.CO, Position.BTN];
    public static IReadOnlyList<int> BetPercents { get; } = [33, 75];

    public override LineId Id => LineId.SRP_HeroOOP_FacingFlopCbet;
    public override Street DecisionStreet => Street.Flop;
    public override IReadOnlyList<string> OptionIds { get; } = ["Fold", "Call", "Raise"];
    public override string Question => "Villain c-bets the flop. What do you do?";

    public override Spot Build(Rng rng, IReadOnlyList<Card> board) =>
        Build(new Params(rng.Pick(VillainSeats), rng.Pick(BetPercents)), board);

    public override Spot? TryBuild(Rng rng, IReadOnlyList<Card> hole, IReadOnlyList<Card> board, PreflopRanges ranges)
    {
        var p = new Params(rng.Pick(VillainSeats), rng.Pick(BetPercents));
        return ranges.CanCallInBigBlind(p.Villain, hole) ? Build(p, board) : null;
    }

    public Spot Build(Params p, IReadOnlyList<Card> board)
    {
        RequireBoard(board);
        if (!VillainSeats.Contains(p.Villain)) throw new ArgumentException($"Unsupported villain seat {p.Villain}", nameof(p));
        if (!BetPercents.Contains(p.BetPercent)) throw new ArgumentException($"Unsupported bet size {p.BetPercent}", nameof(p));

        var potBefore = Stakes.SrpPot;
        var stack = Stakes.StartingStack - Stakes.OpenSize;
        var bet = Math.Min(Stakes.Bet(potBefore, p.BetPercent), stack);
        var history = new[]
        {
            OpenCalledByBigBlind(p.Villain, "Villain", "Hero"),
            $"{StreetHeader("Flop", board, potBefore)} Hero checks. Villain bets {Stakes.Bb(bet)} ({p.BetPercent}%).",
        };
        var steps = OpenCalledByBigBlindSteps(p.Villain);
        steps.Add(new ActionStep(Street.Flop, Position.BB, ActionKind.Check, 0m));
        steps.Add(new ActionStep(Street.Flop, p.Villain, ActionKind.Bet, bet));

        return new Spot(Id, Position.BB, p.Villain, potBefore + bet, bet, stack, stack - bet, history, steps,
            FacingBetOptions(bet, stack));
    }
}

/// <summary>What happens on the flop or turn before the river decision.</summary>
public enum StreetPlay { CheckThrough, Bet33Call, Bet75Call }

/// <summary>
/// Hero opens, villain calls in the BB. On flop and turn villain checks and hero either checks back
/// (50%) or bets 33%/75% (25% each) and gets called. Subclasses define the river action.
/// </summary>
public abstract class SrpToRiverLine : LineTemplate
{
    public sealed record Params(Position Hero, StreetPlay Flop, StreetPlay Turn);

    public static IReadOnlyList<Position> HeroSeats { get; } = [Position.UTG, Position.MP, Position.CO, Position.BTN];

    public override Street DecisionStreet => Street.River;

    public override Spot Build(Rng rng, IReadOnlyList<Card> board) =>
        Build(new Params(rng.Pick(HeroSeats), SamplePlay(rng), SamplePlay(rng)), board);

    public override Spot? TryBuild(Rng rng, IReadOnlyList<Card> hole, IReadOnlyList<Card> board, PreflopRanges ranges)
    {
        var seat = rng.Pick(HeroSeats);
        if (!ranges.CanOpen(seat, hole)) return null;
        return Build(new Params(seat, SamplePlay(rng), SamplePlay(rng)), board);
    }

    public Spot Build(Params p, IReadOnlyList<Card> board)
    {
        RequireBoard(board);
        if (!HeroSeats.Contains(p.Hero)) throw new ArgumentException($"Unsupported hero seat {p.Hero}", nameof(p));

        var pot = Stakes.SrpPot;
        var stack = Stakes.StartingStack - Stakes.OpenSize; // both players have the same behind throughout
        var history = new List<string> { OpenCalledByBigBlind(p.Hero, "Hero", "Villain") };
        var steps = OpenCalledByBigBlindSteps(p.Hero);

        var flopHeader = StreetHeader("Flop", board.Take(3), pot);
        history.Add($"{flopHeader} {Play(p.Flop, Street.Flop, p.Hero, ref pot, ref stack, steps)}");
        var turnHeader = StreetHeader("Turn", [board[3]], pot);
        history.Add($"{turnHeader} {Play(p.Turn, Street.Turn, p.Hero, ref pot, ref stack, steps)}");

        return BuildRiver(p, board[4], pot, stack, history, steps);
    }

    protected abstract Spot BuildRiver(Params p, Card river, decimal pot, decimal stack, List<string> history, List<ActionStep> steps);

    private static StreetPlay SamplePlay(Rng rng) => rng.NextInt(4) switch
    {
        0 or 1 => StreetPlay.CheckThrough,
        2 => StreetPlay.Bet33Call,
        _ => StreetPlay.Bet75Call,
    };

    private static string Play(StreetPlay play, Street street, Position hero, ref decimal pot, ref decimal stack, List<ActionStep> steps)
    {
        steps.Add(new ActionStep(street, Position.BB, ActionKind.Check, 0m));
        if (play == StreetPlay.CheckThrough)
        {
            steps.Add(new ActionStep(street, hero, ActionKind.Check, 0m));
            return "Villain checks. Hero checks.";
        }

        var percent = play == StreetPlay.Bet33Call ? 33 : 75;
        var bet = Math.Min(Stakes.Bet(pot, percent), stack);
        pot += 2 * bet;
        stack -= bet;
        steps.Add(new ActionStep(street, hero, ActionKind.Bet, bet));
        steps.Add(new ActionStep(street, Position.BB, ActionKind.Call, bet));
        return $"Villain checks. Hero bets {Stakes.Bb(bet)} ({percent}%). Villain calls.";
    }
}

public sealed class RiverVillainChecksLine : SrpToRiverLine
{
    public override LineId Id => LineId.SRP_HeroIP_RiverVillainChecks;
    public override IReadOnlyList<string> OptionIds { get; } = ["Check", "Bet33", "Bet75", "Bet150"];
    public override string Question => "Villain checks the river. What do you do?";

    protected override Spot BuildRiver(Params p, Card river, decimal pot, decimal stack, List<string> history, List<ActionStep> steps)
    {
        history.Add($"{StreetHeader("River", [river], pot)} Villain checks.");
        steps.Add(new ActionStep(Street.River, Position.BB, ActionKind.Check, 0m));
        IReadOnlyList<DrillOption> options =
        [
            new("Check", "Check"),
            BetOption("Bet33", 33, pot, stack),
            BetOption("Bet75", 75, pot, stack),
            BetOption("Bet150", 150, pot, stack),
        ];
        return new Spot(Id, p.Hero, Position.BB, pot, 0m, stack, stack, history, steps, options);
    }
}

public sealed class FacingRiverBetLine : SrpToRiverLine
{
    public const int RiverBetPercent = 75;

    public override LineId Id => LineId.SRP_HeroIP_FacingRiverBet;
    public override IReadOnlyList<string> OptionIds { get; } = ["Fold", "Call", "Raise"];
    public override string Question => "Villain bets the river. What do you do?";

    protected override Spot BuildRiver(Params p, Card river, decimal pot, decimal stack, List<string> history, List<ActionStep> steps)
    {
        var bet = Math.Min(Stakes.Bet(pot, RiverBetPercent), stack);
        history.Add($"{StreetHeader("River", [river], pot)} Villain bets {Stakes.Bb(bet)} ({RiverBetPercent}%).");
        steps.Add(new ActionStep(Street.River, Position.BB, ActionKind.Bet, bet));
        return new Spot(Id, p.Hero, Position.BB, pot + bet, bet, stack, stack - bet, history, steps,
            FacingBetOptions(bet, stack));
    }
}
