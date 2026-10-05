import type { Drill } from '../types';
import { addDays, type DateKey } from './dates';
import { scheduleAnswer, type DrillProgress } from './leitner';

export const STORAGE_KEY = 'pokerDrills.v1';

export interface Answer {
  drillId: string;
  chosen: string;
  correct: boolean;
}

export interface ActiveSession {
  /** The day this session counts for. */
  date: DateKey;
  drillIds: string[];
  answers: Answer[];
  completed: boolean;
}

export interface Streak {
  current: number;
  best: number;
  lastDate: DateKey | null;
}

export interface TypeStat {
  correct: number;
  total: number;
}

export interface AppState {
  version: 1;
  progress: Record<string, DrillProgress>;
  typeStats: Record<string, TypeStat>;
  streak: Streak;
  session: ActiveSession | null;
}

export const emptyState = (): AppState => ({
  version: 1,
  progress: {},
  typeStats: {},
  streak: { current: 0, best: 0, lastDate: null },
  session: null,
});

type KeyValueStore = Pick<Storage, 'getItem' | 'setItem'>;

function browserStorage(): KeyValueStore | null {
  try {
    return typeof localStorage === 'undefined' ? null : localStorage;
  } catch {
    return null; // blocked storage throws on access
  }
}

export function loadState(storage: KeyValueStore | null = browserStorage()): AppState {
  try {
    const raw = storage?.getItem(STORAGE_KEY);
    if (!raw) return emptyState();
    const parsed = JSON.parse(raw) as Partial<AppState>;
    if (parsed?.version !== 1) return emptyState();
    return { ...emptyState(), ...parsed } as AppState;
  } catch {
    return emptyState();
  }
}

/** Returns false when the browser refuses to store (private mode, quota). */
export function saveState(state: AppState, storage: KeyValueStore | null = browserStorage()): boolean {
  try {
    if (!storage) return false;
    storage.setItem(STORAGE_KEY, JSON.stringify(state));
    return true;
  } catch {
    return false;
  }
}

export function startSession(state: AppState, drills: readonly Drill[], today: DateKey): AppState {
  return { ...state, session: { date: today, drillIds: drills.map((d) => d.id), answers: [], completed: false } };
}

/** Applies one answer: Leitner box, per-type accuracy, session log. Repeat answers are ignored. */
export function recordAnswer(state: AppState, drill: Drill, chosen: string, today: DateKey): AppState {
  const s = state.session;
  if (!s || s.completed || !s.drillIds.includes(drill.id) || s.answers.some((a) => a.drillId === drill.id)) return state;

  const correct = chosen === drill.correct;
  const stat = state.typeStats[drill.villainType] ?? { correct: 0, total: 0 };
  return {
    ...state,
    progress: { ...state.progress, [drill.id]: scheduleAnswer(state.progress[drill.id], correct, today) },
    typeStats: {
      ...state.typeStats,
      [drill.villainType]: { correct: stat.correct + (correct ? 1 : 0), total: stat.total + 1 },
    },
    session: { ...s, answers: [...s.answers, { drillId: drill.id, chosen, correct }] },
  };
}

export function completeSession(state: AppState): AppState {
  const s = state.session;
  if (!s || s.completed) return state;
  return { ...state, streak: bumpStreak(state.streak, s.date), session: { ...s, completed: true } };
}

/** Same day: unchanged. Day after the last session: +1. Otherwise the streak restarts at 1. */
export function bumpStreak(streak: Streak, day: DateKey): Streak {
  if (streak.lastDate === day) return streak;
  const current = streak.lastDate && addDays(streak.lastDate, 1) === day ? streak.current + 1 : 1;
  return { current, best: Math.max(streak.best, current), lastDate: day };
}

/** The streak shown today: it is still alive if the last session was today or yesterday. */
export function visibleStreak(streak: Streak, today: DateKey): number {
  if (!streak.lastDate) return 0;
  return streak.lastDate === today || addDays(streak.lastDate, 1) === today ? streak.current : 0;
}
