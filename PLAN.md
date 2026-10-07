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
            SRP_HeroIP_RiverVillainChecks SRP_HeroIP_FacingRiverBet Pre_FacingThreeBet
            SRP_3Way_FlopCheckedToHero SRP_HeroIP_TurnVillainChecks
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
| SRP_HeroIP_TurnVillainChecks | as line 2 | flop: V checks, H checks back or bets 33%/75% by rule/default and V calls (see below); V checks turn | computed | Check, Bet33, Bet75 |
| SRP_HeroIP_RiverVillainChecks | as line 2 | flop & turn each: V checks, then H checks back or bets 33%/75% by rule/default and V calls (see below); V checks river | computed | Check, Bet33, Bet75, Bet150 |
| SRP_HeroIP_FacingRiverBet | as line 4 | ... V bets 75% river | computed + bet | Fold, Call, Raise |
| Pre_FacingThreeBet | H in {UTG, MP, CO, BTN} opens, V any seat after H | V 3-bets 3x IP / 4x from blinds, rest fold | open + 3-bet + dead blinds | Fold, Call, FourBet (2.5x) |
| SRP_3Way_FlopCheckedToHero | H = BTN opens, SB cold-calls (by type), V = BB calls | SB and V check flop | 7.5 | Check, Bet33, Bet75 |

Rule seating conditions: `behind` (players still to act after hero, iso only) and `others` (opponents in the hand
besides villain: extra limpers, the 3-way SB). Drills carry `behind` and `others` seats (schemaVersion 5).

### Turn and river spots follow the rules (`SrpLaterStreetLine.FollowRules`, `DrillGenerator.HeroPlay`)
`SrpLaterStreetLine` is the base of the turn and river lines; `EarlierStreets` = flop (turn line) or flop + turn.
`TryBuild` only picks hero's seat. After villain's HUD is drawn, the generator plays the earlier streets: hero's play
is the first rule (by id) on that street's checked-to-hero line (`CheckedToHeroLine`: flop →
SRP_HeroIP_FlopVillainChecks, turn → SRP_HeroIP_TurnVillainChecks) matching villain type + cards seen so far with no
seating (Check → check back, Bet33/Bet75 → bet and called), else `DefaultPlay` (TPGK+ → 75%, SecondPair+ or FD/OE →
33%, else check). When hero bets, villain calls with probability 1 − FoldToCbet/100 from the `path:{rule}:{attempt}`
stream; a fold drops the deal. `facts.path` records each street ("Flop: check (rule station-flop-no-stab)").
The plays are part of the spot, so river drill ids changed when this came in (saved progress on the old river drills
is simply no longer used, as for any removed drill).

### Hand drills (`DrillGenerator.GenerateHands`, `HandAnchors`)
Several decisions in one hand. Anchors = rules on a `SrpLaterStreetLine` whose villain type also has a rule on the
checked-to-hero line of one of its earlier streets. Per anchor, `DealSpots` deals like the rule's own drills but on
streams `hand:{rule}`, `hand-seats:…`, `hand-path:…` (single drills keep theirs), keeping deals where a rule decided at
least one earlier street, up to `HandsPerRule` (50). Steps = one per rule-decided earlier street (the spot built by
FlopVillainChecksLine / TurnVillainChecksLine with the plays so far) + the anchor's spot. Between steps the hand goes
on along the rule's answer (bet → villain calls), so each step's `actions` extend the previous step's. Hands are
grouped per villain type: `ruleId = hands-<type>`, one rules[] summary each. The conflict checker checks every step.
In the app a wrong answer is corrected and the hand continues along the correct line (no branching on the student's
choice).

### Preflop ranges (`content/ranges.json`)
In the postflop lines hero's hand must fit the preflop action. `open[seat]` = hands hero opens from
UTG/MP/CO/BTN; `bigBlindCall[opener]` = hands hero flat-calls in the BB vs an open from that seat.
`LineTemplate.TryBuild` draws the seat uniformly, then returns null (deal rejected) if the hand isn't in the
range. Pre_IsoVsLimper keeps any hand (the preflop decision is the drill). Notation: `22+`, `77-99`,
`A2s+`, `KTo+`, `K9s-K6s`, `AK`.

