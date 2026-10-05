import type { Drill } from '../types';

export const FLAGS_KEY = 'pokerDrills.flags.v1';

export interface Flag {
  drillId: string;
  ruleId: string;
  note: string;
  /** ISO-8601 timestamp. */
  flaggedAt: string;
}

export type Flags = Record<string, Flag>;

export function loadFlags(): Flags {
  try {
    const raw = localStorage.getItem(FLAGS_KEY);
    const parsed = raw ? (JSON.parse(raw) as Flags) : {};
    return parsed && typeof parsed === 'object' ? parsed : {};
  } catch {
    return {};
  }
}

export function saveFlags(flags: Flags): boolean {
  try {
    localStorage.setItem(FLAGS_KEY, JSON.stringify(flags));
    return true;
  } catch {
    return false;
  }
}

/** Self-contained export: each flag carries the drill as the coach saw it. */
export function buildFlagExport(flags: Flags, drillsById: ReadonlyMap<string, Drill>, seed: number, now = new Date()) {
  const list = Object.values(flags).sort((a, b) => a.ruleId.localeCompare(b.ruleId) || a.flaggedAt.localeCompare(b.flaggedAt));
  return {
    exportedAt: now.toISOString(),
    drillsSeed: seed,
    count: list.length,
    flags: list.map((f) => {
      const d = drillsById.get(f.drillId);
      return {
        ...f,
        drill: d
          ? {
              villainType: d.villainType,
              line: d.line,
              heroPosition: d.heroPosition,
              villainPosition: d.villainPosition,
              heroCards: d.heroCards,
              board: d.board,
              pot: d.pot,
              actionHistory: d.actionHistory,
              villainStats: d.villainStats,
              options: d.options,
              correct: d.correct,
              facts: d.facts,
            }
          : null, // drill no longer in drills.json (regenerated)
      };
    }),
  };
}
