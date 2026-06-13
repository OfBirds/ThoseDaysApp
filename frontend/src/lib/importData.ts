// Client-side parse/validate of an export file and the review summary for a
// reviewed-patch import (see docs/data-export-import.md). Pure, string-date based.

import { addDaysIso, daysBetween } from './predictions';

export interface ImportCycle {
  startDate: string; // ISO yyyy-MM-dd (may carry a time component)
  durationDays: number;
  corrected?: boolean;
  auto?: boolean;
  predictedStart?: string | null;
  createdAt?: string;
}

export interface ImportDoc {
  schemaVersion: number;
  kind?: string;
  scope?: string;
  range?: { start: string; end: string };
  cycleCount?: number;
  cycles: ImportCycle[];
}

/** A minimal view of an existing cycle, enough to compute the patch window/seams. */
export interface ExistingCycle {
  startDate: string;
  durationDays: number;
}

export interface ImportReview {
  rangeStart: string; // P0 — first imported start
  rangeEnd: string; // P1 — last imported bleeding day
  importedCount: number;
  replacedCount: number; // existing cycles whose start falls in [P0, P1]
  leadingGap: number | null; // days from the prior period's last day to P0; null if none before
  trailingGap: number | null; // days from P1 to the next period's start; null if none after
}

const day = (iso: string) => iso.slice(0, 10);
const SCHEMA_VERSION = 1;
const MAX_CYCLES = 5000;

/** Parse + validate an export file's text. Returns the doc or a human message. */
export function parseImport(text: string): { doc: ImportDoc } | { error: string } {
  let raw: unknown;
  try {
    raw = JSON.parse(text);
  } catch {
    return { error: "That doesn't look like a ThoseDays file — it isn't valid JSON." };
  }
  const obj = raw as Partial<ImportDoc>;
  if (typeof obj.schemaVersion !== 'number')
    return { error: "This file is missing its version — it may not be a ThoseDays export." };
  if (obj.schemaVersion !== SCHEMA_VERSION)
    return { error: `This file is version ${obj.schemaVersion}; this app reads version ${SCHEMA_VERSION}.` };
  if (!Array.isArray(obj.cycles) || obj.cycles.length === 0)
    return { error: 'This file has no cycles to import.' };
  if (obj.cycles.length > MAX_CYCLES)
    return { error: `This file has too many cycles (${obj.cycles.length}).` };

  for (let i = 0; i < obj.cycles.length; i++) {
    const c = obj.cycles[i];
    if (!c || typeof c.startDate !== 'string' || Number.isNaN(Date.parse(c.startDate)))
      return { error: `Cycle ${i + 1} has an invalid start date.` };
    if (typeof c.durationDays !== 'number' || c.durationDays < 1 || c.durationDays > 30)
      return { error: `Cycle ${i + 1} has an invalid duration.` };
  }
  return { doc: obj as ImportDoc };
}

/** Window covered by the imported cycles: [first start, last bleeding day]. */
export function importWindow(cycles: ImportCycle[]): { start: string; end: string } {
  const sorted = [...cycles].sort((a, b) => day(a.startDate).localeCompare(day(b.startDate)));
  const first = sorted[0];
  const last = sorted[sorted.length - 1];
  return {
    start: day(first.startDate),
    end: addDaysIso(day(last.startDate), Math.max(1, last.durationDays) - 1),
  };
}

/** Compute the review summary: replaced count and the two boundary seams. */
export function reviewImport(doc: ImportDoc, existing: ExistingCycle[]): ImportReview {
  const { start: p0, end: p1 } = importWindow(doc.cycles);

  const sorted = [...existing].sort((a, b) => day(a.startDate).localeCompare(day(b.startDate)));
  let replaced = 0;
  let prevLastDay: string | null = null; // last bleeding day of the latest period before the window
  let nextStart: string | null = null; // start of the earliest period after the window

  for (const c of sorted) {
    const s = day(c.startDate);
    const lastDay = addDaysIso(s, Math.max(1, c.durationDays) - 1);
    if (s >= p0 && s <= p1) {
      replaced++;
    } else if (s < p0) {
      prevLastDay = lastDay; // keep advancing; ends on the closest one before the window
    } else if (s > p1 && nextStart === null) {
      nextStart = s; // first one after the window
    }
  }

  return {
    rangeStart: p0,
    rangeEnd: p1,
    importedCount: doc.cycles.length,
    replacedCount: replaced,
    leadingGap: prevLastDay === null ? null : daysBetween(prevLastDay, p0),
    trailingGap: nextStart === null ? null : daysBetween(p1, nextStart),
  };
}