### Other players act by type (`engine/Behaviour.cs`, `LineTemplate.ApplySeating`)
Each seated player's preflop action comes from their own HUD line: unopened → raise PFR%, limp 0.6 × (VPIP − PFR),
else fold; facing a raise → 3-bet 3Bet%, cold-call 0.5 × (VPIP − PFR), else fold. Per attempt the seating (types +
HUD lines) is drawn from its own stream and redrawn until every player the line shows folding would fold, or —
in Pre_IsoVsLimper only — limps (extra limper: pot +1bb, iso sizes +1bb each, extra limpers join the duplicate
key). Postflop drills therefore keep their cards; only the seating changes.

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
  "schemaVersion": 6, "seed": 42,
  "rules": [ { "id", "kind", "villainType", "line", "conditions", "correct", "reason", "placeholder" } ],
  "types": [ { "id", "name", "description", "ranges" } ],
  "drills": [ {
    "id": "station-river-value-1a2b3c4d",     // ruleId + FNV hash of the spot -> stable across regenerations
    "ruleId", "kind": "action" | "identify", "line": "<LineId>" | "Identify",
    "villainType", "villainStats": { "VPIP": 52, "PFR": 6, "3Bet": 2, "WTSD": 38, "AF": 1.1, "FoldToCbet": 28 },
    "heroPosition", "villainPosition", "stacks": { "hero": 97.5, "villain": 97.5 }, "pot": 5.5, "toCall": 0,
    "actionHistory": [ "Preflop: ...", "Flop [Ks 7d 2c] (5.5bb): Villain checks." ],
    // schemaVersion 2: the same story step by step for the table view. `to` = the seat's total in front
    // of it on this street after the action; replaying the steps must give exactly pot/stacks/toCall.
    "actions": [ { "street": "Preflop", "seat": "SB", "kind": "Post", "to": 0.5 }, { "street": "Flop", "seat": "BB", "kind": "Check", "to": 0 } ],
    // schemaVersion 3: who sits where (every seat but hero's). Villain = rule type + villainStats; other seats
    // drawn by types[].tableShare from a separate RNG stream keyed by the drill id, so cards/action/ids never change.
    "players": [ { "seat": "UTG", "type": "Nit", "stats": { "VPIP": 13, "PFR": 11, "3Bet": 3, "WTSD": 22, "AF": 2.4, "FoldToCbet": 58 } } ],
    // schemaVersion 4: seats still to act after hero (no voluntary action yet; empty postflop). Rules can
    // require/exclude player types among them ("behind"); the seating is drawn per attempt, before the rule check.
    "behind": [ "SB", "BB" ],
    // schemaVersion 5: other opponents still in the hand besides villain (extra limpers, the 3-way SB).
    "others": [],
    "heroCards": ["As","Kd"], "board": ["Ks","7d","2c"],
    "question": "...", "options": [ { "id": "Bet33", "label": "Bet 33% (1.8bb)" } ],
    "correct": "Bet33", "reason": "...",
    "facts": { "handClass", "draws": [], "boardFlags": [], "highCard", "handGroup", "hand", "path": [] },  // for coach review
    // schemaVersion 6, kind "hand" only: the decisions in order. The drill keeps the shared fields (players,
    // villain, positions, heroCards, full board); its question/options/correct/reason are empty, pot/stacks null.
    "steps": [ { "ruleId", "line", "pot", "toCall", "stacks", "actionHistory", "actions", "board",
                 "question", "options", "correct", "reason", "facts" } ]
  } ]
}
```
Identify drills: `ruleId = identify-<type>`, no cards/positions, options = the 5 types.
Hand drills: `ruleId = hands-<type>`, `line = "Hand"`, `steps` as above (left out of other drills).

## Generator
- Per rule: RNG = xoshiro(seed derived from --seed + rule id). Rules processed sorted by id.
- Attempt: deal 2 + boardCount cards, analyse, check rule, dedupe on hand+board (preflop: hand+positions,
  since there is no board), build line, sample villain stats, emit. Stop at --per-rule or 200,000 attempts.
- Identify: 100 samples per type, keep lines inside exactly one type, dedupe.
- Hands: per villain type with anchors, 50 per anchor (see "Hand drills").
- Conflict checker: re-analyse every action drill and every hand step against every rule with the same line (and
  villain type); a match with a different `correct` is a conflict -> print rule pair + example, exit 1, no file written.
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
- Hands (`logic/hand.ts`): `decisionsOf` turns a hand into one action drill per step (id `<hand>#n`), so DrillView,
  the table and the summary work unchanged. Answers carry `step`; a hand is done when every step is answered, its
  Leitner box moves once (up only if all steps were right), typeStats count each decision. The table replay of
  step n starts at the action count of step n-1.

## Tests
engine.tests: evaluator, classifier (40+ cases), draws, each board flag +/-, preflop groups, each line's pot math,
generator (every drill matches its rule, deterministic output), conflict checker (conflicting pair caught).
web: vitest for Leitner + session builder.
