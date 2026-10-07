import { describe, expect, it } from 'vitest';
import type { Drill } from '../types';
import { addDays } from './dates';
import { scheduleAnswer, type DrillProgress } from './leitner';
import { arrange, buildSession, canArrange, MAX_PER_RULE, SESSION_SIZE } from './session';
import {
  bumpStreak,
  completeSession,
  emptyState,
  loadState,
  recordAnswer,
  saveState,
  startSession,
  STORAGE_KEY,
  visibleStreak,
} from './state';

const TODAY = '2026-10-05';

/** Small seeded PRNG so session tests are reproducible. */
function mulberry32(seed: number) {
  return () => {
    seed = (seed + 0x6d2b79f5) | 0;
    let t = Math.imul(seed ^ (seed >>> 15), 1 | seed);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

function drill(id: string, ruleId: string, villainType = 'Nit', line = 'L', kind: Drill['kind'] = 'action'): Drill {
  return {
    id,
    ruleId,
    kind,
    line,
    villainType,
    villainStats: { VPIP: 10, PFR: 8, '3Bet': 2, WTSD: 20, AF: 2, FoldToCbet: 50 },
    heroPosition: null,
    villainPosition: null,
    stacks: null,
    pot: null,
    toCall: null,
    actionHistory: [],
    actions: [],
    players: [],
    behind: [],
    heroCards: [],
    board: [],
    question: 'q',
    options: [
      { id: 'A', label: 'A' },
      { id: 'B', label: 'B' },
    ],
    correct: 'A',
    reason: 'r',
    facts: null,
  };
}

/** 9 action rules over 5 types and 5 lines, plus 5 identify "rules". */
function pool(): Drill[] {
  const types = ['Nit', 'CallingStation', 'Maniac', 'Overfolder', 'Reg'];
  const drills: Drill[] = [];
  for (let r = 0; r < 9; r++) {
    for (let i = 0; i < 20; i++) drills.push(drill(`r${r}-${i}`, `rule-${r}`, types[r % 5], `line-${r % 5}`));
  }
  for (const t of types) {
    for (let i = 0; i < 10; i++) drills.push(drill(`id-${t}-${i}`, `identify-${t}`, t, 'Identify', 'identify'));
  }
  return drills;
}

const progress = (box: DrillProgress['box'], due: string): DrillProgress => ({ box, due, seen: 1, correct: 0, lastSeen: TODAY });

function expectNoSameRuleNeighbours(session: Drill[]) {
  for (let i = 1; i < session.length; i++) expect(session[i].ruleId).not.toBe(session[i - 1].ruleId);
}

describe('dates', () => {
  it('adds calendar days across months, years, leap days and DST changes', () => {
    expect(addDays('2026-01-31', 1)).toBe('2026-02-01');
    expect(addDays('2026-12-31', 1)).toBe('2027-01-01');
    expect(addDays('2024-02-28', 1)).toBe('2024-02-29');
    expect(addDays('2026-03-28', 2)).toBe('2026-03-30'); // EU DST starts 29 March
    expect(addDays('2026-10-24', 2)).toBe('2026-10-26'); // EU DST ends 25 October
    expect(addDays('2026-10-05', 16)).toBe('2026-10-21');
  });
});

describe('leitner', () => {
  it('moves a right answer up one box and schedules by the new box interval', () => {
    expect(scheduleAnswer(undefined, true, TODAY)).toMatchObject({ box: 2, due: '2026-10-07', seen: 1, correct: 1 });
    expect(scheduleAnswer(progress(1, TODAY), true, TODAY)).toMatchObject({ box: 2, due: '2026-10-07' });
    expect(scheduleAnswer(progress(2, TODAY), true, TODAY)).toMatchObject({ box: 3, due: '2026-10-09' });
    expect(scheduleAnswer(progress(3, TODAY), true, TODAY)).toMatchObject({ box: 4, due: '2026-10-13' });
    expect(scheduleAnswer(progress(4, TODAY), true, TODAY)).toMatchObject({ box: 5, due: '2026-10-21' });
    expect(scheduleAnswer(progress(5, TODAY), true, TODAY)).toMatchObject({ box: 5, due: '2026-10-21' });
  });

  it('sends a wrong answer back to box 1 (due tomorrow)', () => {
    expect(scheduleAnswer(undefined, false, TODAY)).toMatchObject({ box: 1, due: '2026-10-06', seen: 1, correct: 0 });
    expect(scheduleAnswer(progress(4, TODAY), false, TODAY)).toMatchObject({ box: 1, due: '2026-10-06', seen: 2 });
  });
});

describe('streak', () => {
  it('counts consecutive days, ignores repeats on the same day and restarts after a gap', () => {
    let s = { current: 0, best: 0, lastDate: null as string | null };
    s = bumpStreak(s, '2026-10-01');
    expect(s.current).toBe(1);
    s = bumpStreak(s, '2026-10-02');
    s = bumpStreak(s, '2026-10-02');
    expect(s).toEqual({ current: 2, best: 2, lastDate: '2026-10-02' });
    s = bumpStreak(s, '2026-10-05');
    expect(s).toEqual({ current: 1, best: 2, lastDate: '2026-10-05' });
  });

  it('shows the streak only while it is still alive', () => {
    const s = { current: 4, best: 4, lastDate: '2026-10-04' };
    expect(visibleStreak(s, '2026-10-04')).toBe(4);
    expect(visibleStreak(s, '2026-10-05')).toBe(4); // can still continue today
    expect(visibleStreak(s, '2026-10-06')).toBe(0);
  });
});

describe('session builder', () => {
  it('builds 10 unique drills, max 3 per rule, never the same rule twice in a row', () => {
    const drills = pool();
    for (let seed = 1; seed <= 300; seed++) {
      const session = buildSession(drills, {}, TODAY, mulberry32(seed));
      expect(session).toHaveLength(SESSION_SIZE);
      expect(new Set(session.map((d) => d.id)).size).toBe(SESSION_SIZE);
      expectNoSameRuleNeighbours(session);
      const perRule = new Map<string, number>();
      for (const d of session) perRule.set(d.ruleId, (perRule.get(d.ruleId) ?? 0) + 1);
      expect(Math.max(...perRule.values())).toBeLessThanOrEqual(MAX_PER_RULE);
    }
  });

  it('mixes villain types and gives identification drills one rotation slot', () => {
    const session = buildSession(pool(), {}, TODAY, mulberry32(7));
    expect(new Set(session.map((d) => d.villainType)).size).toBeGreaterThanOrEqual(4);
    expect(session.filter((d) => d.kind === 'identify').length).toBe(1);
  });

  it('takes due drills first, then new ones, and skips reviews that are not due', () => {
    const drills = pool();
    const p: Record<string, DrillProgress> = {
      'r0-0': progress(1, '2026-10-01'),
      'r1-0': progress(2, TODAY),
      'r2-0': progress(3, '2026-10-04'),
      'r3-0': progress(5, '2026-10-20'), // not due
    };
    for (let seed = 1; seed <= 50; seed++) {
      const ids = buildSession(drills, p, TODAY, mulberry32(seed)).map((d) => d.id);
      expect(ids).toEqual(expect.arrayContaining(['r0-0', 'r1-0', 'r2-0']));
      expect(ids).not.toContain('r3-0');
      expect(ids).toHaveLength(SESSION_SIZE);
    }
  });

  it('caps due drills per rule so one rule cannot fill the session', () => {
    const drills = pool();
    const p: Record<string, DrillProgress> = {};
    for (let i = 0; i < 8; i++) p[`r0-${i}`] = progress(1, '2026-10-01');
    const session = buildSession(drills, p, TODAY, mulberry32(3));
    expect(session.filter((d) => d.ruleId === 'rule-0')).toHaveLength(MAX_PER_RULE);
    expectNoSameRuleNeighbours(session);
  });

  it('shrinks rather than repeating a rule back to back', () => {
    const oneRule = [drill('a', 'x'), drill('b', 'x'), drill('c', 'x')];
    expect(buildSession(oneRule, {}, TODAY, mulberry32(1))).toHaveLength(1);
    const twoRules = [drill('a', 'x'), drill('b', 'x'), drill('c', 'x'), drill('d', 'y')];
    const s = arrange(twoRules, mulberry32(1));
    expect(s).toHaveLength(3);
    expectNoSameRuleNeighbours(s);
  });

  it('arranges every feasible mix without dropping anything', () => {
    const items = ['x', 'x', 'x', 'y', 'y', 'z'].map((r, i) => drill(`d${i}`, r));
    expect(canArrange(items, null)).toBe(true);
    expect(canArrange(items, 'x')).toBe(true); // y x z x y x
    const odd = ['x', 'x', 'x', 'y', 'z'].map((r, i) => drill(`o${i}`, r));
    expect(canArrange(odd, 'x')).toBe(false); // x must take slots 1, 3, 5, but the previous drill was x
    for (let seed = 1; seed <= 100; seed++) {
      const s = arrange(items, mulberry32(seed));
      expect(s).toHaveLength(6);
      expectNoSameRuleNeighbours(s);
    }
  });
});

describe('app state', () => {
  const drills = pool().slice(0, 10);

  it('records an answer once: Leitner box, per-type accuracy and the session log', () => {
    let s = startSession(emptyState(), drills, TODAY);
    s = recordAnswer(s, drills[0], 'A', TODAY);
    s = recordAnswer(s, drills[1], 'B', TODAY);
    const again = recordAnswer(s, drills[1], 'A', TODAY); // double tap / repeat
    expect(again).toBe(s);
    expect(s.progress[drills[0].id]).toMatchObject({ box: 2, due: '2026-10-07' });
    expect(s.progress[drills[1].id]).toMatchObject({ box: 1, due: '2026-10-06' });
    expect(s.typeStats.Nit).toEqual({ correct: 1, total: 2 });
    expect(s.session?.answers.map((a) => a.correct)).toEqual([true, false]);
  });

  it('ignores answers for drills outside the session', () => {
    const s = startSession(emptyState(), drills, TODAY);
    expect(recordAnswer(s, drill('other', 'z'), 'A', TODAY)).toBe(s);
  });

  it('allows another session on the same day with fresh drills and an unchanged streak', () => {
    const all = pool();
    let s = startSession(emptyState(), buildSession(all, {}, TODAY, mulberry32(1)), TODAY);
    const first = s.session!.drillIds;
    for (const id of first) s = recordAnswer(s, all.find((d) => d.id === id)!, 'A', TODAY);
    s = completeSession(s);

    const second = buildSession(all, s.progress, TODAY, mulberry32(2));
    s = startSession(s, second, TODAY);
    for (const d of second) s = recordAnswer(s, d, 'A', TODAY);
    s = completeSession(s);

    expect(second).toHaveLength(SESSION_SIZE);
    expect(second.some((d) => first.includes(d.id))).toBe(false); // answered drills are not due yet
    expect(s.streak).toEqual({ current: 1, best: 1, lastDate: TODAY });
    expect(Object.keys(s.progress)).toHaveLength(2 * SESSION_SIZE);
  });

  it('bumps the streak once when the session completes', () => {
    let s = startSession(emptyState(), drills, TODAY);
    s = completeSession(s);
    expect(s.streak).toEqual({ current: 1, best: 1, lastDate: TODAY });
    expect(completeSession(s)).toBe(s);
  });

  it('survives a save/load round trip and falls back on corrupt data', () => {
    const store = new Map<string, string>();
    const storage = { getItem: (k: string) => store.get(k) ?? null, setItem: (k: string, v: string) => void store.set(k, v) };
    let s = startSession(emptyState(), drills, TODAY);
    s = recordAnswer(s, drills[0], 'A', TODAY);
    s = completeSession(s);
    expect(saveState(s, storage)).toBe(true);
    expect(loadState(storage)).toEqual(s);

    store.set(STORAGE_KEY, '{not json');
    expect(loadState(storage)).toEqual(emptyState());
  });
});
