// Shape of web/public/drills.json, written by the C# generator (engine/Drills.cs).

export type DrillKind = 'action' | 'identify' | 'hand';

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
  /** How hero played the streets before a turn or river decision, e.g. "Flop: bet 33%, called (rule …)". */
  path?: string[];
}

/** One decision of a hand drill: the spot as hero sees it on that street and the rule that grades it. */
export interface HandStep {
  ruleId: string;
  line: string;
  pot: number;
  toCall: number;
  stacks: { hero: number; villain: number };
  actionHistory: string[];
  actions: DrillAction[];
  /** The board cards visible at this decision. */
  board: string[];
  question: string;
  options: DrillOption[];
  correct: string;
  reason: string;
  facts: DrillFacts;
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
  /** Seats still to act after hero at the decision (empty postflop). */
  behind: DrillAction['seat'][];
  /** Other opponents still in the hand besides villain (extra limpers, the 3-way small blind). */
  others: DrillAction['seat'][];
  heroCards: string[];
  board: string[];
  question: string;
  options: DrillOption[];
  correct: string;
  reason: string;
  facts: DrillFacts | null;
  /**
   * Hand drills (kind "hand") only: the decisions in order. The drill itself holds what they share (players,
   * villain, hero's cards, the full board); its question, options and pot are empty. See logic/hand.ts.
   */
  steps?: HandStep[];
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
