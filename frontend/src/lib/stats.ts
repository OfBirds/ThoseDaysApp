// Read-only statistics derived from the user's stored cycles.
// Pure, string-date based (no timezone drift), no React deps. Mirrors the
// domain math in predictions.ts but aimed at visualization rather than forecasting.

import { daysBetween, addDaysIso, spanDays, weightedAvg } from './predictions';

export interface CycleRecord {
  id: string;
  startDate: string; // ISO, may carry a time component
  durationDays: number;
  corrected: boolean;
  auto: boolean;
  predictedStart?: string | null; // for auto cycles: the date originally forecast
}

/** One stored cycle, normalized: an ISO start date plus the gap to the previous start. */
export interface Period {
  id: string;
  start: string; // ISO yyyy-MM-dd
  length: number; // period duration in days
  interval: number | null; // days since the previous start; null for the first
  corrected: boolean;
  auto: boolean;
  predictedStart: string | null;
}

const day = (iso: string) => iso.slice(0, 10);

/** Normalize raw cycles into start-sorted periods with intervals attached. */
export function toPeriods(cycles: CycleRecord[]): Period[] {
  const sorted = [...cycles].sort((a, b) => day(a.startDate).localeCompare(day(b.startDate)));
  return sorted.map((c, i) => ({
    id: c.id,
    start: day(c.startDate),
    length: c.durationDays,
    interval: i === 0 ? null : daysBetween(day(sorted[i - 1].startDate), day(c.startDate)),
    corrected: c.corrected,
    auto: c.auto,
    predictedStart: c.predictedStart ? day(c.predictedStart) : null,
  }));
}

// --- Descriptive helpers ---------------------------------------------------

export const mean = (xs: number[]) => (xs.length ? xs.reduce((a, b) => a + b, 0) / xs.length : 0);

export function median(xs: number[]): number {
  if (xs.length === 0) return 0;
  const s = [...xs].sort((a, b) => a - b);
  const mid = Math.floor(s.length / 2);
  return s.length % 2 ? s[mid] : (s[mid - 1] + s[mid]) / 2;
}

/** Population standard deviation — our "regularity" measure for intervals. */
export function stdDev(xs: number[]): number {
  if (xs.length < 2) return 0;
  const m = mean(xs);
  return Math.sqrt(mean(xs.map((x) => (x - m) ** 2)));
}

export const minOf = (xs: number[]) => (xs.length ? Math.min(...xs) : 0);
export const maxOf = (xs: number[]) => (xs.length ? Math.max(...xs) : 0);

// --- Aggregate summary -----------------------------------------------------

export interface Summary {
  totalCycles: number;
  trackedDays: number; // span from first to last start
  intervals: number[];
  durations: number[];
  meanInterval: number;
  medianInterval: number;
  weightedInterval: number; // recent-favored, matches the prediction basis
  intervalStdDev: number;
  shortestInterval: number;
  longestInterval: number;
  meanDuration: number;
  medianDuration: number;
  shortestDuration: number;
  longestDuration: number;
}

export function summarize(periods: Period[], weights: number[], tailWeight: number, fallbackInterval: number): Summary {
  const intervals = periods.map((p) => p.interval).filter((v): v is number => v !== null);
  const durations = periods.map((p) => p.length);
  // Weighted mean wants newest-first to favor recent cycles.
  const intervalsNewestFirst = [...intervals].reverse();
  return {
    totalCycles: periods.length,
    trackedDays: periods.length > 1 ? daysBetween(periods[0].start, periods[periods.length - 1].start) : 0,
    intervals,
    durations,
    meanInterval: mean(intervals),
    medianInterval: median(intervals),
    weightedInterval: weightedAvg(intervalsNewestFirst, weights, tailWeight, fallbackInterval),
    intervalStdDev: stdDev(intervals),
    shortestInterval: minOf(intervals),
    longestInterval: maxOf(intervals),
    meanDuration: mean(durations),
    medianDuration: median(durations),
    shortestDuration: minOf(durations),
    longestDuration: maxOf(durations),
  };
}

// --- Distribution ----------------------------------------------------------

