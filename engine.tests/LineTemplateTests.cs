using PokerDrills.Engine;
using static System.FormattableString;
using static PokerDrills.Engine.Tests.TestHelpers;

namespace PokerDrills.Engine.Tests;

public class LineTemplateTests
{
    private static readonly Card[] Flop = Cards("Ks7d2c");
    private static readonly Card[] River = Cards("Ks7d2c9hTs");

    private static string Label(Spot spot, string id) => spot.Options.Single(o => o.Id == id).Label;

    [Theory]
    [InlineData(Position.MP, Position.CO, "Preflop: UTG folds. Villain (MP) limps 1bb.")]
    [InlineData(Position.MP, Position.BTN, "Preflop: UTG folds. Villain (MP) limps 1bb. CO folds.")]
    [InlineData(Position.CO, Position.BTN, "Preflop: UTG, MP fold. Villain (CO) limps 1bb.")]
    public void IsoVsLimper_pot_and_history(Position villain, Position hero, string history)
    {
        var spot = new IsoVsLimperLine().Build(new IsoVsLimperLine.Params(villain, hero));
        Assert.Equal(2.5m, spot.Pot); // SB 0.5 + BB 1 + limp 1
        Assert.Equal(1m, spot.ToCall);
        Assert.Equal(100m, spot.HeroStack);
        Assert.Equal(99m, spot.VillainStack);
        Assert.Equal([history], spot.ActionHistory);
        Assert.Equal(["Fold", "Limp", "Iso3", "Iso5"], spot.Options.Select(o => o.Id));
        Assert.Equal("Raise to 5bb", Label(spot, "Iso5"));
    }

    [Fact]
    public void IsoVsLimper_rejects_hero_in_front_of_limper() =>
        Assert.Throws<ArgumentException>(() => new IsoVsLimperLine().Build(new IsoVsLimperLine.Params(Position.CO, Position.CO)));

    [Fact]
    public void FlopVillainChecks_pot_and_bet_sizes()
    {
        var spot = new FlopVillainChecksLine().Build(new FlopVillainChecksLine.Params(Position.CO), Flop);
        Assert.Equal(5.5m, spot.Pot); // 2.5 + 2.5 + dead SB 0.5
        Assert.Equal(97.5m, spot.HeroStack);
        Assert.Equal(97.5m, spot.VillainStack);
        Assert.Equal(Position.BB, spot.VillainPosition);
        Assert.Equal("Bet 33% (1.8bb)", Label(spot, "Bet33")); // 1.815 -> 1.8
        Assert.Equal("Bet 75% (4.1bb)", Label(spot, "Bet75")); // 4.125 -> 4.1
        Assert.Equal("Preflop: UTG, MP fold. Hero (CO) raises to 2.5bb. BTN, SB fold. Villain (BB) calls.", spot.ActionHistory[0]);
        Assert.Equal("Flop [Ks 7d 2c] (5.5bb): Villain checks.", spot.ActionHistory[1]);
    }

    [Theory]
    [InlineData(33, 1.8, 7.3, 95.7, "Raise to 5.4bb")]
    [InlineData(75, 4.1, 9.6, 93.4, "Raise to 12.3bb")]
    public void FacingFlopCbet_pot_includes_the_bet(int percent, double bet, double pot, double villainStack, string raise)
    {
        var spot = new FacingFlopCbetLine().Build(new FacingFlopCbetLine.Params(Position.BTN, percent), Flop);
        Assert.Equal((decimal)pot, spot.Pot);
        Assert.Equal((decimal)bet, spot.ToCall);
        Assert.Equal(97.5m, spot.HeroStack);
        Assert.Equal((decimal)villainStack, spot.VillainStack);
        Assert.Equal(Position.BB, spot.HeroPosition);
        Assert.Equal(Invariant($"Call {bet}bb"), Label(spot, "Call"));
        Assert.Equal(raise, Label(spot, "Raise"));
        Assert.EndsWith(Invariant($"Hero checks. Villain bets {bet}bb ({percent}%)."), spot.ActionHistory[1]);
    }

