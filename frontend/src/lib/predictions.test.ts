import { describe, it, expect } from 'vitest';
import {
  isoDate, addDaysIso, daysBetween, spanDays,
  groupPeriods, weightedAvg, computeAverages,
  findNextPrediction, predictionTier,
  isOutOfRange, findFutureDays,
  type RecalcConfig,
} from '../lib/predictions';

const TEST_CONFIG: RecalcConfig = {
  weights: [3, 2, 1],
  tailWeight: 1,
  defaultCycleLength: 28,
  defaultPeriodDuration: 5,
  cycleLengthMin: 21,
  cycleLengthMax: 35,
  periodDurationMin: 2,
  periodDurationMax: 10,
};

describe('predictions', () => {
  describe('isoDate', () => {
    it('matches the system date', () => {
      const today = new Date();
      const iso = isoDate(today);
      expect(iso).toBe(
        `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}-${String(today.getDate()).padStart(2, '0')}`
      );
    });
  });

  describe('daysBetween', () => {
    it('computes correct delta', () => {
      expect(daysBetween('2025-01-01', '2025-01-05')).toBe(4);
      expect(daysBetween('2025-01-01', '2025-01-01')).toBe(0);
    });
  });

  describe('addDaysIso', () => {
    it('adds days correctly', () => {
      expect(addDaysIso('2025-01-01', 1)).toBe('2025-01-02');
      expect(addDaysIso('2025-12-31', 1)).toBe('2026-01-01');
    });
  });

  describe('spanDays', () => {
    it('expands start + duration', () => {
      const days = spanDays('2025-06-01', 3);
      expect(days).toEqual(['2025-06-01', '2025-06-02', '2025-06-03']);
    });
  });

  describe('groupPeriods', () => {
    it('consecutive run → one period', () => {
      const result = groupPeriods(['2025-06-01', '2025-06-02', '2025-06-03']);
      expect(result).toHaveLength(1);
      expect(result[0]).toEqual({ start: '2025-06-01', length: 3 });
    });

    it('gap splits into two', () => {
      const result = groupPeriods(['2025-06-01', '2025-06-02', '2025-06-05']);
      expect(result).toHaveLength(2);
      expect(result[0].length).toBe(2);
      expect(result[1].length).toBe(1);
    });

    it('single isolated day → length 1', () => {
      const result = groupPeriods(['2025-07-04']);
      expect(result).toHaveLength(1);
      expect(result[0].length).toBe(1);
    });
  });

  describe('weightedAvg', () => {
    it('empty values returns fallback', () => {
      expect(weightedAvg([], [3, 2, 1], 1, 99)).toBe(99);
    });

    it('recent-favored weights', () => {
      // [10, 20] with [3,2] → (10*3 + 20*2)/5 = 70/5 = 14
      expect(weightedAvg([10, 20], [3, 2], 1, 0)).toBe(14);
    });

    it('tailWeight applied beyond weights array', () => {
      // [10, 20, 30, 40] with [3,2], tailWeight=1 → (10*3+20*2+30*1+40*1)/7 = 140/7 = 20
      expect(weightedAvg([10, 20, 30, 40], [3, 2], 1, 0)).toBe(20);
    });
  });

  describe('computeAverages', () => {
    it('empty days → defaults', () => {
      const result = computeAverages([], TEST_CONFIG);
      expect(result.cycleLength).toBe(28);
      expect(result.periodDuration).toBe(5);
    });

    it('single period → interval falls back', () => {
      // One period of 3 days. No interval → cycleLength = default 28.
      const result = computeAverages(['2025-01-01', '2025-01-02', '2025-01-03'], TEST_CONFIG);
      expect(result.cycleLength).toBe(28);
      expect(result.periodDuration).toBe(3);
    });
  });

  describe('findNextPrediction', () => {
    it('finds the soonest forecast >= today', () => {
      const predictions = [
        { predictedStart: '2025-05-01T00:00:00Z', predictedDuration: 5 },
        { predictedStart: '2025-06-01T00:00:00Z', predictedDuration: 5 },
        { predictedStart: '2025-07-01T00:00:00Z', predictedDuration: 5 },
      ];

      // Today is June 1 → next should be June 1 (0 days).
      const result = findNextPrediction(predictions, '2025-06-01');
      expect(result).not.toBeNull();
      expect(result!.startIso).toBe('2025-06-01');
      expect(result!.daysUntil).toBe(0);
    });

    it('shows correct countdown delta', () => {
      const predictions = [
        { predictedStart: '2025-06-05T00:00:00Z', predictedDuration: 5 },
      ];

      const result = findNextPrediction(predictions, '2025-06-01');
      expect(result).not.toBeNull();
      expect(result!.daysUntil).toBe(4);
    });

    it('returns null when no upcoming predictions', () => {
      const predictions = [
        { predictedStart: '2025-01-01T00:00:00Z', predictedDuration: 5 },
      ];

      const result = findNextPrediction(predictions, '2025-06-01');
      expect(result).toBeNull();
    });
  });

  describe('predictionTier', () => {
    it('next prediction classified as tier 0', () => {
      const predictions = [
        { predictedStart: '2025-06-01T00:00:00Z', predictedDuration: 3 },
        { predictedStart: '2025-07-01T00:00:00Z', predictedDuration: 3 },
      ];

      // June 1 is in the first prediction → tier 0
      expect(predictionTier('2025-06-01', predictions)).toBe(0);
      // July 2 is in the second → tier 1
      expect(predictionTier('2025-07-02', predictions)).toBe(1);
    });

    it('returns -1 for non-prediction days', () => {
      const predictions = [
        { predictedStart: '2025-06-01T00:00:00Z', predictedDuration: 3 },
      ];

      expect(predictionTier('2025-05-15', predictions)).toBe(-1);
    });
  });

  describe('isOutOfRange', () => {
    it('detects out-of-range values', () => {
      const cfg: RecalcConfig = { ...TEST_CONFIG, cycleLengthMin: 21, cycleLengthMax: 35, periodDurationMin: 2, periodDurationMax: 10 };

      expect(isOutOfRange(20, 5, cfg)).toBe(true);  // cycle too low
      expect(isOutOfRange(36, 5, cfg)).toBe(true);  // cycle too high
      expect(isOutOfRange(28, 1, cfg)).toBe(true);  // period too low
      expect(isOutOfRange(28, 11, cfg)).toBe(true); // period too high
      expect(isOutOfRange(28, 5, cfg)).toBe(false); // in range
    });
  });

  describe('findFutureDays', () => {
    it('finds days beyond the cutoff', () => {
      const days = ['2025-06-01', '2025-06-02', '2025-06-10', '2025-06-15'];
      // Today June 1, max 3 days ahead → cutoff June 4
      const result = findFutureDays(days, '2025-06-01', 3);
      expect(result).toEqual(['2025-06-10', '2025-06-15']);
    });

    it('returns empty when all days within range', () => {
      const days = ['2025-06-01', '2025-06-02'];
      const result = findFutureDays(days, '2025-06-01', 3);
      expect(result).toEqual([]);
    });
  });
});
