// Centralized localStorage access. The mechanism lives here so it can be swapped
// (e.g. IndexedDB for the PWA offline cache) without touching callers.

const PREFIX = 'tda:';

export interface Draft {
  days: string[]; // ISO yyyy-MM-dd local dates the user has painted
  cycleLength: number;
  periodDuration: number;
  dirty: boolean;
}

function read<T>(key: string): T | null {
  try {
    const raw = localStorage.getItem(PREFIX + key);
    return raw ? (JSON.parse(raw) as T) : null;
  } catch {
    return null;
  }
}

function write<T>(key: string, value: T): void {
  try {
    localStorage.setItem(PREFIX + key, JSON.stringify(value));
  } catch {
    /* ignore quota / private-mode errors */
  }
}

function remove(key: string): void {
  try {
    localStorage.removeItem(PREFIX + key);
  } catch {
    /* ignore */
  }
}

// --- Draft (per user) ---
export const getDraft = (userId: string) => read<Draft>(`draft:${userId}`);
export const saveDraft = (userId: string, draft: Draft) => write(`draft:${userId}`, draft);
export const clearDraft = (userId: string) => remove(`draft:${userId}`);

// --- Pending import (per user) ---
// A reviewed-but-not-saved import, staged for the user to look over on the
// calendar before committing it with "Save this history permanently".
export interface PendingImport {
  cycles: { startDate: string; durationDays: number; corrected?: boolean; auto?: boolean; predictedStart?: string | null }[];
  range: { start: string; end: string };
  schemaVersion: number;
}
export const getPendingImport = (userId: string) => read<PendingImport>(`pendingImport:${userId}`);
export const savePendingImport = (userId: string, p: PendingImport) => write(`pendingImport:${userId}`, p);
export const clearPendingImport = (userId: string) => remove(`pendingImport:${userId}`);

// --- Theme ---
export type Theme = 'light' | 'dark';
export const getTheme = () => read<Theme>('theme');
export const saveTheme = (theme: Theme) => write('theme', theme);

// --- Auto-update preference (fields track the calendar) ---
export const getAutoUpdate = () => read<boolean>('autoUpdate') ?? true;
export const saveAutoUpdate = (on: boolean) => write('autoUpdate', on);

// --- Font scale (percent, e.g. 100) ---
export const getFontScale = () => read<number>('fontScale') ?? 100;
export const saveFontScale = (pct: number) => write('fontScale', pct);
