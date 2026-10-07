import { useState } from 'react';
import type { Drill, StatLine, TypeNamer } from '../types';
import { TextWithCards } from './Cards';
import { PokerTable } from './PokerTable';
import { STAT_LABELS, lineLabel } from './labels';

interface Props {
  drill: Drill;
  typeName: TypeNamer;
  /** The option the player picked. Once set, the options show right/wrong. */
  chosen?: string;
  /** Coach review: everything revealed, correct option marked, classifier facts shown. */
  review?: boolean;
  onChoose?: (optionId: string) => void;
}

export function DrillView({ drill, typeName, chosen, review = false, onChoose }: Props) {
  const revealed = review || chosen !== undefined;
  const isIdentify = drill.kind === 'identify';
  const showType = !isIdentify || revealed; // the type IS the question on identify drills

  // Whose HUD the stat strip shows: villain first; tapping a seat at the table switches it.
  const [hudSeat, setHudSeat] = useState<string | null>(drill.villainPosition);
  const hudPlayer = drill.players.find((p) => p.seat === hudSeat);

  return (
    <article className="drill">
      <div className="drill-head">
        <span className={`badge type-${showType ? drill.villainType : 'unknown'}`}>
          {showType ? typeName(drill.villainType) : 'Unknown player'}
        </span>
        <span className="line-label">{lineLabel(drill.line)}</span>
      </div>

      {!isIdentify && hudPlayer && (
        <div className="hud-label">
          <span className="muted">HUD</span>
          <span className={`badge badge-sm type-${hudPlayer.type}`}>{typeName(hudPlayer.type)}</span>
          <span>
            {hudPlayer.seat}
            {hudPlayer.seat === drill.villainPosition ? ' · your opponent' : ''}
          </span>
          <span className="muted hud-hint">Tap a seat for its stats</span>
        </div>
      )}
      <StatGrid stats={isIdentify ? drill.villainStats : (hudPlayer?.stats ?? drill.villainStats)} compact={!isIdentify} />

      {!isIdentify && (
        <PokerTable
          drill={drill}
          typeName={typeName}
          autoPlay={!review}
          answered={revealed}
          selectedSeat={hudSeat}
          onSelectSeat={setHudSeat}
        />
      )}
      {drill.others.length > 0 && (
        <p className="behind-line">
          Also in the hand:{' '}
          <b>
            {drill.others
              .map((s) => `${typeName(drill.players.find((p) => p.seat === s)?.type ?? '?', true)} (${s})`)
              .join(', ')}
          </b>
        </p>
      )}
      {drill.behind.length > 0 && (
        <p className="behind-line">
          Left to act behind you:{' '}
          <b>
            {drill.behind
              .map((s) => `${typeName(drill.players.find((p) => p.seat === s)?.type ?? '?', true)} (${s})`)
              .join(', ')}
          </b>
        </p>
      )}

      <h2 className="question">{drill.question}</h2>
      <div className={`options${isIdentify ? '' : ' options-grid'}`}>
        {drill.options.map((o) => {
          const isCorrect = o.id === drill.correct;
          const isChosen = o.id === chosen;
          const state = !revealed ? '' : isCorrect ? ' correct' : isChosen ? ' wrong' : ' dim';
          return (
            <button
              key={o.id}
              type="button"
              className={`btn option${state}`}
              disabled={revealed}
              aria-pressed={isChosen}
              onClick={() => onChoose?.(o.id)}
            >
              <span>{o.label}</span>
              {revealed && isCorrect && <span aria-label="correct answer">✓</span>}
              {isChosen && !isCorrect && <span aria-label="your answer">✗</span>}
            </button>
          );
        })}
      </div>

      {!isIdentify && (
        <details className="history-details" open={review}>
          <summary>Hand history</summary>
          <ol className="history">
            {drill.actionHistory.map((line, i) => (
              <li key={i}>
                <HistoryLine text={line} />
              </li>
            ))}
          </ol>
        </details>
      )}

      {review && <Facts drill={drill} />}
    </article>
  );
}

function StatGrid({ stats, compact }: { stats: StatLine; compact: boolean }) {
  const values = stats as unknown as Record<string, number>;
  return (
    <dl className={`stats${compact ? ' compact' : ''}`} aria-label="Villain HUD stats">
      {STAT_LABELS.map(([key, label]) => (
        <div key={key} className="stat">
          <dt>{label}</dt>
          <dd>{key === 'AF' ? values[key].toFixed(1) : values[key]}</dd>
        </div>
      ))}
    </dl>
  );
}

/** "Flop [Ks 7d 2c] (5.5bb): Villain checks." -> street header emphasised, cards drawn. */
function HistoryLine({ text }: { text: string }) {
  const i = text.indexOf(':');
  if (i < 0) return <TextWithCards text={text} />;
  return (
    <>
      <span className="street">
        <TextWithCards text={text.slice(0, i)} />
      </span>
      <TextWithCards text={text.slice(i)} />
    </>
  );
}

function Facts({ drill }: { drill: Drill }) {
  const f = drill.facts;
  return (
    <dl className="facts">
      <div>
        <dt>Drill id</dt>
        <dd>
          <code>{drill.id}</code>
        </dd>
      </div>
      {f?.hand && (
        <div>
          <dt>Hand</dt>
          <dd>
            {f.hand}
            {f.handGroup ? ` · ${f.handGroup}` : ''}
            {f.handClass ? ` · ${f.handClass}` : ''}
          </dd>
        </div>
      )}
      {f?.path && f.path.length > 0 && (
        <div>
          <dt>Earlier</dt>
          <dd>
            {f.path.map((step) => (
              <div key={step}>{step}</div>
            ))}
          </dd>
        </div>
      )}
      {drill.others.length > 0 && (
        <div>
          <dt>Also in</dt>
          <dd>{drill.others.map((s) => `${s} ${drill.players.find((p) => p.seat === s)?.type ?? '?'}`).join(', ')}</dd>
        </div>
      )}
      {drill.behind.length > 0 && (
        <div>
          <dt>Behind</dt>
          <dd>{drill.behind.map((s) => `${s} ${drill.players.find((p) => p.seat === s)?.type ?? '?'}`).join(', ')}</dd>
        </div>
      )}
      {f && drill.board.length > 0 && (
        <>
          <div>
            <dt>Draws</dt>
            <dd>{f.draws.join(', ') || 'none'}</dd>
          </div>
          <div>
            <dt>Board</dt>
            <dd>
              {f.boardFlags.join(', ') || '-'} · high card {f.highCard}
            </dd>
          </div>
        </>
      )}
    </dl>
  );
}
