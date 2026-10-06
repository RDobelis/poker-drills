import { useCallback, useEffect, useMemo, useState } from 'react';
import type { Drill, DrillFile } from '../types';
import { todayKey } from '../logic/dates';
import { buildSession } from '../logic/session';
import { completeSession, loadState, recordAnswer, saveState, startSession } from '../logic/state';
import { Home } from './Home';
import { SessionPage } from './SessionPage';
import { Summary } from './Summary';

type Screen = 'home' | 'session' | 'summary';

/** Student side: home, the daily session and its summary. All progress lives in localStorage. */
export function Trainer({ data }: { data: DrillFile }) {
  const drillById = useMemo(() => new Map(data.drills.map((d) => [d.id, d])), [data]);
  const typeName = useCallback((id: string) => data.types.find((t) => t.id === id)?.name ?? id, [data]);

  const [state, setState] = useState(loadState);
  const [saveFailed, setSaveFailed] = useState(false);
  const [screen, setScreen] = useState<Screen>('home');

  // Persist every change; answers are saved as soon as they are given.
  useEffect(() => {
    if (!saveState(state)) setSaveFailed(true);
  }, [state]);

  const today = todayKey();
  const session = state.session;
  // Drills can disappear if drills.json was regenerated with different content; skip those.
  const sessionDrills = useMemo(
    () => (session?.drillIds ?? []).map((id) => drillById.get(id)).filter((d): d is Drill => d !== undefined),
    [session, drillById],
  );
  const sessionToday = session && session.date === today && sessionDrills.length > 0 ? session : null;

  const go = (next: Screen) => {
    setScreen(next);
    window.scrollTo(0, 0);
  };

  const startOrResume = () => {
    const day = todayKey();
    const resumable = session && session.date === day && !session.completed && sessionDrills.length > 0;
    if (!resumable) {
      const picked = buildSession(data.drills, state.progress, day);
      if (picked.length === 0) return;
      setState((s) => startSession(s, picked, day));
    }
    go('session');
  };

  if (screen === 'session' && session && !session.completed && sessionDrills.length > 0) {
    return (
      <SessionPage
        key={`${session.date}:${session.drillIds.join(',')}`}
        drills={sessionDrills}
        answers={session.answers}
        typeName={typeName}
        onAnswer={(drill, optionId) => setState((s) => recordAnswer(s, drill, optionId, todayKey()))}
        onFinish={() => {
          setState((s) => completeSession(s));
          go('summary');
        }}
        onExit={() => go('home')}
      />
    );
  }

  if (screen === 'summary' && session) {
    return (
      <Summary
        drills={sessionDrills}
        answers={session.answers}
        typeName={typeName}
        onAnother={startOrResume}
        onDone={() => go('home')}
      />
    );
  }

  return (
    <Home
      data={data}
      state={state}
      today={today}
      sessionToday={sessionToday}
      saveFailed={saveFailed}
      onStart={startOrResume}
      onShowSummary={() => go('summary')}
    />
  );
}
