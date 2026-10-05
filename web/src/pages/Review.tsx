import { useCallback, useEffect, useMemo, useState } from 'react';
import type { Drill, DrillFile } from '../types';
import { DrillView } from '../components/DrillView';
import { lineLabel } from '../components/labels';
import { todayKey } from '../logic/dates';
import { buildFlagExport, loadFlags, saveFlags, type Flags } from '../logic/flags';
import { shuffle } from '../logic/session';

const SAMPLE_SIZE = 10;

/** Coach page (/review): pick a rule, inspect random drills in full, flag them with notes, export. */
export function Review({ data }: { data: DrillFile }) {
  const drillById = useMemo(() => new Map(data.drills.map((d) => [d.id, d])), [data]);
  const typeName = useCallback((id: string) => data.types.find((t) => t.id === id)?.name ?? id, [data]);
  const countByRule = useMemo(() => {
    const m = new Map<string, number>();
    for (const d of data.drills) m.set(d.ruleId, (m.get(d.ruleId) ?? 0) + 1);
    return m;
  }, [data]);

  const [ruleId, setRuleId] = useState(data.rules[0]?.id ?? '');
  const [round, setRound] = useState(0);
  const sample = useMemo(
    () => shuffle(data.drills.filter((d) => d.ruleId === ruleId), Math.random).slice(0, SAMPLE_SIZE),
    // `round` is a dependency on purpose: bumping it draws a new sample.
    [data, ruleId, round],
  );

  const [flags, setFlags] = useState<Flags>(loadFlags);
  const [exported, setExported] = useState<string | null>(null);
  const [copied, setCopied] = useState(false);
  useEffect(() => {
    saveFlags(flags);
  }, [flags]);

  const rule = data.rules.find((r) => r.id === ruleId);
  const flagList = Object.values(flags).sort((a, b) => a.flaggedAt.localeCompare(b.flaggedAt));

  const toggleFlag = (d: Drill) => {
    const flaggedAt = new Date().toISOString();
    setFlags((f) => {
      const next = { ...f };
      if (next[d.id]) delete next[d.id];
      else next[d.id] = { drillId: d.id, ruleId: d.ruleId, note: '', flaggedAt };
      return next;
    });
  };
  const setNote = (id: string, note: string) => setFlags((f) => (f[id] ? { ...f, [id]: { ...f[id], note } } : f));
  const removeFlag = (id: string) =>
    setFlags((f) => {
      const next = { ...f };
      delete next[id];
      return next;
    });

  const exportFlags = () => {
    const json = JSON.stringify(buildFlagExport(flags, drillById, data.seed), null, 2);
    setExported(json);
    setCopied(false);
    const url = URL.createObjectURL(new Blob([json], { type: 'application/json' }));
    const a = document.createElement('a');
    a.href = url;
    a.download = `drill-flags-${todayKey()}.json`;
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  };

  const copyExport = async () => {
    if (!exported) return;
    try {
      await navigator.clipboard.writeText(exported);
      setCopied(true);
    } catch {
      setCopied(false); // clipboard blocked: the textarea is still there to copy by hand
    }
  };

  return (
    <main className="app review">
      <header className="review-header">
        <a href={import.meta.env.BASE_URL} className="small">
          ← Trainer
        </a>
        <h1>Coach review</h1>
        <p className="small muted">Pick a rule, check 10 random drills in full, flag anything wrong, export the flags.</p>
      </header>

      <label className="field">
        <span>Rule</span>
        <select
          value={ruleId}
          onChange={(e) => {
            setRuleId(e.target.value);
            setRound(0);
          }}
        >
          {data.rules.map((r) => (
            <option key={r.id} value={r.id}>
              {r.id} ({countByRule.get(r.id) ?? 0})
            </option>
          ))}
        </select>
      </label>

      {rule && (
        <section className="card rule-info">
          <div className="drill-head">
            <span className={`badge type-${rule.villainType}`}>{typeName(rule.villainType)}</span>
            <span className="line-label">{lineLabel(rule.line)}</span>
            {rule.placeholder && <span className="tag">placeholder</span>}
          </div>
          <p>
            <b>When:</b> {rule.conditions}
          </p>
          <p>
            <b>Correct:</b> {rule.correct}
          </p>
          <p className="reason">{rule.reason}</p>
          <p className="small muted">{countByRule.get(rule.id) ?? 0} drills generated</p>
        </section>
      )}

      <button type="button" className="btn" onClick={() => setRound((r) => r + 1)}>
        Show 10 other drills
      </button>

      {sample.map((d, i) => {
        const flag = flags[d.id];
        return (
          <section key={d.id} className={`card review-item${flag ? ' flagged' : ''}`}>
            <div className="review-item-head">
              <span className="small muted">
                #{i + 1} of {sample.length}
              </span>
              <button
                type="button"
                className={`btn btn-flag${flag ? ' on' : ''}`}
                aria-pressed={!!flag}
                onClick={() => toggleFlag(d)}
              >
                {flag ? '⚑ Flagged' : '⚐ Flag'}
              </button>
            </div>
            <DrillView drill={d} typeName={typeName} review />
            <p className="reason">{d.reason}</p>
            {flag && (
              <label className="field">
                <span>Note</span>
                <textarea
                  rows={3}
                  value={flag.note}
                  placeholder="What is wrong with this drill?"
                  onChange={(e) => setNote(d.id, e.target.value)}
                />
              </label>
            )}
          </section>
        );
      })}

      <section className="card export">
        <h2>Flags ({flagList.length})</h2>
        {flagList.length > 0 && (
          <ul className="flag-list">
            {flagList.map((f) => (
              <li key={f.drillId}>
                <code>{f.drillId}</code>
                {f.note && <span> {f.note}</span>}
                <button type="button" className="link" onClick={() => removeFlag(f.drillId)}>
                  remove
                </button>
              </li>
            ))}
          </ul>
        )}
        <button type="button" className="btn btn-primary" disabled={flagList.length === 0} onClick={exportFlags}>
          Export flags as JSON
        </button>
        {exported && (
          <>
            <textarea className="export-json" readOnly rows={8} value={exported} onFocus={(e) => e.currentTarget.select()} />
            <button type="button" className="btn" onClick={copyExport}>
              {copied ? 'Copied' : 'Copy JSON'}
            </button>
          </>
        )}
      </section>
    </main>
  );
}
