import { describe, it, expect, beforeEach, vi } from 'vitest';
import {
  getDraft, saveDraft, clearDraft,
  getAutoUpdate, getFontScale,
  getTheme, saveTheme,
  type Draft,
} from '../lib/storage';

// localStorage is available in jsdom environment by default.

const TEST_USER = 'test-user-123';

describe('storage', () => {
  beforeEach(() => {
    localStorage.clear();
  });

  describe('draft save/read round-trip', () => {
    it('saves and reads a draft correctly', () => {
      const draft: Draft = {
        days: ['2025-01-01', '2025-01-02', '2025-01-03'],
        cycleLength: 28,
        periodDuration: 5,
        dirty: true,
      };

      saveDraft(TEST_USER, draft);
      const read = getDraft(TEST_USER);

      expect(read).not.toBeNull();
      expect(read!.days).toEqual(['2025-01-01', '2025-01-02', '2025-01-03']);
      expect(read!.cycleLength).toBe(28);
      expect(read!.periodDuration).toBe(5);
      expect(read!.dirty).toBe(true);
    });

    it('uses tda: key prefix', () => {
      const draft: Draft = { days: [], cycleLength: 28, periodDuration: 5, dirty: false };
      saveDraft(TEST_USER, draft);

      // The underlying localStorage key should be namespaced.
      const keys = Object.keys(localStorage);
      const draftKey = keys.find(k => k.includes('tda:draft'));
      expect(draftKey).toBeDefined();
      expect(draftKey).toBe(`tda:draft:${TEST_USER}`);
    });

    it('clearDraft removes the key', () => {
      const draft: Draft = { days: ['2025-06-01'], cycleLength: 30, periodDuration: 4, dirty: true };
      saveDraft(TEST_USER, draft);
      clearDraft(TEST_USER);

      expect(getDraft(TEST_USER)).toBeNull();
    });
  });

  describe('getAutoUpdate', () => {
    it('returns true by default', () => {
      expect(getAutoUpdate()).toBe(true);
    });
  });

  describe('getFontScale', () => {
    it('returns 100 by default', () => {
      expect(getFontScale()).toBe(100);
    });
  });

  describe('malformed JSON', () => {
    it('returns null when the stored value is not valid JSON', () => {
      // Write a corrupt value under the real key read() will parse, so the
      // try/catch in read() is actually exercised.
      localStorage.setItem('tda:theme', '{not valid json}');
      expect(getTheme()).toBeNull();
    });
  });

  describe('theme', () => {
    it('saves and reads theme', () => {
      saveTheme('dark');
      expect(getTheme()).toBe('dark');

      saveTheme('light');
      expect(getTheme()).toBe('light');
    });
  });
});