export interface Bin {
  start: number; // inclusive lower edge
  end: number; // exclusive upper edge
  count: number;
  label: string;
}

/** Bucket numeric values into fixed-width bins spanning their range (min one bin). */
export function histogram(values: number[], binWidth: number): Bin[] {
  if (values.length === 0 || binWidth <= 0) return [];
  const lo = Math.floor(minOf(values) / binWidth) * binWidth;
  const hi = Math.floor(maxOf(values) / binWidth) * binWidth + binWidth;
  const bins: Bin[] = [];
  for (let s = lo; s < hi; s += binWidth) {
    const end = s + binWidth;
    const count = values.filter((v) => v >= s && v < end).length;
    bins.push({
      start: s,
      end,
      count,
      label: binWidth === 1 ? `${s}` : `${s}–${end - 1}`,
    });
  }
  return bins;
}

// --- Current cycle progress ------------------------------------------------

export interface CurrentCycle {
  day: number; // 1-based day within the current (latest) cycle
  expectedLength: number;
  fraction: number; // 0..1 progress toward the next predicted start
  overdue: boolean;
}

/**
 * Where the user is within the cycle in progress: day N of an expected length.
 * Expected length is the gap to the next predicted start when known, else the
 * recent-favored interval.
 */
export function currentCycle(
  periods: Period[],
  todayIso: string,
  nextPredictedStart: string | null,
  expectedFallback: number
): CurrentCycle | null {
  if (periods.length === 0) return null;
  const lastStart = periods[periods.length - 1].start;
  const day = daysBetween(lastStart, todayIso) + 1;
  const expectedLength =
    nextPredictedStart ? daysBetween(lastStart, nextPredictedStart) : expectedFallback;
  const safeLen = expectedLength > 0 ? expectedLength : expectedFallback;
  return {
    day,
    expectedLength: safeLen,
    fraction: Math.max(0, Math.min(1, day / safeLen)),
    overdue: day > safeLen,
  };
}

// --- Calendar heatmap ------------------------------------------------------

/** Set of every ISO day that falls inside a logged period — for a year heatmap. */
export function periodDaySet(periods: Period[]): Set<string> {
  const set = new Set<string>();
  for (const p of periods) for (const d of spanDays(p.start, p.length)) set.add(d);
  return set;
}

// --- Prediction accuracy ---------------------------------------------------

export interface AccuracyError {
  start: string; // actual start
  predicted: string;
  error: number; // signed: actual − predicted (positive = late)
}

export interface Accuracy {
  count: number; // corrected cycles that carried a prediction
  acceptedCount: number; // auto cycles accepted as-predicted (error 0 by construction)
  mae: number; // mean absolute error in days
  bias: number; // mean signed error (positive = predictions ran early)
  errors: AccuracyError[];
}

/**
 * Compares forecast start dates against what actually happened. Only cycles the
 * user *corrected* count toward MAE — an uncorrected auto cycle equals its own
 * prediction by construction, so including it would fake a perfect score.
 */
export function accuracy(periods: Period[]): Accuracy {
  const errors: AccuracyError[] = [];
  let acceptedCount = 0;
  for (const p of periods) {
    if (!p.predictedStart) continue;
    if (p.corrected) {
      errors.push({ start: p.start, predicted: p.predictedStart, error: daysBetween(p.predictedStart, p.start) });
    } else if (p.auto) {
      acceptedCount++;
    }
  }
  const abs = errors.map((e) => Math.abs(e.error));
  const signed = errors.map((e) => e.error);
  return {
    count: errors.length,
    acceptedCount,
    mae: mean(abs),
    bias: mean(signed),
    errors,
  };
}

/** Convenience: the recent-cycles list, newest first, with deviation from the mean. */
export interface CycleRow extends Period {
  deviation: number | null; // interval − mean interval
}

export function recentRows(periods: Period[]): CycleRow[] {
  const intervals = periods.map((p) => p.interval).filter((v): v is number => v !== null);
  const m = mean(intervals);
  return [...periods]
    .reverse()
    .map((p) => ({ ...p, deviation: p.interval === null ? null : p.interval - m }));
}

export { addDaysIso };
