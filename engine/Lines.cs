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

/// <summary>A fully built decision point. Stacks are what each player has behind at the decision.</summary>
public sealed record Spot(
    LineId Line,
    Position HeroPosition,
    Position VillainPosition,
    decimal Pot,
    decimal ToCall,
    decimal HeroStack,
    decimal VillainStack,
    IReadOnlyList<string> ActionHistory,
    IReadOnlyList<DrillOption> Options);

public abstract class LineTemplate
{
    public abstract LineId Id { get; }
    public abstract Street DecisionStreet { get; }
    public abstract IReadOnlyList<string> OptionIds { get; }
    public abstract string Question { get; }

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

    public Spot Build(Params p, IReadOnlyList<Card>? board = null)
    {
        RequireBoard(board ?? []);
        if (!Seatings.Contains(p)) throw new ArgumentException($"Unsupported seating {p}", nameof(p));

        var pot = Stakes.SmallBlind + Stakes.BigBlind + LimpSize;
        var history = new[]
        {
            "Preflop: " + Sentence(
                Folds(Before(p.Villain)),
                $"{Seat("Villain", p.Villain)} limps {Stakes.Bb(LimpSize)}.",
                Folds(Between(p.Villain, p.Hero))),
        };
        IReadOnlyList<DrillOption> options =
        [
            new("Fold", "Fold"),
            new("Limp", $"Limp behind ({Stakes.Bb(LimpSize)})"),
            new("Iso3", $"Raise to {Stakes.Bb(SmallIso)}"),
            new("Iso5", $"Raise to {Stakes.Bb(BigIso)}"),
        ];
        return new Spot(Id, p.Hero, p.Villain, pot, LimpSize, Stakes.StartingStack, Stakes.StartingStack - LimpSize, history, options);
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
        IReadOnlyList<DrillOption> options =
        [
            new("Check", "Check"),
            BetOption("Bet33", 33, pot, stack),
            BetOption("Bet75", 75, pot, stack),
        ];
        return new Spot(Id, p.Hero, Position.BB, pot, 0m, stack, stack, history, options);
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
        return new Spot(Id, Position.BB, p.Villain, potBefore + bet, bet, stack, stack - bet, history,
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

        var flopHeader = StreetHeader("Flop", board.Take(3), pot);
        history.Add($"{flopHeader} {Play(p.Flop, ref pot, ref stack)}");
        var turnHeader = StreetHeader("Turn", [board[3]], pot);
        history.Add($"{turnHeader} {Play(p.Turn, ref pot, ref stack)}");

        return BuildRiver(p, board[4], pot, stack, history);
    }

    protected abstract Spot BuildRiver(Params p, Card river, decimal pot, decimal stack, List<string> history);

    private static StreetPlay SamplePlay(Rng rng) => rng.NextInt(4) switch
    {
        0 or 1 => StreetPlay.CheckThrough,
        2 => StreetPlay.Bet33Call,
        _ => StreetPlay.Bet75Call,
    };

    private static string Play(StreetPlay play, ref decimal pot, ref decimal stack)
    {
        if (play == StreetPlay.CheckThrough) return "Villain checks. Hero checks.";
        var percent = play == StreetPlay.Bet33Call ? 33 : 75;
        var bet = Math.Min(Stakes.Bet(pot, percent), stack);
        pot += 2 * bet;
        stack -= bet;
        return $"Villain checks. Hero bets {Stakes.Bb(bet)} ({percent}%). Villain calls.";
    }
}

public sealed class RiverVillainChecksLine : SrpToRiverLine
{
    public override LineId Id => LineId.SRP_HeroIP_RiverVillainChecks;
    public override IReadOnlyList<string> OptionIds { get; } = ["Check", "Bet33", "Bet75", "Bet150"];
    public override string Question => "Villain checks the river. What do you do?";

    protected override Spot BuildRiver(Params p, Card river, decimal pot, decimal stack, List<string> history)
    {
        history.Add($"{StreetHeader("River", [river], pot)} Villain checks.");
        IReadOnlyList<DrillOption> options =
        [
            new("Check", "Check"),
            BetOption("Bet33", 33, pot, stack),
            BetOption("Bet75", 75, pot, stack),
            BetOption("Bet150", 150, pot, stack),
        ];
        return new Spot(Id, p.Hero, Position.BB, pot, 0m, stack, stack, history, options);
    }
}

public sealed class FacingRiverBetLine : SrpToRiverLine
{
    public const int RiverBetPercent = 75;

    public override LineId Id => LineId.SRP_HeroIP_FacingRiverBet;
    public override IReadOnlyList<string> OptionIds { get; } = ["Fold", "Call", "Raise"];
    public override string Question => "Villain bets the river. What do you do?";

    protected override Spot BuildRiver(Params p, Card river, decimal pot, decimal stack, List<string> history)
    {
        var bet = Math.Min(Stakes.Bet(pot, RiverBetPercent), stack);
        history.Add($"{StreetHeader("River", [river], pot)} Villain bets {Stakes.Bb(bet)} ({RiverBetPercent}%).");
        return new Spot(Id, p.Hero, Position.BB, pot + bet, bet, stack, stack - bet, history,
            FacingBetOptions(bet, stack));
    }
}