    [Theory]
    // flop play, turn play, river pot, stacks behind, bet33 / bet75 / bet150 labels
    [InlineData(StreetPlay.CheckThrough, StreetPlay.CheckThrough, 5.5, 97.5, "1.8bb", "4.1bb", "8.3bb")]
    [InlineData(StreetPlay.Bet33Call, StreetPlay.CheckThrough, 9.1, 95.7, "3bb", "6.8bb", "13.7bb")] // 5.5 + 2*1.8
    [InlineData(StreetPlay.Bet75Call, StreetPlay.Bet33Call, 22.7, 88.9, "7.5bb", "17bb", "34.1bb")] // 13.7 + 2*4.5
    [InlineData(StreetPlay.Bet75Call, StreetPlay.Bet75Call, 34.3, 83.1, "11.3bb", "25.7bb", "51.5bb")] // 13.7 + 2*10.3
    public void RiverVillainChecks_pot_arithmetic(StreetPlay flop, StreetPlay turn, double pot, double stack,
        string bet33, string bet75, string bet150)
    {
        var spot = new RiverVillainChecksLine().Build(new SrpToRiverLine.Params(Position.BTN, flop, turn), River);
        Assert.Equal((decimal)pot, spot.Pot);
        Assert.Equal((decimal)stack, spot.HeroStack);
        Assert.Equal((decimal)stack, spot.VillainStack);
        Assert.Equal(0m, spot.ToCall);
        Assert.Equal(["Check", "Bet33", "Bet75", "Bet150"], spot.Options.Select(o => o.Id));
        Assert.Equal($"Bet 33% ({bet33})", Label(spot, "Bet33"));
        Assert.Equal($"Bet 75% ({bet75})", Label(spot, "Bet75"));
        Assert.Equal($"Bet 150% ({bet150})", Label(spot, "Bet150"));
        Assert.Equal(4, spot.ActionHistory.Count);
        Assert.Equal(Invariant($"River [Ts] ({pot}bb): Villain checks."), spot.ActionHistory[3]);
    }

    [Fact]
    public void RiverVillainChecks_history_shows_pot_before_each_street()
    {
        var spot = new RiverVillainChecksLine().Build(
            new SrpToRiverLine.Params(Position.MP, StreetPlay.Bet75Call, StreetPlay.Bet33Call), River);
        Assert.Equal("Flop [Ks 7d 2c] (5.5bb): Villain checks. Hero bets 4.1bb (75%). Villain calls.", spot.ActionHistory[1]);
        Assert.Equal("Turn [9h] (13.7bb): Villain checks. Hero bets 4.5bb (33%). Villain calls.", spot.ActionHistory[2]);
    }

    [Theory]
    [InlineData(StreetPlay.CheckThrough, StreetPlay.CheckThrough, 5.5, 4.1, 9.6, "Raise to 12.3bb")]
    [InlineData(StreetPlay.Bet75Call, StreetPlay.Bet75Call, 34.3, 25.7, 60.0, "Raise to 77.1bb")]
    public void FacingRiverBet_pot_arithmetic(StreetPlay flop, StreetPlay turn, double potBefore, double bet, double pot, string raise)
    {
        var spot = new FacingRiverBetLine().Build(new SrpToRiverLine.Params(Position.UTG, flop, turn), River);
        Assert.Equal((decimal)pot, spot.Pot);
        Assert.Equal((decimal)bet, spot.ToCall);
        Assert.Equal(spot.HeroStack - (decimal)bet, spot.VillainStack);
        Assert.Equal(raise, Label(spot, "Raise"));
        Assert.Equal(Invariant($"River [Ts] ({potBefore}bb): Villain bets {bet}bb (75%)."), spot.ActionHistory[3]);
    }

    [Fact]
    public void Bet_rounding_is_to_tenths_away_from_zero()
    {
        Assert.Equal(1.8m, Stakes.Bet(5.5m, 33));
        Assert.Equal(4.1m, Stakes.Bet(5.5m, 75));
        Assert.Equal(34.1m, Stakes.Bet(22.7m, 150)); // 34.05 rounds up
        Assert.Equal(0.1m, Stakes.Bet(0.1m, 50)); // 0.05 rounds up
    }

    [Fact]
    public void Chips_are_conserved_on_every_random_postflop_spot()
    {
        // Hero + villain start with 100 each; the folded SB adds 0.5 dead money.
        var rng = new Rng(3);
        var deck = new Deck(rng);
        foreach (var template in LineTemplate.All.Values.Where(t => t.DecisionStreet != Street.Preflop && !t.CanHaveOtherOpponents))
        {
            for (var i = 0; i < 500; i++)
            {
                deck.Reset();
                var spot = template.Build(rng, deck.Deal(template.BoardCardCount));
                Assert.Equal(200.5m, spot.HeroStack + spot.VillainStack + spot.Pot);
                Assert.Equal(template.OptionIds, spot.Options.Select(o => o.Id));
                Assert.True(spot.HeroStack > 0 && spot.VillainStack > 0);
            }
        }
    }

