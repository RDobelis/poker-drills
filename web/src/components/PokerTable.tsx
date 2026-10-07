import { useEffect, useMemo, useState } from 'react';
import type { Drill, DrillAction } from '../types';
import { PlayingCard } from './Cards';
import { bb } from './labels';
import {
  SEATS,
  buildFrames,
  describeFrame,
  slotOf,
  tableAt,
  toCallFor,
  totalPot,
  type Frame,
  type Seat,
  type SeatState,
} from '../logic/table';

interface Props {
  drill: Drill;
  typeName: (typeId: string) => string;
  /** Replay the hand from the blinds when the drill opens. */
  autoPlay: boolean;
  /** The decision is made: show the final state. */
  answered: boolean;
}

const ACTION_LABEL: Record<DrillAction['kind'], string> = {
  Post: '',
  Fold: 'Fold',
  Limp: 'Limp',
  Raise: 'Raise',
  Call: 'Call',
  Check: 'Check',
  Bet: 'Bet',
};

function prefersReducedMotion(): boolean {
  return typeof window.matchMedia === 'function' && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
}

/** How long frame i stays up before the next one, in ms: long enough to read what happened. */
function frameDelay(drill: Drill, frames: Frame[], i: number): number {
  if (i === 0) return 400;
  const steps = drill.actions.slice(frames[i - 1].applied, frames[i].applied);
  if (steps.length === 0) return 600; // a street was just dealt
  return steps.every((s) => s.kind === 'Fold') ? 400 : 700;
}

/** 6-max table, hero at the bottom; replays the action before hero's decision. */
export function PokerTable({ drill, typeName, autoPlay, answered }: Props) {
  const frames = useMemo(() => buildFrames(drill), [drill]);
  const last = frames.length - 1;
  const [animate] = useState(() => autoPlay && !answered && !prefersReducedMotion());
  const [index, setIndex] = useState(animate ? 0 : last);
  const [playing, setPlaying] = useState(animate);

  // Answering ends the replay.
  useEffect(() => {
    if (answered) {
      setPlaying(false);
      setIndex(last);
    }
  }, [answered, last]);

  useEffect(() => {
    if (!playing) return;
    if (index >= last) {
      setPlaying(false);
      return;
    }
    const id = window.setTimeout(() => setIndex((i) => Math.min(i + 1, last)), frameDelay(drill, frames, index));
    return () => window.clearTimeout(id);
  }, [playing, index, last, drill, frames]);

  const table = useMemo(
    () => tableAt(drill, frames[index], index > 0 ? frames[index - 1].applied : frames[index].applied),
    [drill, frames, index],
  );

  const hero = drill.heroPosition as Seat;
  const villain = drill.villainPosition as Seat;
  const atEnd = index === last;
  const toCall = toCallFor(table, hero);
  const acting = new Set(table.acted.map((a) => a.seat));

  const replay = () => {
    setIndex(0);
    setPlaying(true);
  };
  const skip = () => {
    setPlaying(false);
    setIndex(last);
  };

  return (
    <section className="poker-table" aria-label="Poker table">
      <div className="felt" aria-hidden="true" />

      <div className="table-center">
        <div className="pot">
          Pot <b>{bb(totalPot(table))}</b>
        </div>
        <div className="board-cards" aria-label="Board">
          {drill.board.slice(0, table.boardCount).map((c) => (
            <PlayingCard key={c} code={c} size="table" />
          ))}
        </div>
        <p className="caption" aria-live="polite">
          {describeFrame(drill, frames, index, typeName)}
          {atEnd && !answered && (
            <span className="your-move">Your move{toCall > 0 ? ` · ${bb(toCall)} to call` : ''}</span>
          )}
        </p>
      </div>

      {SEATS.map((seat) => (
        <SeatView
          key={seat}
          seat={seat}
          slot={slotOf(seat, hero)}
          state={table.seats[seat]}
          role={seat === hero ? 'hero' : seat === villain ? 'villain' : 'other'}
          villainType={drill.villainType}
          villainName={typeName(drill.villainType)}
          heroCards={drill.heroCards}
          acting={acting.has(seat)}
          toAct={seat === hero && atEnd && !answered}
        />
      ))}

      {SEATS.filter((s) => table.seats[s].inFront > 0).map((s) => (
        <span key={`bet-${s}-${table.street}`} className={`bet-chip bet-slot-${slotOf(s, hero)}`}>
          {bb(table.seats[s].inFront)}
        </span>
      ))}

      <button type="button" className="table-btn" onClick={atEnd ? replay : skip}>
        {atEnd ? '↺ Replay' : 'Skip ▸▸'}
      </button>
    </section>
  );
}

interface SeatProps {
  seat: Seat;
  slot: number;
  state: SeatState;
  role: 'hero' | 'villain' | 'other';
  villainType: string;
  villainName: string;
  heroCards: string[];
  acting: boolean;
  toAct: boolean;
}

function SeatView({ seat, slot, state, role, villainType, villainName, heroCards, acting, toAct }: SeatProps) {
  const classes = ['seat', `seat-slot-${slot}`, state.folded && 'folded', acting && 'acting', toAct && 'to-act']
    .filter(Boolean)
    .join(' ');
  const action = state.last && state.last.kind !== 'Post' ? ACTION_LABEL[state.last.kind] : null;

  return (
    <div className={classes}>
      {role === 'hero' ? (
        <div className="seat-cards hero-cards" aria-label="Your hand">
          {heroCards.map((c) => (
            <PlayingCard key={c} code={c} size="hero" />
          ))}
        </div>
      ) : (
        !state.folded && (
          <div className="seat-cards" aria-hidden="true">
            <span className="card-back" />
            <span className="card-back" />
          </div>
        )
      )}
      <div className="seat-box">
        <div className="seat-head">
          <span>{seat}</span>
          {seat === 'BTN' && (
            <span className="dealer" title="Dealer button">
              D
            </span>
          )}
        </div>
        {role === 'hero' && <div className="seat-name">You</div>}
        {role === 'villain' && <span className={`badge badge-sm type-${villainType}`}>{villainName}</span>}
        <div className="seat-stack">{bb(state.stack)}</div>
      </div>
      {action && <span className={`seat-action action-${state.last!.kind.toLowerCase()}`}>{action}</span>}
    </div>
  );
}
