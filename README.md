# Poker exploit trainer (prototype)

A coach writes short exploit **rules** (`content/rules/*.json`). A C# **generator** turns each rule into
hundreds of concrete drills (`web/public/drills.json`). A mobile-first **web app** serves sessions of 10
drills with Leitner spaced repetition; progress lives in the browser (localStorage). No backend, no accounts.

> The poker strategy in `content/` is **placeholder** content. The prototype is about correct mechanics:
> card evaluation, hand/board classification, pot math, generation and scheduling.

## Requirements

- .NET 10 SDK
- Node.js 20+ (tested with 22) and npm

## Exact commands

Run from the repository root unless stated otherwise.

1. Run the engine tests:

   ```bash
   dotnet test
   ```

2. Generate the drills:

   ```bash
   dotnet run --project generator -- --content ./content --out ./web/public/drills.json --per-rule 200 --seed 42
   ```

   Optional: `--max-attempts 200000` (the default). Exit codes: `0` ok, `1` rule conflicts (nothing is
   written), `2` bad arguments or invalid content.

3. Install and start the web app:

   ```bash
   cd web
   npm install
   npm run dev
   ```

   Open http://localhost:5173. The coach page is http://localhost:5173/#review (`/review` also works locally).

   To try it on a phone on the same Wi-Fi, run `npm run dev -- --host` and open the "Network" URL it prints.

4. Web app tests, type check and production build (inside `web/`):

   ```bash
   npm test
   ```

   ```bash
   npm run build
   ```

   `npm run build` writes a static site to `web/dist` (relative paths, coach page at `#review`), so any
   static host or sub-path works without server config. `npm run preview` serves the build locally.

## Generator output

With the placeholder content and seed 42:

```
Rule                      Kind      Drills  Attempts  Conflicts  Warnings
maniac-flop-call-down     action       200      2415          0  -
maniac-river-bluffcatch   action       200      2847          0  -
nit-river-fold-one-pair   action       200      3681          0  -
overfolder-flop-stab      action       200      5710          0  -
station-flop-no-stab      action       200      1657          0  -
station-iso-big           action       200     11811          0  -
station-iso-trash         action       200       240          0  -
station-river-no-bluff    action       200      1792          0  -
station-river-value       action       200      2354          0  -
identify-nit              identify     100       100          -  -
identify-calling-station  identify     100       100          -  -
identify-maniac           identify     100       100          -  -
identify-overfolder       identify     100       100          -  -
identify-reg              identify     100       100          -  -
Wrote 2300 drills ... Conflicts: 0. Warnings: 0.
```

A rule that yields fewer than 50 drills gets a warning. The same seed always produces a byte-identical file.

### Conflicts and exclusions

A rule *matches* a drill when the line, the villain type and every hand/board condition match. The
conflict checker re-analyses every generated drill from its cards against every other rule on the same
line; a match with a different `correct` answer is a conflict: both rule ids and an example are printed,
the process exits with code 1 and `drills.json` is not written.

The 9 placeholder rules produce **0 conflicts**, so no exclusions were added beyond those in the brief
(`station-river-value` already excludes `FourToFlush` and `FourToStraight`). Rules for different villain
types (for example `overfolder-flop-stab` and `station-flop-no-stab`) never conflict because the villain
type is part of the match.

## Editing content

- `content/types.json`: player types and their stat ranges (VPIP, PFR, 3Bet, WTSD, AF, FoldToCbet).
- `content/ranges.json`: which hands hero can hold in the postflop lines. `open` = hands hero opens from
  UTG/MP/CO/BTN; `bigBlindCall` = hands hero flat-calls in the BB against an open from each seat. Standard
  notation: `22+`, `77-99`, `A2s+`, `KTo+`, `K9s-K6s`, `AK`.
- `content/rules/<id>.json`: one rule per file; the file name must equal the rule `id`.
  - Postflop: `"hero": { "minStrength": ..., "maxStrength": ... }`, `"draws": null | "none"`,
    `"boardRequire"` / `"boardExclude"` (board flag names).
  - Preflop: `"heroGroup": ["Premium", "Strong", "Playable", "Trash"]` instead of `hero`.
  - `correct` must be one of the line's options.

The loader is strict: unknown properties (typos), unknown names, a `correct` answer the line does not
offer, and similar mistakes stop the generator with a list of every problem.

Names you can use:

