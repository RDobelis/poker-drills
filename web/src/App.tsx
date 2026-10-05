import { useEffect, useState } from 'react';
import type { DrillFile } from './types';
import { Review } from './pages/Review';
import { Trainer } from './pages/Trainer';

const SCHEMA_VERSION = 1;

function isReviewPath(): boolean {
  const base = import.meta.env.BASE_URL.replace(/\/$/, '');
  return window.location.pathname.replace(/\/+$/, '') === `${base}/review`;
}

export default function App() {
  const [data, setData] = useState<DrillFile | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    fetch(`${import.meta.env.BASE_URL}drills.json`)
      .then((r) => {
        if (!r.ok) throw new Error(`drills.json: HTTP ${r.status}`);
        return r.json() as Promise<DrillFile>;
      })
      .then((file) => {
        if (file.schemaVersion !== SCHEMA_VERSION) {
          throw new Error(`drills.json has schema ${file.schemaVersion}, expected ${SCHEMA_VERSION}. Regenerate it.`);
        }
        if (!cancelled) setData(file);
      })
      .catch((e: unknown) => {
        if (!cancelled) setError(e instanceof Error ? e.message : String(e));
      });
    return () => {
      cancelled = true;
    };
  }, []);

  if (error) {
    return (
      <main className="app">
        <h1>Could not load drills</h1>
        <p>{error}</p>
        <p className="muted">Run the generator (see README) to create web/public/drills.json.</p>
      </main>
    );
  }
  if (!data) {
    return (
      <main className="app">
        <p className="muted center">Loading drills…</p>
      </main>
    );
  }
  return isReviewPath() ? <Review data={data} /> : <Trainer data={data} />;
}
