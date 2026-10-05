/** Local calendar date as YYYY-MM-DD. Sorts and compares correctly as a string. */
export type DateKey = string;

const pad = (n: number) => String(n).padStart(2, '0');

export function toDateKey(d: Date): DateKey {
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

export function todayKey(now: Date = new Date()): DateKey {
  return toDateKey(now);
}

function parse(key: DateKey): [number, number, number] {
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(key);
  if (!m) throw new Error(`Bad date key: ${key}`);
  return [Number(m[1]), Number(m[2]), Number(m[3])];
}

/** Calendar-day arithmetic. Uses local noon so DST changes can never shift the date. */
export function addDays(key: DateKey, days: number): DateKey {
  const [y, m, d] = parse(key);
  return toDateKey(new Date(y, m - 1, d + days, 12));
}
