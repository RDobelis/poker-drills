import { useEffect, useRef, useState } from 'react';
import type { Drill } from '../types';
import type { Answer } from '../logic/state';
import { DrillView } from '../components/DrillView';

interface Props {
  drills: Drill[];
  answers: Answer[];
  typeName: (typeId: string) => string;
  onAnswer: (drill: Drill, optionId: string) => void;
  onFinish: () => void;
  onExit: () => void;
}

export function SessionPage({ drills, answers, typeName, onAnswer, onFinish, onExit }: Props) {
  // Resume at the first unanswered drill (after a reload, for example).
  const [index, setIndex] = useState(() => {
    const i = drills.findIndex((d) => !answers.some((a) => a.drillId === d.id));
    return i === -1 ? drills.length - 1 : i;
  });
  const drill = drills[index];
  const answer = answers.find((a) => a.drillId === drill.id);
  const isLast = index === drills.length - 1;
  const correctLabel = drill.options.find((o) => o.id === drill.correct)?.label ?? drill.correct;

  const feedbackRef = useRef<HTMLElement>(null);
  useEffect(() => {
    // Instant, not smooth: smooth scrolling silently does nothing in background/hidden tabs.
    if (answer) feedbackRef.current?.scrollIntoView({ block: 'nearest' });
  }, [answer]);

  const next = () => {
    if (isLast) {
      onFinish();
      return;
    }
    setIndex(index + 1);
    window.scrollTo(0, 0);
  };

  return (
    <main className="app">
      <header className="session-header">
        <button type="button" className="icon-btn" onClick={onExit} aria-label="Back to home">
          ←
        </button>
        <div className="progress">
          <div className="progress-bar" aria-hidden="true">
            <span style={{ width: `${(answers.length / drills.length) * 100}%` }} />
          </div>
          <span className="small muted">
            {index + 1} / {drills.length}
          </span>
        </div>
      </header>

      <DrillView
        key={drill.id}
        drill={drill}
        typeName={typeName}
        chosen={answer?.chosen}
        onChoose={(optionId) => onAnswer(drill, optionId)}
      />

      {answer && (
        <section ref={feedbackRef} className={`feedback ${answer.correct ? 'ok' : 'bad'}`} aria-live="polite">
          <p className="verdict">{answer.correct ? '✓ Correct' : '✗ Not quite'}</p>
          {!answer.correct && (
            <p>
              Correct action: <b>{correctLabel}</b>
            </p>
          )}
          <p className="reason">{drill.reason}</p>
          <button type="button" className="btn btn-primary" onClick={next}>
            {isLast ? 'See results' : 'Next'}
          </button>
        </section>
      )}
    </main>
  );
}