    [Fact]
    public void TryBuild_only_seats_hands_that_fit_the_preflop_ranges()
    {
        var ranges = TestHelpers.Content.Ranges;
        var rng = new Rng(9);
        var junk = Cards("9s3d");
        var aces = Cards("AsAd");
        foreach (var line in new[] { LineId.SRP_HeroIP_FlopVillainChecks, LineId.SRP_HeroIP_RiverVillainChecks, LineId.SRP_HeroIP_FacingRiverBet })
        {
            var template = LineTemplate.For(line);
            var board = template.BoardCardCount == 3 ? Flop : River;
            for (var i = 0; i < 100; i++)
            {
                Assert.Null(template.TryBuild(rng, junk, board, ranges));
                Assert.NotNull(template.TryBuild(rng, aces, board, ranges)); // AA opens from every seat
            }
        }

        var cbet = LineTemplate.For(LineId.SRP_HeroOOP_FacingFlopCbet);
        var suitedConnector = Cards("8h7h");
        for (var i = 0; i < 100; i++)
        {
            Assert.Null(cbet.TryBuild(rng, aces, Flop, ranges)); // AA 3-bets preflop, never just calls
            var spot = cbet.TryBuild(rng, suitedConnector, Flop, ranges);
            if (spot is not null) Assert.True(ranges.CanCallInBigBlind(spot.VillainPosition, suitedConnector));
        }

        var iso = LineTemplate.For(LineId.Pre_IsoVsLimper);
        Assert.NotNull(iso.TryBuild(rng, junk, [], ranges)); // the preflop decision is the drill
    }

    [Fact]
    public void Action_steps_for_a_flop_spot()
    {
        var spot = new FlopVillainChecksLine().Build(new FlopVillainChecksLine.Params(Position.CO), Flop);
        string[] expected =
        [
            "Preflop SB Post 0.5", "Preflop BB Post 1", "Preflop UTG Fold 0", "Preflop MP Fold 0", "Preflop CO Raise 2.5",
            "Preflop BTN Fold 0", "Preflop SB Fold 0", "Preflop BB Call 2.5", "Flop BB Check 0",
        ];
        Assert.Equal(expected, spot.Actions.Select(a => Invariant($"{a.Street} {a.Seat} {a.Kind} {a.To}")));
    }

    [Fact]
    public void Action_steps_replay_to_the_spot_pot_stacks_and_amount_to_call()
    {
        var rng = new Rng(21);
        var deck = new Deck(rng);
        foreach (var template in LineTemplate.All.Values)
        {
            for (var i = 0; i < 300; i++)
            {
                deck.Reset();
                var spot = template.Build(rng, deck.Deal(template.BoardCardCount));
                var r = Replay(spot);

                Assert.Equal(spot.Pot, r.Pot);
                Assert.Equal(spot.HeroStack, r.Stacks[spot.HeroPosition]);
                Assert.Equal(spot.VillainStack, r.Stacks[spot.VillainPosition]);
                Assert.Equal(spot.ToCall, r.ToCall);
                Assert.DoesNotContain(spot.HeroPosition, r.Folded);
                Assert.DoesNotContain(spot.VillainPosition, r.Folded);
                Assert.Equal(template.DecisionStreet, spot.Actions[^1].Street);
                if (!template.CanHavePlayersBehind)
                {
                    // Everyone but hero, villain and the other opponents in the hand has folded.
                    var others = TableSeating.OtherOpponents(spot.HeroPosition, spot.VillainPosition, spot.Actions);
                    Assert.Equal(4 - others.Count, r.Folded.Count);
                }
            }
        }
    }

