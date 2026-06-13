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

describe('SettingsPage', () => {
  afterEach(() => {
    cleanup();
    vi.restoreAllMocks();
  });

  beforeEach(() => {
    // GET prefs → opted in; PUT echoes whatever was sent.
    globalThis.fetch = vi.fn((url: string | URL | Request, opts?: RequestInit) => {
      const method = opts?.method ?? 'GET';
      if (method === 'PUT') {
        return Promise.resolve({
          ok: true,
          json: async () => JSON.parse(opts!.body as string),
        } as Response);
      }
      return Promise.resolve({
        ok: true,
        json: async () => ({ notifyReleases: true }),
      } as Response);
    }) as unknown as typeof fetch;
  });

  it('renders the toggle reflecting the loaded preference', async () => {
    render(<SettingsPage />);
    const checkbox = await screen.findByRole('checkbox');
    expect(checkbox).toBeChecked();
  });

  it('PUTs the new value when toggled off', async () => {
    const user = userEvent.setup();
    render(<SettingsPage />);
    const checkbox = await screen.findByRole('checkbox');

    await user.click(checkbox);

    await waitFor(() => {
      const putCall = (globalThis.fetch as ReturnType<typeof vi.fn>).mock.calls.find(
        (c) => (c[1] as RequestInit | undefined)?.method === 'PUT'
      );
      expect(putCall).toBeTruthy();
      expect(putCall![0]).toBe('/api/user/user-1/prefs');
      expect(JSON.parse((putCall![1] as RequestInit).body as string)).toEqual({
        notifyReleases: false,
      });
    });

    await waitFor(() => expect(screen.getByRole('checkbox')).not.toBeChecked());
  });
});
