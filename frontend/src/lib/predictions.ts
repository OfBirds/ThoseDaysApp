// Date prediction and calendar helpers extracted from Calendar.tsx.
// Pure functions (string-based dates to avoid timezone drift), no React deps.

export const isoDate = (date: Date) => {
  const yyyy = date.getFullYear();
  const mm = String(date.getMonth() + 1).padStart(2, '0');
  const dd = String(date.getDate()).padStart(2, '0');
  return `${yyyy}-${mm}-${dd}`;
};

export const addDaysIso = (iso: string, n: number) => {
  const [y, m, d] = iso.split('-').map(Number);
  return new Date(Date.UTC(y, m - 1, d + n)).toISOString().slice(0, 10);
};

export const daysBetween = (a: string, b: string) =>
  Math.round((Date.parse(b + 'T00:00:00Z') - Date.parse(a + 'T00:00:00Z')) / 86400000);

// Expand a start date + duration into a set of ISO day strings.
export function spanDays(startIso: string, duration: number): string[] {
  const out: string[] = [];
  for (let i = 0; i < Math.max(1, duration); i++) out.push(addDaysIso(startIso, i));
  return out;
}

// Collapse painted days into consecutive-day periods. Mirrors the backend.
export function groupPeriods(days: string[]): { start: string; length: number }[] {
  const sorted = Array.from(new Set(days)).sort();
  const periods: { start: string; length: number }[] = [];
  let runStart: string | null = null;
  let prev: string | null = null;
  let len = 0;
  for (const d of sorted) {
    if (runStart === null) { runStart = d; len = 1; }
    else if (d === addDaysIso(prev!, 1)) { len++; }
    else { periods.push({ start: runStart, length: len }); runStart = d; len = 1; }
    prev = d;
  }
  if (runStart !== null) periods.push({ start: runStart, length: len });
  return periods;
}

// Recent-favored weighted mean, rounded. Values newest → oldest. Mirrors the backend.
export function weightedAvg(
  valuesNewestFirst: number[],
  weights: number[],
  tailWeight: number,
  fallback: number
) {
  if (valuesNewestFirst.length === 0) return fallback;
  let ws = 0, wt = 0;
  for (let i = 0; i < valuesNewestFirst.length; i++) {
    const w = i < weights.length ? weights[i] : tailWeight;
    ws += valuesNewestFirst[i] * w;
    wt += w;
  }
  return wt > 0 ? Math.round(ws / wt) : fallback;
}

export interface RecalcConfig {
  weights: number[];
  tailWeight: number;
  defaultCycleLength: number;
  defaultPeriodDuration: number;
  cycleLengthMin: number;
  cycleLengthMax: number;
  periodDurationMin: number;
  periodDurationMax: number;
}

export function computeAverages(days: string[], config: RecalcConfig) {
  const periods = groupPeriods(days);
  if (periods.length === 0) {
    return {
      cycleLength: config.defaultCycleLength,
      periodDuration: config.defaultPeriodDuration,
    };
  }
  const durations: number[] = [];
  for (let i = periods.length - 1; i >= 0; i--) durations.push(periods[i].length);
  const intervals: number[] = [];
  for (let i = periods.length - 1; i >= 1; i--)
    intervals.push(daysBetween(periods[i - 1].start, periods[i].start));
  return {
    cycleLength: weightedAvg(intervals, config.weights, config.tailWeight, config.defaultCycleLength),
    periodDuration: weightedAvg(durations, config.weights, config.tailWeight, config.defaultPeriodDuration),
  };
}

// --- Calendar UI helpers (pure) ---

/** Find the next upcoming prediction: the soonest forecast start >= today. */
export function findNextPrediction(
  predictions: { predictedStart: string; predictedDuration: number }[],
  todayIso: string
): { startIso: string; daysUntil: number } | null {
  const upcoming = predictions
    .map((p) => p.predictedStart.slice(0, 10))
    .filter((s) => s >= todayIso)
    .sort();
  const next = upcoming[0] ?? null;
  if (!next) return null;
  return { startIso: next, daysUntil: daysBetween(todayIso, next) };
}

/** Which prediction tier a given ISO day belongs to. -1 = none, 0 = next period, 1+ = future. */
export function predictionTier(
  iso: string,
  predictions: { predictedStart: string; predictedDuration: number }[]
): number {
  for (let i = 0; i < predictions.length; i++) {
    if (spanDays(predictions[i].predictedStart.slice(0, 10), predictions[i].predictedDuration).includes(iso)) {
      return i;
    }
  }
  return -1;
}

/** Check whether cycle/period values are outside the configured normal range. */
export function isOutOfRange(
  cycleLength: number,
  periodDuration: number,
  config: RecalcConfig
): boolean {
  return (
    cycleLength < config.cycleLengthMin ||
    cycleLength > config.cycleLengthMax ||
    periodDuration < config.periodDurationMin ||
    periodDuration > config.periodDurationMax
  );
}

/** Find days that are more than `maxFutureDays` ahead of today. */
export function findFutureDays(days: string[], todayIso: string, maxFutureDays: number): string[] {
  const cutoff = addDaysIso(todayIso, maxFutureDays);
  return days.filter((d) => d > cutoff).sort();
}
