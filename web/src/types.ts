// Shape of web/public/drills.json, written by the C# generator (engine/Drills.cs).

export type DrillKind = 'action' | 'identify';

export interface DrillOption {
  id: string;
  label: string;
}

export interface StatLine {
  VPIP: number;
  PFR: number;
  '3Bet': number;
  WTSD: number;
  AF: number;
  FoldToCbet: number;
}

export interface DrillFacts {
  handClass: string | null;
  draws: string[];
  boardFlags: string[];
  highCard: string | null;
  handGroup: string | null;
  hand: string | null;
}

/** A player at the table: every seat except hero's, villain included. */
export interface SeatPlayer {
  seat: DrillAction['seat'];
  type: string;
  stats: StatLine;
}

/** One step of the hand. `to` = the seat's total in front of it on this street after the action. */
export interface DrillAction {
  street: 'Preflop' | 'Flop' | 'Turn' | 'River';
  seat: 'UTG' | 'MP' | 'CO' | 'BTN' | 'SB' | 'BB';
  kind: 'Post' | 'Fold' | 'Limp' | 'Raise' | 'Call' | 'Check' | 'Bet';
  to: number;
}

export interface Drill {
  id: string;
  ruleId: string;
  kind: DrillKind;
  line: string;
  villainType: string;
  villainStats: StatLine;
  heroPosition: string | null;
  villainPosition: string | null;
  stacks: { hero: number; villain: number } | null;
  pot: number | null;
  toCall: number | null;
  actionHistory: string[];
  actions: DrillAction[];
  players: SeatPlayer[];
  heroCards: string[];
  board: string[];
  question: string;
  options: DrillOption[];
  correct: string;
  reason: string;
  facts: DrillFacts | null;
}

export interface RuleSummary {
  id: string;
  kind: DrillKind;
  villainType: string;
  line: string;
  conditions: string;
  correct: string;
  reason: string;
  placeholder: boolean;
}

/** Display name of a player type; `short` gives the compact label used on table seats. */
export type TypeNamer = (typeId: string, short?: boolean) => string;

export interface TypeSummary {
  id: string;
  name: string;
  /** Seat label where space is tight (equals `name` unless content sets one). */
  shortName: string;
  description: string;
  ranges: Record<string, [number, number]>;
}

export interface DrillFile {
  schemaVersion: number;
  seed: number;
  rules: RuleSummary[];
  types: TypeSummary[];
  drills: Drill[];
}
