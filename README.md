# Poker exploit trainer (prototype)

A coach writes short exploit **rules** (`content/rules/*.json`). A C# **generator** turns each rule into
hundreds of concrete drills (`web/public/drills.json`). A mobile-first **web app** serves sessions of 10
drills with Leitner spaced repetition; progress lives in the browser (localStorage). No backend, no accounts.

**Live:** https://rdobelis.github.io/poker-drills/ (coach page: https://rdobelis.github.io/poker-drills/#review)

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

## Deployment (GitHub Pages)

Every push to `main` runs [.github/workflows/pages.yml](.github/workflows/pages.yml): engine tests, drill
generation from `content/` (same command and seed as above), web tests, build, and deploy to GitHub Pages.
Editing a rule or `ranges.json` on GitHub is therefore enough to update the live site. A failing test or a
rule conflict stops the deploy and the previous version stays live. The workflow can also be started by
hand from the repository's Actions tab ("Deploy to GitHub Pages" → Run workflow).

## Generator output

With the placeholder content and seed 42:

```
Rule                                Kind      Drills  Attempts  Conflicts  Warnings
maniac-3bet-4bet-premium            action       200     10666          0  -
maniac-3bet-call-strong             action       200      9121          0  -
maniac-flop-call-down               action       200      2415          0  -
maniac-river-bluffcatch             action       200      4750          0  -
nit-3bet-fold                       action       200      1633          0  -
nit-river-fold-one-pair             action       200     11199          0  -
overfolder-3way-dry-stab            action       200     11744          0  -
overfolder-flop-stab                action       200      5710          0  -
overfolder-turn-barrel              action       200      1851          0  -
station-3way-no-stab                action       200      1058          0  -
station-flop-no-stab                action       200      1657          0  -
station-iso-big                     action       200      6589          0  -
station-iso-playable                action       200      2696          0  -
station-iso-playable-maniac-behind  action       200      9233          0  -
station-iso-trash                   action       200       237          0  -
station-river-no-bluff              action       200      1926          0  -
station-river-value                 action       200      3878          0  -
station-turn-no-bluff               action       200      2794          0  -
station-turn-value                  action       200      4240          0  -
identify-nit                        identify     100       100          -  -
identify-calling-station            identify     100       100          -  -
identify-maniac                     identify     100       100          -  -
identify-overfolder                 identify     100       100          -  -
identify-reg                        identify     100       100          -  -
hands-calling-station               hand         200     11158          -  -
hands-overfolder                    hand          50      6237          -  -
Wrote 4550 drills ... Conflicts: 0. Warnings: 0.
```

Turn and river rules need more attempts than flop rules because a deal is dropped when villain would have folded
to one of hero's earlier bets (see "Turn and river spots are reached by the rules" below). `hands-*` rows are the
multi-street hand drills (see "Hand drills" below): 50 per rule a hand can end at.

A rule that yields fewer than 50 drills gets a warning. The same seed always produces a byte-identical file.

### Conflicts and exclusions

A rule *matches* a drill when the line, the villain type and every hand/board condition match. The
conflict checker re-analyses every generated drill from its cards against every other rule on the same
line; a match with a different `correct` answer is a conflict: both rule ids and an example are printed,
the process exits with code 1 and `drills.json` is not written.

The 19 placeholder rules produce **0 conflicts**. Beyond the brief's exclusions (`station-river-value` already
excludes `FourToFlush` and `FourToStraight`), `station-iso-playable` excludes a maniac behind so it doesn't clash
with `station-iso-playable-maniac-behind`. Every decision of every hand drill is checked the same way. Rules for different villain
types (for example `overfolder-flop-stab` and `station-flop-no-stab`) never conflict because the villain
type is part of the match.

## Editing content

- `content/types.json`: player types and their stat ranges (VPIP, PFR, 3Bet, WTSD, AF, FoldToCbet).
  `tableShare` (optional, default 1) sets how often each type sits in the other seats at the table;
  `shortName` (optional) is the label on a table seat when the full name is too long.
- `content/ranges.json`: which hands hero can hold in the postflop lines. `open` = hands hero opens from
  UTG/MP/CO/BTN; `bigBlindCall` = hands hero flat-calls in the BB against an open from each seat. Standard
  notation: `22+`, `77-99`, `A2s+`, `KTo+`, `K9s-K6s`, `AK`.
