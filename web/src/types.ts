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

export interface TypeSummary {
  id: string;
  name: string;
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
