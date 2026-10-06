import type { Drill } from '../types';
import type { Answer } from '../logic/state';
import { CardRow } from '../components/Cards';
import { ruleTitle } from '../components/labels';

interface Props {
  drills: Drill[];
  answers: Answer[];
  typeName: (typeId: string) => string;
  onAnother: () => void;
  onDone: () => void;
}

interface Group {
  ruleId: string;
  villainType: string;
  reason: string;
  items: { drill: Drill; answer: Answer }[];
}

const optionLabel = (d: Drill, id: string) => d.options.find((o) => o.id === id)?.label ?? id;

export function Summary({ drills, answers, typeName, onAnother, onDone }: Props) {
  const byId = new Map(drills.map((d) => [d.id, d]));
  const score = answers.filter((a) => a.correct).length;

  // Mistakes grouped by rule, in the order they were made.
  const groups: Group[] = [];
  for (const answer of answers) {
    const drill = byId.get(answer.drillId);
    if (!drill || answer.correct) continue;
    let g = groups.find((x) => x.ruleId === drill.ruleId);
    if (!g) {
      g = { ruleId: drill.ruleId, villainType: drill.villainType, reason: drill.reason, items: [] };
      groups.push(g);
    }
    g.items.push({ drill, answer });
  }

  return (
    <main className="app">
      <h1>Session complete</h1>
      <section className="card score">
        <div className="score-num">
          {score}
          <span>/{answers.length}</span>
        </div>
        <p className="muted">{groups.length === 0 ? 'Perfect session.' : 'Review the mistakes below.'}</p>
      </section>

      {groups.length > 0 && (
        <section>
          <h2>Mistakes by rule</h2>
          {groups.map((g) => (
            <article key={g.ruleId} className="card mistake-group">
              <header className="mistake-head">
                <span className={`badge type-${g.villainType}`}>{typeName(g.villainType)}</span>
                <h3>{ruleTitle(g.ruleId)}</h3>
              </header>
              <p className="reason">{g.reason}</p>
              <ul className="mistakes">
                {g.items.map(({ drill, answer }) => (
                  <li key={drill.id}>
                    <div className="mistake-spot">
                      {drill.kind === 'identify' ? (
                        <span className="small">
                          VPIP {drill.villainStats.VPIP} · PFR {drill.villainStats.PFR} · 3Bet {drill.villainStats['3Bet']} ·
                          WTSD {drill.villainStats.WTSD} · AF {drill.villainStats.AF.toFixed(1)} · Fold c-bet{' '}
                          {drill.villainStats.FoldToCbet}
                        </span>
                      ) : (
                        <>
                          <CardRow codes={drill.heroCards} size="mini" />
                          {drill.board.length > 0 ? (
                            <>
                              <span className="muted small"> on </span>
                              <CardRow codes={drill.board} size="mini" />
                            </>
                          ) : (
                            <span className="muted small"> preflop</span>
                          )}
                        </>
                      )}
                    </div>
                    <div className="small">
                      You: <b className="bad-text">{optionLabel(drill, answer.chosen)}</b> · Correct:{' '}
                      <b className="ok-text">{optionLabel(drill, drill.correct)}</b>
                    </div>
                  </li>
                ))}
              </ul>
            </article>
          ))}
        </section>
      )}

      <button type="button" className="btn btn-primary" onClick={onAnother}>
        Another 10 drills
      </button>
      <button type="button" className="btn" onClick={onDone}>
        Back to home
      </button>
    </main>
  );
}