| Field | Values |
|---|---|
| strengths | `Air WeakPair SecondPair TopPairWeakKicker TopPairGoodKicker Overpair TwoPair Set Trips Straight Flush FullHousePlus` |
| board flags | `Paired Monotone TwoTone Rainbow FourToFlush FourToStraight Dry` |
| lines (options) | `Pre_IsoVsLimper` (Fold, Limp, Iso3, Iso5) · `SRP_HeroIP_FlopVillainChecks` (Check, Bet33, Bet75) · `SRP_HeroOOP_FacingFlopCbet` (Fold, Call, Raise) · `SRP_HeroIP_RiverVillainChecks` (Check, Bet33, Bet75, Bet150) · `SRP_HeroIP_FacingRiverBet` (Fold, Call, Raise) |

After editing, run the generator again and reload the app.

## Mechanics: decisions worth knowing

Full definitions are in [PLAN.md](PLAN.md). The interpretation calls:

- **Hero's hand must use a hole card.** Board-only pairs, trips, straights, flushes and full houses don't count.
  With a 5-card board, a straight/flush/full house only counts if it beats the board on its own (so a
  low heart under a board flush is not a "Flush"). On a turn with board quads, a hole card that is only
  a kicker does not make "FullHousePlus".
- **Pairs** follow the brief's definitions using distinct board ranks; for example QQ on K-K-5 is
  SecondPair, A-K on K-5-5 is TopPairGoodKicker (the 5s are the board's). Good kicker = J+
  (`ClassifierOptions.GoodKickerMinRank`).
- **Draws** (flop and turn only): FlushDraw = exactly four of a suit including a hole card.
  OpenEnded = two or more ranks complete a straight that needs a hole card (double gutshots count, 8 outs);
  Gutshot = exactly one such rank. Cleared when the hand already is that strong.
- **Suit texture** is measured on the whole visible board: 3+ of a suit = Monotone, max 2 = TwoTone,
  all different = Rainbow (a river can never be Rainbow, so it is never Dry).
- **Money**: blinds 0.5/1, 100bb stacks, opens to 2.5bb. Bets are pot × % rounded to 0.1bb. Iso3/Iso5 =
  raise to 3bb/5bb; "Raise" facing a bet = raise to 3× the bet (all-in if that exceeds the stack).
  On river lines, flop and turn are each checked through (50%) or bet 33%/75% by hero and called (25% each).
- **Preflop realism**: in the postflop lines hero's hand must fit the action. The generator draws the seat
  first and rejects the deal if hero wouldn't open that hand from that seat (or wouldn't flat it in the BB
  against that opener), per `content/ranges.json`. So 93o is never opened UTG, and weak hands mostly appear
  as button opens. The iso-vs-limper line keeps any hand: deciding to fold trash is the lesson there.
- **Duplicates** are skipped on hand + board. Preflop spots have no board, so the seating takes its
  place; otherwise `station-iso-big` (only 70 Premium/Strong combos) could never reach 150 drills.
- **Stat lines** are sampled uniformly inside the type's ranges with PFR ≤ VPIP and 3Bet ≤ PFR, and
  are kept only if they fall inside exactly one type (identification drills *and* the villain HUD on action drills).
- **Drill ids** are `ruleId-<hash of the spot>`, so a drill keeps its id (and a student's progress) when
  drills are regenerated with the same content and seed.

## Web app behaviour

- **Session**: due reviews first (most overdue, lowest box), then new drills taken round-robin over the
  rules, least-practised first (all identification drills share one slot). At most 3 drills per rule; the
  order never puts two drills from the same rule next to each other and prefers alternating villain types
  and lines.
- **Leitner**: boxes 1-5 with intervals 1/2/4/8/16 days. Wrong → box 1. Right → one box up. A drill seen for
  the first time counts as box 1 (right → box 2, due in 2 days).
- **More sessions**: after a session, "Another 10 drills" (summary) or "Start another session" (home) starts a
  new one right away. Drills just answered aren't due yet, so the next session brings fresh drills.
- **Streak**: counts days with at least one completed session. The day after the previous one adds 1;
  more sessions the same day change nothing; a gap restarts at 1. The home screen shows 0 once a day was missed.
- Answers are saved immediately; reloading mid-session offers "Resume".
- **Coach review** (`#review`; "Coach review" link at the bottom of the home screen): choose a rule, see 10 random drills in full (with the classifier's
  facts), flag drills with a note (saved in the browser), and export all flags as JSON (download plus a
  copyable text box).

Browser storage keys: `pokerDrills.v1` (progress) and `pokerDrills.flags.v1` (coach flags). Clear
them in the browser's dev tools to start fresh.

## Layout

```
engine/          C# class library: cards, RNG, evaluator, classifier, board flags, lines, rules, generator
engine.tests/    xUnit tests (dotnet test)
generator/       console app: content -> drills.json
content/         types.json, ranges.json, rules/*.json
web/             React + Vite + TypeScript app (reads web/public/drills.json)
PLAN.md          enums, schemas, definitions
IDEAS.md         ideas deliberately not built
```