- `content/rules/<id>.json`: one rule per file; the file name must equal the rule `id` (ids starting with
  `identify-` or `hands-` are reserved for the generated identification and hand drills).
  - Postflop: `"hero": { "minStrength": ..., "maxStrength": ... }`, `"draws": null | "none"`,
    `"boardRequire"` / `"boardExclude"` (board flag names).
  - Preflop: `"heroGroup": ["Premium", "Strong", "Playable", "Trash"]` instead of `hero`.
  - Preflop, optional: `"behind": { "require": ["Maniac"], "exclude": ["Nit"] }` reacts to the players left to
    act after hero: every `require` type must be among them, no `exclude` type may be. (Postflop nobody is
    behind hero, so the loader rejects it there.) When a general rule and a "behind" rule overlap with
    different answers, the conflict checker flags it; add an `exclude` to the general rule, as
    `station-iso-playable` does.
  - Multiway spots, optional: `"others": { "require": [...], "exclude": [...] }` works the same way for the
    other opponents still in the hand besides villain (extra limpers, the 3-way small blind), e.g.
    `overfolder-3way-dry-stab` excludes a station in the small blind.
  - `correct` must be one of the line's options.

The loader is strict: unknown properties (typos), unknown names, a `correct` answer the line does not
offer, and similar mistakes stop the generator with a list of every problem.

Names you can use:

| Field | Values |
|---|---|
| strengths | `Air WeakPair SecondPair TopPairWeakKicker TopPairGoodKicker Overpair TwoPair Set Trips Straight Flush FullHousePlus` |
| board flags | `Paired Monotone TwoTone Rainbow FourToFlush FourToStraight Dry` |
| lines (options) | `Pre_IsoVsLimper` (Fold, Limp, Iso3, Iso5) · `SRP_HeroIP_FlopVillainChecks` (Check, Bet33, Bet75) · `SRP_HeroOOP_FacingFlopCbet` (Fold, Call, Raise) · `SRP_HeroIP_TurnVillainChecks` (Check, Bet33, Bet75): villain checks the flop, hero plays it by the rules, villain checks the turn · `SRP_HeroIP_RiverVillainChecks` (Check, Bet33, Bet75, Bet150) · `SRP_HeroIP_FacingRiverBet` (Fold, Call, Raise) · `Pre_FacingThreeBet` (Fold, Call, FourBet): hero opens, villain 3-bets (3x in position, 4x from the blinds) · `SRP_3Way_FlopCheckedToHero` (Check, Bet33, Bet75): hero opens the button, the small blind (a player who cold-calls by type) and villain in the big blind call, both check the flop |

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
- **Turn and river spots are reached by the rules.** Villain checks the earlier streets to hero; hero's play
  on each is what the trainer teaches there: the first rule (by id) on that street's "villain checks" line
  (`SRP_HeroIP_FlopVillainChecks`, `SRP_HeroIP_TurnVillainChecks`) that matches villain's type and the cards
  seen so far, otherwise a default: top pair good kicker or better bets 75%, second pair or better or a
  flush/open-ended draw bets 33%, the rest checks. When hero bets, villain calls with probability 100% minus
  their FoldToCbet; if villain would fold, the deal is dropped. So a station river drill never follows a flop
  stab that `station-flop-no-stab` says not to make. The coach view shows the path under "Earlier", e.g.
  "Flop: check (rule station-flop-no-stab) · Turn: bet 33%, called (default for Air + FlushDraw)".
- **Hand drills** (multi-street): the student plays several streets of one hand. A hand ends at a turn or river
  rule (the "anchor") whose villain type also has a rule for an earlier street, and is dealt like that rule's
  drills; it is kept only if a rule decided at least one earlier street. Each street a rule decided becomes a
  decision, graded by that rule; streets no rule covers are played by the default and are just part of the
  story. With the placeholder rules that gives station hands (flop no-stab → turn value/no-bluff → river
  value/no-bluff) and overfolder hands (flop stab → turn barrel); nit and maniac have no rules for earlier
  streets yet, so they get none. Hands are listed per villain type as `hands-<type>`.