    /// <summary>Plays the steps like a table: chips in front move to the pot when the street changes.</summary>
    private static (decimal Pot, Dictionary<Position, decimal> Stacks, decimal ToCall, HashSet<Position> Folded) Replay(Spot spot)
    {
        var seats = Enum.GetValues<Position>();
        var stacks = seats.ToDictionary(p => p, _ => Stakes.StartingStack);
        var inFront = seats.ToDictionary(p => p, _ => 0m);
        var folded = new HashSet<Position>();
        var pot = 0m;
        var street = Street.Preflop;

        foreach (var a in spot.Actions)
        {
            Assert.True(a.Street >= street, "streets never go backwards");
            Assert.DoesNotContain(a.Seat, folded);
            if (a.Street != street)
            {
                pot += inFront.Values.Sum();
                foreach (var p in seats) inFront[p] = 0m;
                street = a.Street;
            }

            switch (a.Kind)
            {
                case ActionKind.Fold:
                    folded.Add(a.Seat);
                    break;
                case ActionKind.Check:
                    Assert.Equal(inFront.Values.Max(), inFront[a.Seat]); // can't check facing a bet
                    break;
                default:
                    Assert.True(a.To > inFront[a.Seat], $"{a} must add chips");
                    stacks[a.Seat] -= a.To - inFront[a.Seat];
                    inFront[a.Seat] = a.To;
                    break;
            }
        }

        return (pot + inFront.Values.Sum(), stacks, inFront.Values.Max() - inFront[spot.HeroPosition], folded);
    }

    [Theory]
    [InlineData(Position.MP, Position.CO, "BTN,SB,BB")]
    [InlineData(Position.MP, Position.BTN, "SB,BB")] // CO folded in between
    [InlineData(Position.CO, Position.BTN, "SB,BB")]
    public void Seats_behind_hero_preflop_are_those_still_to_act(Position villain, Position hero, string expected)
    {
        var spot = new IsoVsLimperLine().Build(new IsoVsLimperLine.Params(villain, hero));
        Assert.Equal(expected, string.Join(",", TableSeating.SeatsBehind(spot.HeroPosition, spot.Actions)));
    }

    [Fact]
    public void Nobody_is_behind_hero_postflop()
    {
        var rng = new Rng(4);
        var deck = new Deck(rng);
        foreach (var template in LineTemplate.All.Values.Where(t => !t.CanHavePlayersBehind))
        {
            for (var i = 0; i < 50; i++)
            {
                deck.Reset();
                var spot = template.Build(rng, deck.Deal(template.BoardCardCount));
                Assert.Empty(TableSeating.SeatsBehind(spot.HeroPosition, spot.Actions));
            }
        }
    }

    [Fact]
    public void Iso_spot_with_extra_limpers_grows_the_pot_and_the_iso_sizes()
    {
        var iso = new IsoVsLimperLine();
        var spot = iso.Build(new IsoVsLimperLine.Params(Position.MP, Position.BTN), [], [Position.UTG, Position.CO]);
        Assert.Equal(4.5m, spot.Pot); // blinds 1.5 + three limps
        Assert.Equal(1m, spot.ToCall);
        Assert.Equal(["Preflop: UTG limps 1bb. Villain (MP) limps 1bb. CO limps 1bb."], spot.ActionHistory);
        Assert.Equal("Raise to 5bb", Label(spot, "Iso3")); // 3bb + 1bb per extra limper
        Assert.Equal("Raise to 7bb", Label(spot, "Iso5"));
        var r = Replay(spot);
        Assert.Equal(spot.Pot, r.Pot);
        Assert.Equal(spot.ToCall, r.ToCall);

        Assert.Throws<ArgumentException>(() => iso.Build(new IsoVsLimperLine.Params(Position.MP, Position.CO), [], [Position.BTN]));
    }

    [Fact]
    public void Behaviour_odds_come_from_the_hud()
    {
        var station = new StatLine(52, 6, 2, 40, 1.0, 28);
        var nit = new StatLine(13, 11, 3, 22, 3.0, 58);

        var (fold, limp, raise) = PreflopBehaviour.Odds(station, PreflopSituation.Unopened);
        Assert.Equal(0.06, raise, 6); // PFR
        Assert.Equal(0.6 * 0.46, limp, 6); // limp share of VPIP - PFR
        Assert.Equal(1 - 0.06 - 0.276, fold, 6);
        Assert.True(PreflopBehaviour.Odds(nit, PreflopSituation.Unopened).Fold > fold);

        var (foldVsRaise, call, threeBet) = PreflopBehaviour.Odds(station, PreflopSituation.FacingRaise);
        Assert.Equal(0.02, threeBet, 6); // 3Bet
        Assert.Equal(0.5 * 0.46, call, 6); // cold-call share of VPIP - PFR
        Assert.Equal(1 - 0.02 - 0.23, foldVsRaise, 6);
    }

