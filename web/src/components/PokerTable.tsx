import { useEffect, useMemo, useState } from 'react';
import type { Drill, SeatPlayer, TypeNamer } from '../types';
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
  typeName: TypeNamer;
  /** Replay the hand from the blinds when the drill opens. */
  autoPlay: boolean;
  /** Start that replay after this many actions (the next decision of a hand picks up where the last one was). */
  replayFrom?: number;
  /** The decision is made: show the final state. */
  answered: boolean;
  /** Seat whose HUD is shown; tapping another seat selects it. */
  selectedSeat?: string | null;
  onSelectSeat?: (seat: Seat) => void;
}

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
export function PokerTable({ drill, typeName, autoPlay, replayFrom = 0, answered, selectedSeat = null, onSelectSeat }: Props) {
  const frames = useMemo(() => buildFrames(drill), [drill]);
  const last = frames.length - 1;
  const [animate] = useState(() => autoPlay && !answered && !prefersReducedMotion());
  const [index, setIndex] = useState(() => (animate ? Math.max(0, frames.findIndex((f) => f.applied >= replayFrom)) : last));
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
  const behind = new Set<string>(drill.behind);

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
          player={drill.players.find((p) => p.seat === seat)}
          typeName={typeName}
          heroCards={drill.heroCards}
          acting={acting.has(seat)}
          toAct={seat === hero && atEnd && !answered}
          leftToAct={atEnd && behind.has(seat)}
          selected={seat === selectedSeat}
          onSelect={onSelectSeat}
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
  player: SeatPlayer | undefined;
  typeName: TypeNamer;
  heroCards: string[];
  acting: boolean;
  toAct: boolean;
  /** Still to act after hero's decision. */
  leftToAct: boolean;
  selected: boolean;
  onSelect?: (seat: Seat) => void;
}

function SeatView({ seat, slot, state, role, player, typeName, heroCards, acting, toAct, leftToAct, selected, onSelect }: SeatProps) {
  const classes = [
    'seat',
    `seat-slot-${slot}`,
    role === 'villain' && 'villain',
    state.folded && 'folded',
    acting && 'acting',
    toAct && 'to-act',
    leftToAct && 'left-to-act',
    selected && 'selected',
  ]
    .filter(Boolean)
    .join(' ');
  const action = state.label;

  const boxContent = (
    <>
      <span className="seat-head">
        <span>{seat}</span>
        {seat === 'BTN' && (
          <span className="dealer" title="Dealer button">
            D
          </span>
        )}
        {role === 'villain' && (
          <span className="vs-tag" title="Your opponent in this hand">
            VS
          </span>
        )}
      </span>
      {role === 'hero' && <span className="seat-name">You</span>}
      {player && <span className={`badge badge-sm type-${player.type}`}>{typeName(player.type, true)}</span>}
      <span className="seat-stack">{bb(state.stack)}</span>
    </>
  );

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
      {player && onSelect ? (
        <button
          type="button"
          className="seat-box"
          aria-pressed={selected}
          aria-label={`${seat}, ${typeName(player.type)}${role === 'villain' ? ', your opponent' : ''}. Show HUD stats`}
          onClick={() => onSelect(seat)}
        >
          {boxContent}
        </button>
      ) : (
        <div className="seat-box">{boxContent}</div>
      )}
      {action && <span className={`seat-action action-${state.last!.kind.toLowerCase()}`}>{action}</span>}
    </div>
  );
}
