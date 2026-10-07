import { useEffect, useMemo, useRef, useState } from 'react';
import type { Drill } from '../types';
import type { Answer } from '../logic/state';
import { answersFor, decisionsOf, isDone } from '../logic/hand';
import { decisionStreet } from '../logic/table';
import { DrillView } from '../components/DrillView';

interface Props {
  drills: Drill[];
  answers: Answer[];
  typeName: (typeId: string) => string;
  /** `step` is the decision within a hand (always 0 for other drills). */
  onAnswer: (drill: Drill, optionId: string, step: number) => void;
  onFinish: () => void;
  onExit: () => void;
}

export function SessionPage({ drills, answers, typeName, onAnswer, onFinish, onExit }: Props) {
  // Resume at the first drill not fully answered (after a reload, for example)...
  const [index, setIndex] = useState(() => {
    const i = drills.findIndex((d) => !isDone(d, answers));
    return i === -1 ? drills.length - 1 : i;
  });
  const drill = drills[index];
  const decisions = useMemo(() => decisionsOf(drill), [drill]);
  const given = answersFor(drill, answers);
  // ...and, in a hand, at its first unanswered decision.
  const [step, setStep] = useState(() => Math.min(given.length, decisions.length - 1));

  const decision = decisions[step];
  const answer = given[step];
  const isLastStep = step === decisions.length - 1;
  const isLast = index === drills.length - 1;
  const correctLabel = decision.options.find((o) => o.id === decision.correct)?.label ?? decision.correct;
  const doneCount = drills.filter((d) => isDone(d, answers)).length;

  const feedbackRef = useRef<HTMLElement>(null);
  useEffect(() => {
    // Instant, not smooth: smooth scrolling silently does nothing in background/hidden tabs.
    if (answer) feedbackRef.current?.scrollIntoView({ block: 'nearest' });
  }, [answer]);

  const next = () => {
    if (!isLastStep) {
      setStep(step + 1);
    } else if (isLast) {
      onFinish();
      return;
    } else {
      setIndex(index + 1);
      setStep(0);
    }
    window.scrollTo(0, 0);
  };

  const nextLabel = !isLastStep
    ? `Continue to the ${decisionStreet(decisions[step + 1]).toLowerCase()}`
    : isLast
      ? 'See results'
      : 'Next';

  return (
    <main className="app">
      <header className="session-header">
        <button type="button" className="icon-btn" onClick={onExit} aria-label="Back to home">
          ←
        </button>
        <div className="progress">
          <div className="progress-bar" aria-hidden="true">
            <span style={{ width: `${(doneCount / drills.length) * 100}%` }} />
          </div>
          <span className="small muted">
            {index + 1} / {drills.length}
          </span>
        </div>
      </header>

      {drill.kind === 'hand' && <HandSteps decisions={decisions} given={given} step={step} />}

      <DrillView
        key={decision.id}
        drill={decision}
        typeName={typeName}
        chosen={answer?.chosen}
        replayFrom={step > 0 ? decisions[step - 1].actions.length : 0}
        onChoose={(optionId) => onAnswer(drill, optionId, step)}
      />

      {answer && (
        <section ref={feedbackRef} className={`feedback ${answer.correct ? 'ok' : 'bad'}`} aria-live="polite">
          <p className="verdict">{answer.correct ? '✓ Correct' : '✗ Not quite'}</p>
          {!answer.correct && (
            <p>
              Correct action: <b>{correctLabel}</b>
            </p>
          )}
          <p className="reason">{decision.reason}</p>
          {!answer.correct && !isLastStep && <p className="small muted">The hand goes on with the correct play.</p>}
          <button type="button" className="btn btn-primary" onClick={next}>
            {nextLabel}
          </button>
        </section>
      )}
    </main>
  );
}

/** "Whole hand: Flop ✓ · Turn · River": where the student is in a hand and how the earlier decisions went. */
function HandSteps({ decisions, given, step }: { decisions: Drill[]; given: Answer[]; step: number }) {
  return (
    <div className="hand-steps">
      <span className="muted">Whole hand</span>
      <ol aria-label="Decisions in this hand">
        {decisions.map((d, i) => {
          const a = given[i];
          const state = a ? (a.correct ? 'ok' : 'bad') : i === step ? 'current' : 'todo';
          return (
            <li key={d.id} className={`hand-step ${state}`} aria-current={i === step ? 'step' : undefined}>
              {decisionStreet(d)}
              {a && <span aria-label={a.correct ? 'right' : 'wrong'}>{a.correct ? ' ✓' : ' ✗'}</span>}
            </li>
          );
        })}
      </ol>
    </div>
  );
}
