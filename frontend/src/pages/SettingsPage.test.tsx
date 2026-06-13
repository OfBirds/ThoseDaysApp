import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, waitFor, cleanup } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import SettingsPage from './SettingsPage';

// Stub auth so the page has a user without an AuthProvider. The user object must
// be stable across renders (like the real useState-backed context), otherwise the
// useEffect([user]) would re-run every render.
vi.mock('../context/AuthContext', () => {
  const user = { id: 'user-1', email: 'a@b.c' };
  return { useAuth: () => ({ user }) };
});

const initialPrefs = { notifyReleases: true, notifyPeriodReminder: false, reminderLeadDays: 2 };

function lastPut() {
  const puts = (globalThis.fetch as ReturnType<typeof vi.fn>).mock.calls.filter(
    (c) => (c[1] as RequestInit | undefined)?.method === 'PUT'
  );
  return puts[puts.length - 1];
}

describe('SettingsPage', () => {
  afterEach(() => {
    cleanup();
    vi.restoreAllMocks();
  });

  beforeEach(() => {
    globalThis.fetch = vi.fn((_url: string | URL | Request, opts?: RequestInit) => {
      if ((opts?.method ?? 'GET') === 'PUT') {
        return Promise.resolve({ ok: true, json: async () => JSON.parse(opts!.body as string) } as Response);
      }
      return Promise.resolve({ ok: true, json: async () => ({ ...initialPrefs }) } as Response);
    }) as unknown as typeof fetch;
  });

  it('renders toggles reflecting the loaded preferences', async () => {
    render(<SettingsPage />);
    const releases = await screen.findByRole('checkbox', { name: /new versions/i });
    const reminder = screen.getByRole('checkbox', { name: /before my period/i });
    expect(releases).toBeChecked();
    expect(reminder).not.toBeChecked();
  });

  it('PUTs the release pref when toggled off, preserving the others', async () => {
    const user = userEvent.setup();
    render(<SettingsPage />);
    const releases = await screen.findByRole('checkbox', { name: /new versions/i });

    await user.click(releases);

    await waitFor(() => {
      const put = lastPut();
      expect(put).toBeTruthy();
      expect(put![0]).toBe('/api/user/user-1/prefs');
      expect(JSON.parse((put![1] as RequestInit).body as string)).toEqual({
        notifyReleases: false,
        notifyPeriodReminder: false,
        reminderLeadDays: 2,
      });
    });
    await waitFor(() => expect(screen.getByRole('checkbox', { name: /new versions/i })).not.toBeChecked());
  });

  it('turning reminders on PUTs the opt-in and reveals the lead-days input', async () => {
    const user = userEvent.setup();
    render(<SettingsPage />);
    const reminder = await screen.findByRole('checkbox', { name: /before my period/i });

    await user.click(reminder);

    await waitFor(() => {
      const put = lastPut();
      expect(put).toBeTruthy();
      expect(JSON.parse((put![1] as RequestInit).body as string).notifyPeriodReminder).toBe(true);
    });
    // Lead-days input appears once reminders are on.
    await waitFor(() => expect(screen.getByRole('spinbutton')).toBeInTheDocument());
  });
});