    private static Card[] BoardFor(LineTemplate t) => t.BoardCardCount switch { 0 => [], 3 => Flop, _ => River };

    private static Dictionary<Position, StatLine> Everyone(Spot spot, StatLine stats) =>
        Enum.GetValues<Position>().Where(p => p != spot.HeroPosition && p != spot.VillainPosition).ToDictionary(p => p, _ => stats);

    [Fact]
    public void Players_who_never_play_a_hand_always_fold_through()
    {
        var never = new StatLine(0, 0, 0, 20, 1.0, 50);
        var rng = new Rng(2);
        foreach (var template in LineTemplate.All.Values.Where(t => t is not ThreeWayFlopLine)) // that one needs a caller
        {
            for (var i = 0; i < 30; i++)
            {
                var spot = template.Build(rng, BoardFor(template));
                Assert.Same(spot, template.ApplySeating(spot, Everyone(spot, never), rng));
            }
        }
    }

    [Fact]
    public void Players_who_always_raise_cannot_sit_where_the_line_has_them_fold()
    {
        var raiser = new StatLine(100, 100, 100, 30, 8.0, 20);
        var rng = new Rng(3);
        var flop = new FlopVillainChecksLine().Build(new FlopVillainChecksLine.Params(Position.CO), Flop); // UTG, MP, BTN, SB fold
        Assert.Null(new FlopVillainChecksLine().ApplySeating(flop, Everyone(flop, raiser), rng));
        var iso = new IsoVsLimperLine().Build(new IsoVsLimperLine.Params(Position.MP, Position.BTN)); // UTG, CO fold
        Assert.Null(new IsoVsLimperLine().ApplySeating(iso, Everyone(iso, raiser), rng));
    }

    [Fact]
    public void Limp_happy_players_before_hero_become_extra_limpers()
    {
        var limper = new StatLine(100, 0, 0, 40, 0.5, 20); // limps 60%, folds 40%, never raises
        var iso = new IsoVsLimperLine();
        var spot = iso.Build(new IsoVsLimperLine.Params(Position.MP, Position.BTN)); // UTG and CO act before hero
        var rng = new Rng(4);
        var counts = new int[3];
        for (var i = 0; i < 4000; i++)
        {
            var played = iso.ApplySeating(spot, Everyone(spot, limper), rng)!;
            var extra = played.Actions.Count(a => a.Kind == ActionKind.Limp && a.Seat != Position.MP);
            counts[extra]++;
            Assert.Equal(2.5m + extra, played.Pot);
            Assert.DoesNotContain(played.Actions, a => a.Seat is Position.SB or Position.BB && a.Kind != ActionKind.Post); // behind hero: not acted
        }
        // Two players limping 60% each: 0 / 1 / 2 extra limpers ~ 16% / 48% / 36%.
        Assert.InRange(counts[0] / 4000.0, 0.13, 0.19);
        Assert.InRange(counts[1] / 4000.0, 0.44, 0.52);
        Assert.InRange(counts[2] / 4000.0, 0.32, 0.40);
    }

    [Theory]
    [InlineData(Position.MP, Position.BTN, 7.5, 11.5, 5, "4-bet to 18.8bb",
        "Preflop: UTG folds. Hero (MP) raises to 2.5bb. CO folds. Villain (BTN) 3-bets to 7.5bb. SB, BB fold.")]
    [InlineData(Position.CO, Position.SB, 10, 13.5, 7.5, "4-bet to 25bb",
        "Preflop: UTG, MP fold. Hero (CO) raises to 2.5bb. BTN folds. Villain (SB) 3-bets to 10bb. BB folds.")]
    [InlineData(Position.BTN, Position.BB, 10, 13, 7.5, "4-bet to 25bb",
        "Preflop: UTG, MP, CO fold. Hero (BTN) raises to 2.5bb. SB folds. Villain (BB) 3-bets to 10bb.")]
    public void FacingThreeBet_pot_arithmetic(Position hero, Position villain, double threeBet, double pot, double toCall,
        string fourBet, string history)
    {
        // 3-bet: 3x the open in position, 4x from the blinds; pot = open + 3-bet + dead blinds.
        var spot = new FacingThreeBetLine().Build(new FacingThreeBetLine.Params(hero, villain));
        Assert.Equal((decimal)pot, spot.Pot);
        Assert.Equal((decimal)toCall, spot.ToCall);
        Assert.Equal(97.5m, spot.HeroStack);
        Assert.Equal(100m - (decimal)threeBet, spot.VillainStack);
        Assert.Equal(Invariant($"Call {toCall}bb"), Label(spot, "Call"));
        Assert.Equal(fourBet, Label(spot, "FourBet"));
        Assert.Equal([history], spot.ActionHistory);
        var r = Replay(spot);
        Assert.Equal(spot.Pot, r.Pot);
        Assert.Equal(spot.ToCall, r.ToCall);
        Assert.Throws<ArgumentException>(() => new FacingThreeBetLine().Build(new FacingThreeBetLine.Params(Position.CO, Position.MP)));
    }