- **Preflop realism**: in the postflop lines hero's hand must fit the action. The generator draws the seat
  first and rejects the deal if hero wouldn't open that hand from that seat (or wouldn't flat it in the BB
  against that opener), per `content/ranges.json`. So 93o is never opened UTG, and weak hands mostly appear
  as button opens. The iso-vs-limper line keeps any hand: deciding to fold trash is the lesson there.
- **Other players act by type** (placeholder model in `engine/Behaviour.cs`, read from each player's own HUD
  line): when nobody has raised they raise PFR%, limp 60% of (VPIP - PFR) and fold the rest; facing a raise
  they 3-bet 3Bet%, cold-call 50% of (VPIP - PFR) and fold the rest. The seating is drawn until those actions
  fit the spot, so the players who fold before hero are mostly tight types, and in "villain limps" spots
  loose players often limp in as well (pot +1bb and iso sizes +1bb per extra limper). Raises and cold calls by
  other players are filtered out for now: they would make 3-bet or multiway pots, which no spot covers yet.
- **Duplicates** are skipped on hand + board. Preflop spots have no board, so the seating takes its
  place; otherwise `station-iso-big` (only 70 Premium/Strong combos) could never reach 150 drills.
- **Stat lines** are sampled uniformly inside the type's ranges with PFR ≤ VPIP and 3Bet ≤ PFR, and
  are kept only if they fall inside exactly one type (identification drills *and* the villain HUD on action drills).
- **Drill ids** are `ruleId-<hash of the spot>`, so a drill keeps its id (and a student's progress) when
  drills are regenerated with the same content and seed.

## Web app behaviour

- **Table view**: action drills are shown on a 6-max table with hero at the bottom. Every seat shows its
  position, stack, last action and the type of the player sitting there (drawn by `tableShare`, each with
  its own HUD line); villain's seat is marked "VS". Tapping a seat shows that player's HUD stats. Bets sit in
  front of the seats and the pot and board in the middle. Players still to act after hero get a dashed
  outline and are listed under the table ("Left to act behind you"); rules can react to them via `behind`.
  The other players act by type before hero decides (see "Other players" below), and every caption names
  the player by type ("Station (UTG) limps 1bb", "Nit (CO) folds"). The action before hero's decision replays step by step (Skip /
  Replay buttons; no animation with reduced-motion settings). The text hand history is under the answers.
- **Session**: due reviews first (most overdue, lowest box), then new drills taken round-robin over the
  rules, least-practised first (all identification drills share one slot; each villain type's hands are one
  slot). At most 3 drills per rule; the order never puts two drills from the same rule next to each other and
  prefers alternating villain types and lines.
- **Hands**: a hand drill shows "Whole hand: Flop · Turn · River" above the table. The student answers each
  decision and gets feedback with that rule's reason, then "Continue to the turn": the table replays from where
  the last decision was (hero's play, villain's call, the next card, villain's check). After a wrong answer the
  hand goes on with the correct play, so later decisions are always the spots the rules teach. A hand counts
  as one drill in the session and the Leitner boxes (up a box only if every decision was right); accuracy by
  villain type and the summary count each decision, and mistakes are listed under each decision's own rule.
- **Leitner**: boxes 1-5 with intervals 1/2/4/8/16 days. Wrong → box 1. Right → one box up. A drill seen for
  the first time counts as box 1 (right → box 2, due in 2 days).
- **More sessions**: after a session, "Another 10 drills" (summary) or "Start another session" (home) starts a
  new one right away. Drills just answered aren't due yet, so the next session brings fresh drills.
- **Streak**: counts days with at least one completed session. The day after the previous one adds 1;
  more sessions the same day change nothing; a gap restarts at 1. The home screen shows 0 once a day was missed.
- Answers are saved immediately; reloading mid-session offers "Resume".
- **Coach review** (`#review`; "Coach review" link at the bottom of the home screen): choose a rule, see 10 random drills in full (with the classifier's
  facts), flag drills with a note (saved in the browser), and export all flags as JSON (download plus a
  copyable text box). For `hands-<type>`, each hand shows all its decisions with the rule behind each.

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
