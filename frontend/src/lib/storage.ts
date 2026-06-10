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
