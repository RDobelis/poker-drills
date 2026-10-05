import type { Drill } from '../types';
import type { DateKey } from './dates';
import { isDue, type DrillProgress } from './leitner';

export const SESSION_SIZE = 10;
/** At most this many drills from one rule per session, so a session always mixes spots. */
export const MAX_PER_RULE = 3;

export type Random = () => number;

export function shuffle<T>(items: readonly T[], random: Random): T[] {
  const a = [...items];
  for (let i = a.length - 1; i > 0; i--) {
    const j = Math.floor(random() * (i + 1));
    [a[i], a[j]] = [a[j], a[i]];
  }
  return a;
}

/** All identification drills share one rotation slot so they don't crowd out the action rules. */
export const slotOf = (d: Drill): string => (d.kind === 'identify' ? 'identify' : d.ruleId);

/**
 * Picks today's drills: due reviews first (most overdue, lowest box), then new drills taken
 * round-robin over rules (least-practised rules first). Then orders them with arrange().
 */
export function buildSession(
  drills: readonly Drill[],
  progress: Readonly<Record<string, DrillProgress>>,
  today: DateKey,
  random: Random = Math.random,
  size = SESSION_SIZE,
): Drill[] {
  const picked: Drill[] = [];
  const pickedIds = new Set<string>();
  const perRule = new Map<string, number>();
  const room = (d: Drill) => (perRule.get(d.ruleId) ?? 0) < MAX_PER_RULE;
  const take = (d: Drill) => {
    if (picked.length >= size || pickedIds.has(d.id) || !room(d)) return false;
    picked.push(d);
    pickedIds.add(d.id);
    perRule.set(d.ruleId, (perRule.get(d.ruleId) ?? 0) + 1);
    return true;
  };

  const seen = drills.filter((d) => progress[d.id]);
  const byDue = (a: Drill, b: Drill) =>
    progress[a.id].due.localeCompare(progress[b.id].due) || progress[a.id].box - progress[b.id].box;

  // 1. Due reviews.
  for (const d of shuffle(seen.filter((d) => isDue(progress[d.id], today)), random).sort(byDue)) take(d);

  // 2. New drills, one per rule slot per round.
  const fresh = new Map<string, Drill[]>();
  for (const d of shuffle(drills.filter((d) => !progress[d.id]), random)) {
    const slot = slotOf(d);
    if (!fresh.has(slot)) fresh.set(slot, []);
    fresh.get(slot)!.push(d);
  }
  const practised = new Map<string, number>();
  for (const d of seen) practised.set(slotOf(d), (practised.get(slotOf(d)) ?? 0) + 1);
  const slots = shuffle([...fresh.keys()], random).sort((a, b) => (practised.get(a) ?? 0) - (practised.get(b) ?? 0));

  let added = true;
  while (picked.length < size && added) {
    added = false;
    for (const slot of slots) {
      const queue = fresh.get(slot)!;
      const i = queue.findIndex(room);
      if (i >= 0 && take(queue.splice(i, 1)[0])) added = true;
    }
  }

  // 3. Pool exhausted: pull the soonest upcoming reviews forward.
  for (const d of shuffle(seen, random).sort(byDue)) take(d);

  return arrange(picked, random);
}

function countRules(items: readonly Drill[]): Map<string, number> {
  const counts = new Map<string, number>();
  for (const d of items) counts.set(d.ruleId, (counts.get(d.ruleId) ?? 0) + 1);
  return counts;
}

/**
 * True if the items can be ordered with no two neighbours from the same rule
 * and the first item's rule differs from prevRule.
 */
export function canArrange(items: readonly Drill[], prevRule: string | null): boolean {
  const m = items.length;
  for (const [rule, n] of countRules(items)) {
    if (n > (rule === prevRule ? Math.floor(m / 2) : Math.ceil(m / 2))) return false;
  }
  return true;
}

/**
 * Orders drills so that no two neighbours share a rule (hard rule), preferring a different
 * villain type and line from the previous drill. Each step keeps the rest arrangeable, so the
 * greedy never gets stuck. If the input can't be arranged at all, extra drills of the most
 * common rule are dropped.
 */
export function arrange(items: readonly Drill[], random: Random): Drill[] {
  const pool = [...items];
  while (pool.length > 0 && !canArrange(pool, null)) {
    const [top] = [...countRules(pool)].sort((a, b) => b[1] - a[1])[0];
    pool.splice(pool.map((d) => d.ruleId).lastIndexOf(top), 1);
  }

  const out: Drill[] = [];
  let rest = shuffle(pool, random);
  while (rest.length > 0) {
    const prev = out.at(-1);
    let best: Drill | undefined;
    let bestScore = -Infinity;
    for (const d of rest) {
      if (prev && d.ruleId === prev.ruleId) continue;
      if (!canArrange(rest.filter((x) => x !== d), d.ruleId)) continue;
      const score =
        (prev && d.villainType !== prev.villainType ? 2 : 0) + (prev && d.line !== prev.line ? 1 : 0) + random() * 0.5;
      if (score > bestScore) {
        best = d;
        bestScore = score;
      }
    }
    if (!best) break; // cannot happen: canArrange() guarantees a candidate
    out.push(best);
    rest = rest.filter((x) => x !== best);
  }
  return out;
}
