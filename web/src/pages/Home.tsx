import { useMemo } from 'react';
import type { DrillFile } from '../types';
import type { DateKey } from '../logic/dates';
import { isDue } from '../logic/leitner';
import { visibleStreak, type ActiveSession, type AppState } from '../logic/state';

interface Props {
  data: DrillFile;
  state: AppState;
  today: DateKey;
  sessionToday: ActiveSession | null;
  saveFailed: boolean;
  onStart: () => void;
  onShowSummary: () => void;
}

export function Home({ data, state, today, sessionToday, saveFailed, onStart, onShowSummary }: Props) {
  const streak = visibleStreak(state.streak, today);

  const { due, fresh } = useMemo(() => {
    let dueCount = 0;
    let freshCount = 0;
    for (const d of data.drills) {
      const p = state.progress[d.id];
      if (!p) freshCount++;
      else if (isDue(p, today)) dueCount++;
    }
    return { due: dueCount, fresh: freshCount };
  }, [data, state.progress, today]);

  const answered = sessionToday?.answers.length ?? 0;
  const total = sessionToday?.drillIds.length ?? 0;
  const score = sessionToday?.answers.filter((a) => a.correct).length ?? 0;

  return (
    <main className="app">
      <header className="home-header">
        <h1>Exploit Trainer</h1>
        <p className="muted">Ten drills a day on punishing each player type.</p>
      </header>

      <section className="card streak" aria-label="Streak">
        <div className="streak-num">{streak}</div>
        <div>
          <div className="streak-label">day streak</div>
          <div className="small muted">Best {state.streak.best}</div>
        </div>
      </section>

      {sessionToday?.completed ? (
        <section className="card done">
          <p>
            <b>Today's session is done:</b> {score}/{total} correct. Come back tomorrow.
          </p>
          <button type="button" className="btn" onClick={onShowSummary}>
            See today's summary
          </button>
        </section>
      ) : (
        <button type="button" className="btn btn-primary btn-start" onClick={onStart}>
          {sessionToday ? `Resume today's session (${answered}/${total})` : "Start today's session"}
        </button>
      )}
      <p className="small muted center">
        {due} due for review · {fresh} new drills
      </p>

      <section className="card">
        <h2>Accuracy by villain type</h2>
        <ul className="accuracy">
          {data.types.map((t) => {
            const s = state.typeStats[t.id];
            const pct = s && s.total > 0 ? Math.round((100 * s.correct) / s.total) : null;
            return (
              <li key={t.id}>
                <span className={`badge type-${t.id}`}>{t.name}</span>
                <span className="bar" aria-hidden="true">
                  <span style={{ width: `${pct ?? 0}%` }} />
                </span>
                <span className="pct">{pct === null ? '-' : `${pct}%`}</span>
                <span className="small muted count">
                  {s?.correct ?? 0}/{s?.total ?? 0}
                </span>
              </li>
            );
          })}
        </ul>
      </section>

      {saveFailed && <p className="warning">This browser is blocking storage, so progress will not be saved.</p>}

      <footer className="footer">
        <a href={`${import.meta.env.BASE_URL}review`}>Coach review</a>
      </footer>
    </main>
  );
}
