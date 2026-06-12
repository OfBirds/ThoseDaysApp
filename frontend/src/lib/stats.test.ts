import { describe, it, expect } from 'vitest';
import {
  toPeriods, summarize, median, stdDev, histogram, currentCycle,
  periodDaySet, accuracy, recentRows, type CycleRecord,
} from './stats';

const cycle = (start: string, durationDays = 5, extra: Partial<CycleRecord> = {}): CycleRecord => ({
  id: start,
  startDate: start,
  durationDays,
  corrected: false,
  auto: false,
  ...extra,
});

describe('toPeriods', () => {
  it('sorts by start and computes intervals (null for the first)', () => {
    const p = toPeriods([cycle('2026-02-01'), cycle('2026-01-01'), cycle('2026-01-29')]);
    expect(p.map((x) => x.start)).toEqual(['2026-01-01', '2026-01-29', '2026-02-01']);
    expect(p.map((x) => x.interval)).toEqual([null, 28, 3]);
  });

  it('strips any time component from the start date', () => {
    const p = toPeriods([cycle('2026-01-01T22:00:00Z')]);
    expect(p[0].start).toBe('2026-01-01');
  });
});

describe('descriptive stats', () => {
  it('median handles odd and even counts', () => {
    expect(median([3, 1, 2])).toBe(2);
    expect(median([1, 2, 3, 4])).toBe(2.5);
    expect(median([])).toBe(0);
  });

  it('stdDev is 0 for fewer than two values and for constant input', () => {
    expect(stdDev([5])).toBe(0);
    expect(stdDev([4, 4, 4])).toBe(0);
    expect(stdDev([2, 4])).toBeCloseTo(1);
  });
});

describe('summarize', () => {
  it('derives intervals, durations and weighted recent-favored mean', () => {
    const periods = toPeriods([
      cycle('2026-01-01', 4),
      cycle('2026-01-29', 5), // interval 28
      cycle('2026-02-28', 6), // interval 30
    ]);
    const s = summarize(periods, [3, 2, 1], 1, 28);
    expect(s.totalCycles).toBe(3);
    expect(s.intervals).toEqual([28, 30]);
    expect(s.meanInterval).toBe(29);
    expect(s.shortestInterval).toBe(28);
    expect(s.longestInterval).toBe(30);
    expect(s.durations).toEqual([4, 5, 6]);
    // newest-first [30, 28], weights [3,2] -> (90+56)/5 = 29.2 -> 29
    expect(s.weightedInterval).toBe(29);
    expect(s.trackedDays).toBe(58);
  });

  it('is safe with a single cycle (no intervals)', () => {
    const s = summarize(toPeriods([cycle('2026-01-01')]), [3, 2, 1], 1, 28);
    expect(s.intervals).toEqual([]);
    expect(s.meanInterval).toBe(0);
    expect(s.trackedDays).toBe(0);
    expect(s.weightedInterval).toBe(28); // fallback
  });
});

describe('histogram', () => {
  it('buckets values into fixed-width bins', () => {
    const bins = histogram([21, 22, 28, 29, 35], 2);
    const total = bins.reduce((a, b) => a + b.count, 0);
    expect(total).toBe(5);
    expect(bins[0].start).toBe(20);
    expect(bins[bins.length - 1].end).toBeGreaterThan(35);
  });

  it('returns nothing for empty input', () => {
    expect(histogram([], 2)).toEqual([]);
  });
});

describe('currentCycle', () => {
  const periods = toPeriods([cycle('2026-06-01')]);

  it('reports the day within the cycle and progress toward the next prediction', () => {
    const c = currentCycle(periods, '2026-06-15', '2026-06-29', 28)!;
    expect(c.day).toBe(15); // 14 days elapsed + 1
    expect(c.expectedLength).toBe(28);
    expect(c.fraction).toBeCloseTo(15 / 28);
    expect(c.overdue).toBe(false);
  });

  it('flags overdue when past the expected length', () => {
    const c = currentCycle(periods, '2026-07-05', null, 28)!;
    expect(c.overdue).toBe(true);
    expect(c.fraction).toBe(1);
  });

  it('returns null with no periods', () => {
    expect(currentCycle([], '2026-06-15', null, 28)).toBeNull();
  });
});

describe('periodDaySet', () => {
  it('expands periods into the full set of covered days', () => {
    const set = periodDaySet(toPeriods([cycle('2026-01-01', 3)]));
    expect([...set].sort()).toEqual(['2026-01-01', '2026-01-02', '2026-01-03']);
  });
});

describe('accuracy', () => {
  it('measures error only over corrected forecasts; counts accepted ones separately', () => {
    const periods = toPeriods([
      cycle('2026-01-01'),
      cycle('2026-01-31', 5, { auto: true, predictedStart: '2026-01-29' }), // accepted as-is
      cycle('2026-03-02', 5, { auto: false, corrected: true, predictedStart: '2026-02-26' }), // moved +4
    ]);
    const a = accuracy(periods);
    expect(a.acceptedCount).toBe(1);
    expect(a.count).toBe(1);
    expect(a.errors[0].error).toBe(4);
    expect(a.mae).toBe(4);
    expect(a.bias).toBe(4);
  });

  it('is empty when nothing carried a prediction', () => {
    const a = accuracy(toPeriods([cycle('2026-01-01')]));
    expect(a).toMatchObject({ count: 0, acceptedCount: 0, mae: 0, bias: 0, errors: [] });
  });
});

describe('recentRows', () => {
  it('lists newest first with deviation from the mean interval', () => {
    const periods = toPeriods([cycle('2026-01-01'), cycle('2026-01-29'), cycle('2026-02-28')]);
    const rows = recentRows(periods);
    expect(rows[0].start).toBe('2026-02-28');
    expect(rows[0].deviation).toBeCloseTo(30 - 29); // mean interval 29
    expect(rows[2].deviation).toBeNull(); // first cycle has no interval
  });
});
