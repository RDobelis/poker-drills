# Ideas (not built)

Kept out of the prototype on purpose. Each one is a candidate for after the 20-student test.

## Students
- Per-rule accuracy on the home screen, not just per villain type.
- Keyboard shortcuts (1-4) for answering on desktop.
- Installable PWA / offline mode so the app survives flaky mobile connections.
- Schedule by rule as well as by drill, so a rule you keep missing comes back more often.
- Reminder notification when the streak is about to break.

## Coach / content
- Import the exported flags back into the generator to drop or exclude flagged drills automatically.
- More rule conditions: require a specific draw (`"draws": ["FlushDraw"]`), high-card limits
  (`HighCard` is already computed but not used), hero/villain positions, bet size faced.
- More lines: 3-bet pots, multiway pots, turn decisions, villain donk bets, overbet sizes.
- Per-type default sizings (e.g. bigger iso sizes against stations) instead of fixed option sizes.
- A rule linter that reports how much two rules overlap even when they agree.
- Suit-isomorphic dedupe (AsKs ≈ AhKh) for more distinct drills per rule.

## Data / tech
- Split `drills.json` per rule or compress it; it is ~2 MB uncompressed today.
- Progress export/import (a JSON file) so students can move devices without a backend.
- Optional backend for class leaderboards and coach dashboards (out of scope for this prototype).
