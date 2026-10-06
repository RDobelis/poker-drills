# PLAN — Poker exploit trainer prototype

Goal: mechanically correct drills (card evaluation, classification, pot math, generation,
scheduling). Strategy content is placeholder.

## Layout

```
/PokerDrills.slnx            solution (engine, engine.tests, generator) -> `dotnet test` at root
/engine                      C# class library  (namespace PokerDrills.Engine)
  Cards.cs                   Card, ranks, suits, parsing/formatting
  Rng.cs                     xoshiro256** PRNG + FNV-1a seed derivation (platform independent)
  Deck.cs                    52-card deck, seeded lazy Fisher-Yates dealing
  HandEvaluator.cs           5-card evaluator, best-of-N (5..7)
  HandClassifier.cs          HandClass + DrawFlags
  BoardAnalyzer.cs           BoardFlags + HighCard
  PreflopGroups.cs           HandGroup
  Ranges.cs                  HandRange (range notation parser), PreflopRanges
  Lines.cs                   Position, Stakes/Money, 5 line templates -> Spot
  PlayerTypes.cs             PlayerType, StatLine, sampling, range checks
  Rules.cs                   Rule model + matcher (SpotFacts)
  Content.cs                 JSON loading + validation of /content
  Drills.cs                  Drill output model
  Generation.cs              DrillGenerator + ConflictChecker
/engine.tests                xUnit
/generator                   console app: args -> DrillGenerator -> table + drills.json
/content/types.json          player types
/content/ranges.json         opening / BB calling ranges for the postflop lines
/content/rules/*.json        one rule per file
/web                         React + Vite + TS, reads /web/public/drills.json
README.md, IDEAS.md
```

## Enums

```
Suit        c d h s                      Rank 2..14 (T=10, J=11, Q=12, K=13, A=14)
HandCategory (evaluator)  HighCard Pair TwoPair ThreeOfAKind Straight Flush FullHouse FourOfAKind StraightFlush
HandClass   Air WeakPair SecondPair TopPairWeakKicker TopPairGoodKicker Overpair TwoPair Set Trips Straight Flush FullHousePlus
DrawFlags   None FlushDraw OpenEnded Gutshot                      (flop + turn only)
BoardFlags  Paired Monotone TwoTone Rainbow FourToFlush FourToStraight Dry  (+ HighCard value)
HandGroup   Trash Playable Strong Premium
Position    UTG MP CO BTN SB BB   (6-max)
LineId      Pre_IsoVsLimper SRP_HeroIP_FlopVillainChecks SRP_HeroOOP_FacingFlopCbet
            SRP_HeroIP_RiverVillainChecks SRP_HeroIP_FacingRiverBet
```

## Mechanics decisions

### Hand class (postflop)
Hero's hand only counts if a hole card is part of it; board-only pairs/trips are ignored.

1. **Five-card made hands** (Straight, Flush, FullHousePlus = full house / quads / straight flush):
   best 5-card combo containing >=1 hole card, where for quads the hole card must be one of the
   four. With a 5-card board it must also be strictly better than the board alone
   (otherwise hero "plays the board" and this step yields nothing).
2. Otherwise rank analysis. `D` = distinct board ranks, high to low; `top = D[0]`, `second = D[1]`.
   - pocket pair `p`: board has p -> **Set**; `p > top` -> **Overpair**; `second < p < top` -> **SecondPair**;
     else **WeakPair** (incl. boards with only one distinct rank).
   - unpaired hole cards: a hole rank appears twice on board -> **Trips**; both hole ranks on board -> **TwoPair**;
     one hole rank on board: `== top` -> **TopPair** (Good if other hole card >= J, configurable
     `ClassifierOptions.GoodKickerMinRank`), `== second` -> **SecondPair**, else **WeakPair**; none -> **Air**.

### Draw flags (flop/turn only; river = none)
- **FlushDraw**: some suit has exactly 4 cards among hole+board and >=1 of them is a hole card. Cleared if class >= Flush.
- Straight outs: ranks `r` not already held such that hole+board+r contains a 5-rank straight window
  that board+r alone does not (i.e. needs a hole card). A plays high and low.
  1 out-rank -> **Gutshot**; 2+ -> **OpenEnded** (double gutshots count as OpenEnded: 8 outs).
  Cleared if class >= Straight.

### Board flags
- Paired: any rank appears 2+ times.
- Suit texture over the whole visible board: max suit count >=3 **Monotone**, ==2 **TwoTone**, ==1 **Rainbow**
  (exactly one is set; on a flop this is the usual meaning; a river can never be Rainbow).
- FourToFlush: 4+ of one suit. FourToStraight: some 5-rank window (A low and high) holds 4+ distinct board ranks.
- Dry: Rainbow and not Paired and no window holds 3+ distinct board ranks. HighCard: top board rank.

### Preflop groups
Premium QQ+, AK. Strong TT-JJ, AQ, AJs+, KQs. Playable 22-99, suited broadways, suited connectors 54s+,
ATo-AJo, KQo. Trash: rest. Checked in that order.

### Money and lines
All amounts in bb as `decimal`. Blinds 0.5/1, 100bb starting stacks, opens to 2.5bb.
Bets = pot x pct, rounded to 0.1bb (midpoint away from zero). Bet-call adds 2 x bet to pot.
Raises facing a bet = raise to 3 x bet (capped at stack = all-in). Stacks reported = behind at decision.

