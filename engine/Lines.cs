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
    Pre_FacingThreeBet,
    SRP_3Way_FlopCheckedToHero,
    SRP_HeroIP_TurnVillainChecks,
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

    /// <summary>
    /// Opponents still in the hand besides villain: seats that put chips in voluntarily (limp, call, raise, bet)
    /// and never folded. Extra limpers in "villain limps" spots, the small blind in 3-way pots.
    /// </summary>
    public static IReadOnlyList<Position> OtherOpponents(Position hero, Position villain, IEnumerable<ActionStep> actions)
    {
        var list = actions.ToList();
        return Enum.GetValues<Position>()
            .Where(p => p != hero && p != villain
                && list.Any(a => a.Seat == p && a.Kind is ActionKind.Limp or ActionKind.Call or ActionKind.Raise or ActionKind.Bet)
                && !list.Any(a => a.Seat == p && a.Kind == ActionKind.Fold))
            .ToList();
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

    /// <summary>Whether players can still be left to act behind hero at the decision.</summary>
    public virtual bool CanHavePlayersBehind => false;

    /// <summary>Whether opponents besides villain can still be in the hand at the decision.</summary>
    public virtual bool CanHaveOtherOpponents => false;

    public int BoardCardCount => BoardCountAt(DecisionStreet);

    /// <summary>Board cards visible on a street: 0, 3, 4, 5.</summary>
    public static int BoardCountAt(Street street) => street switch
    {
        Street.Preflop => 0,
        Street.Flop => 3,
        Street.Turn => 4,
        _ => 5,
    };

    /// <summary>The cards dealt on a street: the three flop cards, or the turn or river card.</summary>
    protected static IEnumerable<Card> DealtOn(Street street, IReadOnlyList<Card> board) =>
        street == Street.Flop ? board.Take(3) : [board[BoardCountAt(street) - 1]];

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
        new FacingThreeBetLine(),
        new ThreeWayFlopLine(),
        new TurnVillainChecksLine(),
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
    public override bool CanHavePlayersBehind => true; // the blinds (and the button when hero is CO) act after hero
    public override bool CanHaveOtherOpponents => true; // extra limpers
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

/// <summary>What hero does on a street villain checks to hero, before the decision street.</summary>
public enum StreetPlay { CheckThrough, Bet33Call, Bet75Call }

/// <summary>
/// Hero opens, villain calls in the BB and checks to hero on every street. On the streets before the decision
/// (<see cref="EarlierStreets"/>) hero checks back or bets 33%/75% and gets called: the generator plays those
/// streets by the rules (<see cref="FollowRules"/>); <see cref="Build(Rng, IReadOnlyList{Card})"/> samples them
/// (50% check, 25% each bet). Subclasses define the decision street and villain's action on it.
/// </summary>
public abstract class SrpLaterStreetLine : LineTemplate
{
    /// <param name="Turn">Hero's turn play; only river spots have one (it must stay CheckThrough on turn spots).</param>
    public sealed record Params(Position Hero, StreetPlay Flop, StreetPlay Turn = StreetPlay.CheckThrough);

    public static IReadOnlyList<Position> HeroSeats { get; } = [Position.UTG, Position.MP, Position.CO, Position.BTN];

    /// <summary>Streets hero has played before the decision: the flop, and on river spots the turn.</summary>
    public IReadOnlyList<Street> EarlierStreets => DecisionStreet == Street.Turn ? [Street.Flop] : [Street.Flop, Street.Turn];

    public override Spot Build(Rng rng, IReadOnlyList<Card> board) =>
        Build(new Params(rng.Pick(HeroSeats), SamplePlay(rng),
            DecisionStreet == Street.River ? SamplePlay(rng) : StreetPlay.CheckThrough), board);

    /// <summary>
    /// Picks the seat only; the earlier streets are checked through here and filled in by <see cref="FollowRules"/>,
    /// which the generator calls once it knows villain's HUD.
    /// </summary>
    public override Spot? TryBuild(Rng rng, IReadOnlyList<Card> hole, IReadOnlyList<Card> board, PreflopRanges ranges)
    {
        var seat = rng.Pick(HeroSeats);
        if (!ranges.CanOpen(seat, hole)) return null;
        return Build(new Params(seat, StreetPlay.CheckThrough), board);
    }

    /// <summary>
    /// Plays the earlier streets the way the established rules say: <paramref name="heroPlay"/> decides hero's play on
    /// each street from the cards seen so far, and when hero bets <paramref name="villainCalls"/> decides whether
    /// villain calls. Null when villain would have folded first (this spot isn't reached that way).
    /// </summary>
    public Spot? FollowRules(Position hero, IReadOnlyList<Card> board,
        Func<Street, IReadOnlyList<Card>, StreetPlay> heroPlay, Func<bool> villainCalls)
    {
        RequireBoard(board);
        var plays = new List<StreetPlay>();
        foreach (var street in EarlierStreets)
        {
            var play = heroPlay(street, board.Take(BoardCountAt(street)).ToList());
            if (play != StreetPlay.CheckThrough && !villainCalls()) return null;
            plays.Add(play);
        }
        return Build(new Params(hero, plays[0], plays.Count > 1 ? plays[1] : StreetPlay.CheckThrough), board);
    }

    public Spot Build(Params p, IReadOnlyList<Card> board)
    {
        RequireBoard(board);
        if (!HeroSeats.Contains(p.Hero)) throw new ArgumentException($"Unsupported hero seat {p.Hero}", nameof(p));
        if (DecisionStreet == Street.Turn && p.Turn != StreetPlay.CheckThrough)
        {
            throw new ArgumentException("A turn spot has no turn play before the decision", nameof(p));
        }

        var pot = Stakes.SrpPot;
        var stack = Stakes.StartingStack - Stakes.OpenSize; // both players have the same behind throughout
        var history = new List<string> { OpenCalledByBigBlind(p.Hero, "Hero", "Villain") };
        var steps = OpenCalledByBigBlindSteps(p.Hero);

        foreach (var street in EarlierStreets)
        {
            var header = StreetHeader(street.ToString(), DealtOn(street, board), pot);
            var play = street == Street.Flop ? p.Flop : p.Turn;
            history.Add($"{header} {Play(play, street, p.Hero, ref pot, ref stack, steps)}");
        }

        return BuildDecision(p, board, pot, stack, history, steps);
    }

    /// <summary>Adds the decision street (its header and villain's action) to a hand played up to it.</summary>
    protected abstract Spot BuildDecision(Params p, IReadOnlyList<Card> board, decimal pot, decimal stack,
        List<string> history, List<ActionStep> steps);

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

/// <summary>Hero opens, villain calls in the BB; the flop is played; villain checks the turn.</summary>
public sealed class TurnVillainChecksLine : SrpLaterStreetLine
{
    public override LineId Id => LineId.SRP_HeroIP_TurnVillainChecks;
    public override Street DecisionStreet => Street.Turn;
    public override IReadOnlyList<string> OptionIds { get; } = ["Check", "Bet33", "Bet75"];
    public override string Question => "Villain checks the turn. What do you do?";

    protected override Spot BuildDecision(Params p, IReadOnlyList<Card> board, decimal pot, decimal stack,
        List<string> history, List<ActionStep> steps)
    {
        history.Add($"{StreetHeader("Turn", [board[3]], pot)} Villain checks.");
        steps.Add(new ActionStep(Street.Turn, Position.BB, ActionKind.Check, 0m));
        IReadOnlyList<DrillOption> options =
        [
            new("Check", "Check"),
            BetOption("Bet33", 33, pot, stack),
            BetOption("Bet75", 75, pot, stack),
        ];
        return new Spot(Id, p.Hero, Position.BB, pot, 0m, stack, stack, history, steps, options);
    }
}

public sealed class RiverVillainChecksLine : SrpLaterStreetLine
{
    public override LineId Id => LineId.SRP_HeroIP_RiverVillainChecks;
    public override Street DecisionStreet => Street.River;
    public override IReadOnlyList<string> OptionIds { get; } = ["Check", "Bet33", "Bet75", "Bet150"];
    public override string Question => "Villain checks the river. What do you do?";

    protected override Spot BuildDecision(Params p, IReadOnlyList<Card> board, decimal pot, decimal stack,
        List<string> history, List<ActionStep> steps)
    {
        history.Add($"{StreetHeader("River", [board[4]], pot)} Villain checks.");
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

public sealed class FacingRiverBetLine : SrpLaterStreetLine
{
    public const int RiverBetPercent = 75;

    public override LineId Id => LineId.SRP_HeroIP_FacingRiverBet;
    public override Street DecisionStreet => Street.River;
    public override IReadOnlyList<string> OptionIds { get; } = ["Fold", "Call", "Raise"];
    public override string Question => "Villain bets the river. What do you do?";

    protected override Spot BuildDecision(Params p, IReadOnlyList<Card> board, decimal pot, decimal stack,
        List<string> history, List<ActionStep> steps)
    {
        var bet = Math.Min(Stakes.Bet(pot, RiverBetPercent), stack);
        history.Add($"{StreetHeader("River", [board[4]], pot)} Villain bets {Stakes.Bb(bet)} ({RiverBetPercent}%).");
        steps.Add(new ActionStep(Street.River, Position.BB, ActionKind.Bet, bet));
        return new Spot(Id, p.Hero, Position.BB, pot + bet, bet, stack, stack - bet, history, steps,
            FacingBetOptions(bet, stack));
    }
}

/// <summary>
/// Hero opens, a player behind 3-bets (3x the open in position, 4x from the blinds), everyone else folds,
/// hero decides: fold, call or 4-bet (to 2.5x the 3-bet).
/// </summary>
public sealed class FacingThreeBetLine : LineTemplate
{
    public sealed record Params(Position Hero, Position Villain);

    public static IReadOnlyList<Position> HeroSeats { get; } = [Position.UTG, Position.MP, Position.CO, Position.BTN];

    public override LineId Id => LineId.Pre_FacingThreeBet;
    public override Street DecisionStreet => Street.Preflop;
    public override IReadOnlyList<string> OptionIds { get; } = ["Fold", "Call", "FourBet"];
    public override string Question => "Villain 3-bets your open. What do you do?";

    public static decimal ThreeBetSize(Position villain) =>
        Stakes.OpenSize * (villain is Position.SB or Position.BB ? 4m : 3m);

    public static decimal FourBetSize(decimal threeBet) => Math.Round(threeBet * 2.5m, 1, MidpointRounding.AwayFromZero);

    public override Spot Build(Rng rng, IReadOnlyList<Card> board) => Build(Sample(rng), board);

    public override Spot? TryBuild(Rng rng, IReadOnlyList<Card> hole, IReadOnlyList<Card> board, PreflopRanges ranges)
    {
        var p = Sample(rng);
        return ranges.CanOpen(p.Hero, hole) ? Build(p, board) : null;
    }

    private static Params Sample(Rng rng)
    {
        var hero = rng.Pick(HeroSeats);
        return new Params(hero, rng.Pick(Enum.GetValues<Position>().Where(x => x > hero).ToList()));
    }

    public Spot Build(Params p, IReadOnlyList<Card>? board = null)
    {
        RequireBoard(board ?? []);
        if (!HeroSeats.Contains(p.Hero) || p.Villain <= p.Hero)
        {
            throw new ArgumentException($"Unsupported seating {p}: villain must act after hero", nameof(p));
        }

        var threeBet = ThreeBetSize(p.Villain);
        var after = Enum.GetValues<Position>().Where(x => x > p.Villain).ToList();
        var steps = PostBlinds();
        steps.AddRange(FoldSteps(Before(p.Hero)));
        steps.Add(new ActionStep(Street.Preflop, p.Hero, ActionKind.Raise, Stakes.OpenSize));
        steps.AddRange(FoldSteps(Between(p.Hero, p.Villain)));
        steps.Add(new ActionStep(Street.Preflop, p.Villain, ActionKind.Raise, threeBet));
        steps.AddRange(FoldSteps(after));

        // Open + 3-bet + whatever blinds the villain didn't post itself (they are dead money).
        var deadBlinds = (p.Villain == Position.SB ? 0m : Stakes.SmallBlind) + (p.Villain == Position.BB ? 0m : Stakes.BigBlind);
        var pot = Stakes.OpenSize + threeBet + deadBlinds;
        var history = new[]
        {
            "Preflop: " + Sentence(
                Folds(Before(p.Hero)),
                $"{Seat("Hero", p.Hero)} raises to {Stakes.Bb(Stakes.OpenSize)}.",
                Folds(Between(p.Hero, p.Villain)),
                $"{Seat("Villain", p.Villain)} 3-bets to {Stakes.Bb(threeBet)}.",
                Folds(after)),
        };

        var heroStack = Stakes.StartingStack - Stakes.OpenSize;
        var fourBet = FourBetSize(threeBet);
        IReadOnlyList<DrillOption> options =
        [
            new("Fold", "Fold"),
            new("Call", $"Call {Stakes.Bb(threeBet - Stakes.OpenSize)}"),
            fourBet - Stakes.OpenSize >= heroStack
                ? new DrillOption("FourBet", $"All-in ({Stakes.Bb(Stakes.StartingStack)})")
                : new DrillOption("FourBet", $"4-bet to {Stakes.Bb(fourBet)}"),
        ];
        return new Spot(Id, p.Hero, p.Villain, pot, threeBet - Stakes.OpenSize, heroStack,
            Stakes.StartingStack - threeBet, history, steps, options);
    }
}

/// <summary>
/// Hero opens on the button, the small blind calls, villain calls in the big blind; both check the flop to hero.
/// Whoever sits in the small blind must be a player who would cold-call there (see <see cref="ApplySeating"/>).
/// </summary>
public sealed class ThreeWayFlopLine : LineTemplate
{
    public override LineId Id => LineId.SRP_3Way_FlopCheckedToHero;
    public override Street DecisionStreet => Street.Flop;
    public override bool CanHaveOtherOpponents => true; // the small blind
    public override IReadOnlyList<string> OptionIds { get; } = ["Check", "Bet33", "Bet75"];
    public override string Question => "Both players check to you. What do you do?";

    public override Spot Build(Rng rng, IReadOnlyList<Card> board) => Build(board);

    public override Spot? TryBuild(Rng rng, IReadOnlyList<Card> hole, IReadOnlyList<Card> board, PreflopRanges ranges) =>
        ranges.CanOpen(Position.BTN, hole) ? Build(board) : null;

    public Spot Build(IReadOnlyList<Card> board)
    {
        RequireBoard(board);
        var pot = 3 * Stakes.OpenSize;
        var stack = Stakes.StartingStack - Stakes.OpenSize;

        var steps = PostBlinds();
        steps.AddRange(FoldSteps(Before(Position.BTN)));
        steps.Add(new ActionStep(Street.Preflop, Position.BTN, ActionKind.Raise, Stakes.OpenSize));
        steps.Add(new ActionStep(Street.Preflop, Position.SB, ActionKind.Call, Stakes.OpenSize));
        steps.Add(new ActionStep(Street.Preflop, Position.BB, ActionKind.Call, Stakes.OpenSize));
        steps.Add(new ActionStep(Street.Flop, Position.SB, ActionKind.Check, 0m));
        steps.Add(new ActionStep(Street.Flop, Position.BB, ActionKind.Check, 0m));

        var history = new[]
        {
            "Preflop: " + Sentence(
                Folds(Before(Position.BTN)),
                $"{Seat("Hero", Position.BTN)} raises to {Stakes.Bb(Stakes.OpenSize)}.",
                "SB calls.",
                $"{Seat("Villain", Position.BB)} calls."),
            $"{StreetHeader("Flop", board, pot)} SB checks. Villain checks.",
        };
        IReadOnlyList<DrillOption> options =
        [
            new("Check", "Check"),
            BetOption("Bet33", 33, pot, stack),
            BetOption("Bet75", 75, pot, stack),
        ];
        return new Spot(Id, Position.BTN, Position.BB, pot, 0m, stack, stack, history, steps, options);
    }

    /// <summary>The folders must fold by type, and the small blind must be a player who cold-calls the open.</summary>
    public override Spot? ApplySeating(Spot spot, IReadOnlyDictionary<Position, StatLine> others, Rng rng)
    {
        if (base.ApplySeating(spot, others, rng) is null) return null;
        return PreflopBehaviour.Draw(others[Position.SB], PreflopSituation.FacingRaise, rng) == PreflopAction.Call ? spot : null;
    }
}