    [Fact]
    public void ThreeWay_pot_arithmetic_and_history()
    {
        var spot = new ThreeWayFlopLine().Build(Flop);
        Assert.Equal(7.5m, spot.Pot); // three 2.5bb opens/calls
        Assert.Equal(97.5m, spot.HeroStack);
        Assert.Equal("Bet 33% (2.5bb)", Label(spot, "Bet33")); // 2.475 -> 2.5
        Assert.Equal("Bet 75% (5.6bb)", Label(spot, "Bet75")); // 5.625 -> 5.6
        Assert.Equal("Preflop: UTG, MP, CO fold. Hero (BTN) raises to 2.5bb. SB calls. Villain (BB) calls.", spot.ActionHistory[0]);
        Assert.Equal("Flop [Ks 7d 2c] (7.5bb): SB checks. Villain checks.", spot.ActionHistory[1]);
        Assert.Equal([Position.SB], TableSeating.OtherOpponents(spot.HeroPosition, spot.VillainPosition, spot.Actions));
        Assert.Empty(TableSeating.SeatsBehind(spot.HeroPosition, spot.Actions));
        var r = Replay(spot);
        Assert.Equal(spot.Pot, r.Pot);
    }

    [Fact]
    public void ThreeWay_small_blind_must_be_a_cold_caller()
    {
        var line = new ThreeWayFlopLine();
        var spot = line.Build(Flop);
        var rng = new Rng(6);
        var others = Everyone(spot, new StatLine(0, 0, 0, 20, 1.0, 50)); // never plays a hand
        Assert.Null(line.ApplySeating(spot, others, rng));

        others[Position.SB] = new StatLine(100, 0, 0, 40, 0.5, 20); // cold-calls 50% of opens
        var fits = Enumerable.Range(0, 2000).Count(_ => line.ApplySeating(spot, others, rng) is not null);
        Assert.InRange(fits / 2000.0, 0.45, 0.55);
    }

    [Fact]
    public void FollowRules_plays_streets_by_policy_and_stops_when_villain_folds()
    {
        var line = new RiverVillainChecksLine();
        var barrels = line.FollowRules(Position.CO, River, (_, _) => StreetPlay.Bet75Call, () => true)!;
        Assert.Equal(34.3m, barrels.Pot); // 5.5 -> 13.7 -> 34.3
        Assert.Equal(83.1m, barrels.HeroStack);

        Assert.Null(line.FollowRules(Position.CO, River, (_, _) => StreetPlay.Bet33Call, () => false)); // villain folds the flop

        var cardsSeen = new List<int>();
        var checks = line.FollowRules(Position.CO, River,
            (_, seen) =>
            {
                cardsSeen.Add(seen.Count);
                return StreetPlay.CheckThrough;
            },
            () => throw new InvalidOperationException("villain is never asked to call when hero checks"))!;
        Assert.Equal(5.5m, checks.Pot);
        Assert.Equal([3, 4], cardsSeen); // the policy only sees the cards dealt so far
    }

    [Fact]
    public void Templates_reject_wrong_board_size()
    {
        Assert.Throws<ArgumentException>(() => LineTemplate.For(LineId.SRP_HeroIP_FlopVillainChecks).Build(new Rng(1), River));
        Assert.Throws<ArgumentException>(() => LineTemplate.For(LineId.SRP_HeroIP_FacingRiverBet).Build(new Rng(1), Flop));
    }
}