| Line | Positions | Pre-decision action | Pot at decision | Options |
|---|---|---|---|---|
| Pre_IsoVsLimper | V in {MP, CO}, H after V in {CO, BTN} | folds, V limps 1, folds | 2.5 | Fold, Limp, Iso3 (to 3bb), Iso5 (to 5bb) |
| SRP_HeroIP_FlopVillainChecks | H in {UTG, MP, CO, BTN} opens, V=BB calls | V checks flop | 5.5 | Check, Bet33, Bet75 |
| SRP_HeroOOP_FacingFlopCbet | V in {UTG, MP, CO, BTN} opens, H=BB calls | H checks, V bets 33% or 75% | 5.5 + bet | Fold, Call, Raise |
| SRP_HeroIP_RiverVillainChecks | as line 2 | flop & turn each: V checks, then H checks back (50%) or bets 33%/75% (25% each) and V calls; V checks river | computed | Check, Bet33, Bet75, Bet150 |
| SRP_HeroIP_FacingRiverBet | as line 4 | ... V bets 75% river | computed + bet | Fold, Call, Raise |

### Preflop ranges (`content/ranges.json`)
In the postflop lines hero's hand must fit the preflop action. `open[seat]` = hands hero opens from
UTG/MP/CO/BTN; `bigBlindCall[opener]` = hands hero flat-calls in the BB vs an open from that seat.
`LineTemplate.TryBuild` draws the seat uniformly, then returns null (deal rejected) if the hand isn't in the
range. Pre_IsoVsLimper keeps any hand (the preflop decision is the drill). Notation: `22+`, `77-99`,
`A2s+`, `KTo+`, `K9s-K6s`, `AK`.

### Player types / stats
Stats: VPIP, PFR, 3Bet, WTSD, AF, FoldToCbet. Integers except AF (0.1 steps). Sampling is uniform inside the
type's ranges with sanity constraints PFR <= VPIP and 3Bet <= PFR. A stat line is accepted only if it lies
inside exactly one type's ranges (both for identify drills and for villain stats on action drills).

## Schemas

`content/types.json`
```json
{ "types": [ { "id": "Nit", "name": "Nit", "placeholder": true, "description": "...",
               "ranges": { "VPIP": [10,16], "PFR": [8,13], "3Bet": [2,4], "WTSD": [20,25], "AF": [2,4], "FoldToCbet": [50,65] } } ] }
```

`content/rules/<id>.json` (as in the brief). Postflop rules use `hero {minStrength,maxStrength}`;
preflop rules use `heroGroup: [..]`. `draws`: `null` (any) or `"none"`. `boardRequire`/`boardExclude`: BoardFlags names.
Loader validates: id == file name, known type/line/strengths/groups/flags, `correct` is an option of the line.

Rule matching = same line AND same villain type AND hand/board conditions.

`web/public/drills.json`
```jsonc
{
  "schemaVersion": 1, "seed": 42,
  "rules": [ { "id", "kind", "villainType", "line", "conditions", "correct", "reason", "placeholder" } ],
  "types": [ { "id", "name", "description", "ranges" } ],
  "drills": [ {
    "id": "station-river-value-1a2b3c4d",     // ruleId + FNV hash of the spot -> stable across regenerations
    "ruleId", "kind": "action" | "identify", "line": "<LineId>" | "Identify",
    "villainType", "villainStats": { "VPIP": 52, "PFR": 6, "3Bet": 2, "WTSD": 38, "AF": 1.1, "FoldToCbet": 28 },
    "heroPosition", "villainPosition", "stacks": { "hero": 97.5, "villain": 97.5 }, "pot": 5.5, "toCall": 0,
    "actionHistory": [ "Preflop: ...", "Flop [Ks 7d 2c] (5.5bb): Villain checks." ],
    "heroCards": ["As","Kd"], "board": ["Ks","7d","2c"],
    "question": "...", "options": [ { "id": "Bet33", "label": "Bet 33% (1.8bb)" } ],
    "correct": "Bet33", "reason": "...",
    "facts": { "handClass", "draws": [], "boardFlags": [], "highCard", "handGroup" }   // for coach review
  } ]
}
```
Identify drills: `ruleId = identify-<type>`, no cards/positions, options = the 5 types.

## Generator
- Per rule: RNG = xoshiro(seed derived from --seed + rule id). Rules processed sorted by id.
- Attempt: deal 2 + boardCount cards, analyse, check rule, dedupe on hand+board (preflop: hand+positions,
  since there is no board), build line, sample villain stats, emit. Stop at --per-rule or 200,000 attempts.
- Identify: 100 samples per type, keep lines inside exactly one type, dedupe.
- Conflict checker: re-analyse every action drill against every rule with the same line (and villain type);
  a match with a different `correct` is a conflict -> print rule pair + example, exit 1, no file written.
- Table: rule, drills, attempts, warnings (< 50 drills), conflicts.

## Web app
- Path routing: `/` home, session, summary (in-app state), `/review` coach page.
- localStorage key `pokerDrills.v1`: `{ progress: {drillId: {box, due, seen, correct}}, typeStats, streak, activeSession }`;
  flags in `pokerDrills.flags.v1`.
- Leitner: boxes 1-5, intervals 1/2/4/8/16 days. New drill counts as box 1: right -> box 2, wrong -> box 1.
  Due = today + interval(new box), local calendar dates.
- Session: due drills first (oldest due, lowest box; max 3 per rule), then new drills round-robin over rules
  (all identify drills share one rotation slot), then ordered so no two consecutive drills share a rule,
  preferring alternating villain types and lines. Active session persisted -> survives reload.
- Streak: +1 when a session completes on the day after the last completed session; same day no change; else 1.

## Tests
engine.tests: evaluator, classifier (40+ cases), draws, each board flag +/-, preflop groups, each line's pot math,
generator (every drill matches its rule, deterministic output), conflict checker (conflicting pair caught).
web: vitest for Leitner + session builder.
