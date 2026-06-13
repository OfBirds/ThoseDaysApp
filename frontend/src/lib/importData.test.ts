import { describe, it, expect } from 'vitest';
import { parseImport, importWindow, reviewImport, type ImportDoc } from './importData';

const validDoc = {
  schemaVersion: 1,
  cycles: [
    { startDate: '2026-02-10', durationDays: 4 },
    { startDate: '2026-03-10', durationDays: 6 },
  ],
};

describe('parseImport', () => {
  it('accepts a valid v1 document', () => {
    const result = parseImport(JSON.stringify(validDoc));
    expect('doc' in result).toBe(true);
  });

  it('rejects non-JSON', () => {
    const result = parseImport('not json');
    expect('error' in result && result.error).toMatch(/JSON/);
  });

  it('rejects an unknown schema version', () => {
    const result = parseImport(JSON.stringify({ ...validDoc, schemaVersion: 2 }));
    expect('error' in result && result.error).toMatch(/version 2/);
  });

  it('rejects an empty cycle list', () => {
    const result = parseImport(JSON.stringify({ schemaVersion: 1, cycles: [] }));
    expect('error' in result && result.error).toMatch(/no cycles/);
  });

  it('rejects a bad duration', () => {
    const result = parseImport(JSON.stringify({ schemaVersion: 1, cycles: [{ startDate: '2026-01-01', durationDays: 99 }] }));
    expect('error' in result && result.error).toMatch(/duration/);
  });

  it('rejects a bad start date', () => {
    const result = parseImport(JSON.stringify({ schemaVersion: 1, cycles: [{ startDate: 'nope', durationDays: 5 }] }));
    expect('error' in result && result.error).toMatch(/start date/);
  });
});

describe('importWindow', () => {
  it('spans first start to last bleeding day', () => {
    const w = importWindow(validDoc.cycles);
    expect(w.start).toBe('2026-02-10');
    expect(w.end).toBe('2026-03-15'); // Mar 10 + (6-1)
  });
});

describe('reviewImport', () => {
  const doc = validDoc as ImportDoc;

  it('counts only in-window cycles as replaced and computes both seams', () => {
    const existing = [
      { startDate: '2026-01-01', durationDays: 5 }, // before window → leading seam
      { startDate: '2026-02-15', durationDays: 5 }, // inside [Feb10, Mar15] → replaced
      { startDate: '2026-05-01', durationDays: 5 }, // after window → trailing seam
    ];
    const r = reviewImport(doc, existing);

    expect(r.rangeStart).toBe('2026-02-10');
    expect(r.rangeEnd).toBe('2026-03-15');
    expect(r.replacedCount).toBe(1);
    // leading: Jan 1 + (5-1) = Jan 5 → Feb 10 = 36 days
    expect(r.leadingGap).toBe(36);
    // trailing: Mar 15 → May 1 = 47 days
    expect(r.trailingGap).toBe(47);
  });

  it('reports null seams at the edges of history', () => {
    const r = reviewImport(doc, []);
    expect(r.leadingGap).toBeNull();
    expect(r.trailingGap).toBeNull();
    expect(r.replacedCount).toBe(0);
  });
});
