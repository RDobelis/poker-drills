import type { Drill } from '../types';
import type { Answer } from './state';

// A hand drill asks several decisions; everything else in the app works on single decisions, so a hand is
// turned into one ordinary action drill per step (shared fields from the hand, the rest from the step).

/** The decisions a drill asks for: the drill itself, or one per step of a hand. */
export function decisionsOf(drill: Drill): Drill[] {
  if (drill.kind !== 'hand' || !drill.steps) return [drill];
  return drill.steps.map((step, i) => ({
    ...drill,
    ...step,
    id: `${drill.id}#${i + 1}`,
    kind: 'action',
    steps: undefined,
  }));
}

/** How many answers a drill needs. */
export const decisionCount = (drill: Drill): number => (drill.kind === 'hand' ? (drill.steps?.length ?? 0) : 1);

/** This drill's answers, in the order given (a hand has one per decision). */
export const answersFor = (drill: Drill, answers: readonly Answer[]): Answer[] =>
  answers.filter((a) => a.drillId === drill.id);

/** All of the drill's decisions are answered. */
export const isDone = (drill: Drill, answers: readonly Answer[]): boolean =>
  answersFor(drill, answers).length >= decisionCount(drill);
