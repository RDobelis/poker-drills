import { addDays, type DateKey } from './dates';

/** Review interval in days for boxes 1..5. */
export const BOX_INTERVALS_DAYS = [1, 2, 4, 8, 16] as const;

export type Box = 1 | 2 | 3 | 4 | 5;

export interface DrillProgress {
  box: Box;
  due: DateKey;
  seen: number;
  correct: number;
  lastSeen: DateKey;
}

export const intervalFor = (box: Box): number => BOX_INTERVALS_DAYS[box - 1];

/**
 * Leitner step. Wrong -> box 1. Right -> one box up (max 5).
 * A drill answered for the first time counts as coming from box 1.
 */
export function scheduleAnswer(prev: DrillProgress | undefined, wasCorrect: boolean, today: DateKey): DrillProgress {
  const from = prev?.box ?? 1;
  const box = (wasCorrect ? Math.min(5, from + 1) : 1) as Box;
  return {
    box,
    due: addDays(today, intervalFor(box)),
    seen: (prev?.seen ?? 0) + 1,
    correct: (prev?.correct ?? 0) + (wasCorrect ? 1 : 0),
    lastSeen: today,
  };
}

export const isDue = (p: DrillProgress, today: DateKey): boolean => p.due <= today;
