import type { Drill, DrillAction, TypeNamer } from '../types';

// Replays a drill's action steps into table states for the table view. Pure functions only.

export const SEATS = ['UTG', 'MP', 'CO', 'BTN', 'SB', 'BB'] as const;
export type Seat = (typeof SEATS)[number];
export const STREETS = ['Preflop', 'Flop', 'Turn', 'River'] as const;
export type Street = (typeof STREETS)[number];
export const STARTING_STACK = 100;

export interface SeatState {
  stack: number;
  /** Chips in front of the seat on the current street. */
  inFront: number;
  folded: boolean;
  /** Last action on this street (a fold stays visible for the rest of the hand). */
  last: DrillAction | null;
}

export interface TableState {
  street: Street;
  /** Chips collected from earlier streets. */
  pot: number;
  seats: Record<Seat, SeatState>;
  boardCount: number;
  /** Steps taken in the frame being shown (for highlighting). */
  acted: DrillAction[];
}

/** A pause point in the replay: the first `applied` steps are done and the table shows `street`. */
export interface Frame {
  applied: number;
  street: Street;
}

const round = (n: number) => Math.round(n * 100) / 100;
const streetIndex = (s: Street) => STREETS.indexOf(s);
const boardCountFor = (s: Street) => [0, 3, 4, 5][streetIndex(s)];

export const decisionStreet = (drill: Drill): Street =>
  drill.board.length >= 5 ? 'River' : drill.board.length === 4 ? 'Turn' : drill.board.length === 3 ? 'Flop' : 'Preflop';

export const totalPot = (t: TableState): number => round(t.pot + SEATS.reduce((sum, s) => sum + t.seats[s].inFront, 0));

export const toCallFor = (t: TableState, seat: Seat): number =>
  round(Math.max(...SEATS.map((s) => t.seats[s].inFront)) - t.seats[seat].inFront);

function emptyTable(): TableState {
  const seats = {} as Record<Seat, SeatState>;
  for (const s of SEATS) seats[s] = { stack: STARTING_STACK, inFront: 0, folded: false, last: null };
  return { street: 'Preflop', pot: 0, seats, boardCount: 0, acted: [] };
}

/** Next street: chips in front go into the pot and the new board cards appear. */
function advanceStreet(t: TableState, street: Street): TableState {
  const seats = {} as Record<Seat, SeatState>;
  for (const s of SEATS) {
    const seat = t.seats[s];
    seats[s] = { ...seat, inFront: 0, last: seat.folded ? seat.last : null };
  }
  return { ...t, street, pot: totalPot(t), seats, boardCount: boardCountFor(street) };
}

function applyAction(t: TableState, a: DrillAction): TableState {
  const table = streetIndex(a.street) > streetIndex(t.street) ? advanceStreet(t, a.street) : t;
  const seat = table.seats[a.seat];
  const next: SeatState = { ...seat, last: a };
  if (a.kind === 'Fold') next.folded = true;
  else if (a.kind !== 'Check') {
    next.stack = round(seat.stack - (a.to - seat.inFront));
    next.inFront = a.to;
  }
  return { ...table, seats: { ...table.seats, [a.seat]: next } };
}

/**
 * Pause points for the replay: blinds posted, then one frame per action, with consecutive folds
 * grouped and a separate "deal" frame whenever a new street starts.
 */
export function buildFrames(drill: Drill): Frame[] {
  const actions = drill.actions;
  let i = 0;
  while (i < actions.length && actions[i].kind === 'Post') i++;
  let street: Street = 'Preflop';
  const frames: Frame[] = [{ applied: i, street }];

  while (i < actions.length) {
    const a = actions[i];
    if (streetIndex(a.street) > streetIndex(street)) {
      street = a.street;
      frames.push({ applied: i, street });
    }
    if (a.kind === 'Fold') {
      let j = i;
      while (j < actions.length && actions[j].kind === 'Fold' && actions[j].street === a.street) j++;
      frames.push({ applied: j, street });
      i = j;
    } else {
      frames.push({ applied: i + 1, street });
      i++;
    }
  }

  const decision = decisionStreet(drill);
  if (streetIndex(decision) > streetIndex(street)) frames.push({ applied: actions.length, street: decision });
  return frames;
}

/** Table after a frame. `prevApplied` marks where the frame's own steps start (for highlighting). */
export function tableAt(drill: Drill, frame: Frame, prevApplied = frame.applied): TableState {
  let t = emptyTable();
  for (const a of drill.actions.slice(0, frame.applied)) t = applyAction(t, a);
  if (streetIndex(frame.street) > streetIndex(t.street)) t = advanceStreet(t, frame.street);
  return { ...t, acted: drill.actions.slice(prevApplied, frame.applied) };
}

const VERBS: Record<DrillAction['kind'], [you: string, they: string]> = {
  Post: ['post', 'posts'],
  Fold: ['fold', 'folds'],
  Limp: ['limp', 'limps'],
  Raise: ['raise to', 'raises to'],
  Call: ['call', 'calls'],
  Check: ['check', 'checks'],
  Bet: ['bet', 'bets'],
};

/** "Calling Station (BB)" for villain, "Nit (UTG)" for other players (short type names), "You" for hero. */
function who(drill: Drill, seat: string, typeName: TypeNamer): string {
  if (seat === drill.heroPosition) return 'You';
  const type = seat === drill.villainPosition ? drill.villainType : drill.players.find((p) => p.seat === seat)?.type;
  return type ? `${typeName(type, seat !== drill.villainPosition)} (${seat})` : seat;
}

export function describeAction(drill: Drill, a: DrillAction, typeName: TypeNamer): string {
  const verb = VERBS[a.kind][a.seat === drill.heroPosition ? 0 : 1];
  const amount = a.kind === 'Fold' || a.kind === 'Check' ? '' : ` ${round(a.to)}bb`;
  return `${who(drill, a.seat, typeName)} ${verb}${amount}`;
}

/** Caption for frame i: what just happened. */
export function describeFrame(drill: Drill, frames: Frame[], i: number, typeName: TypeNamer): string {
  if (i === 0) return 'Blinds are in';
  const frame = frames[i];
  const steps = drill.actions.slice(frames[i - 1].applied, frame.applied);
  if (steps.length === 0) return frame.street; // a new street was dealt
  if (steps.every((s) => s.kind === 'Fold')) {
    const names = steps.map((s) => who(drill, s.seat, typeName));
    return names.length === 1 ? `${names[0]} folds` : `${names.join(', ')} fold`;
  }
  return steps.map((s) => describeAction(drill, s, typeName)).join('. ');
}

/** Seat slot around the table: hero at the bottom (0), then clockwise in seating order. */
export function slotOf(seat: Seat, heroSeat: Seat): number {
  return (SEATS.indexOf(seat) - SEATS.indexOf(heroSeat) + SEATS.length) % SEATS.length;
}
