import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, waitFor, cleanup, within, fireEvent } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import DataSection from './DataSection';
import { getPendingImport } from '../lib/storage';

const cycles = [
  { startDate: '2026-01-01', durationDays: 5 },
  { startDate: '2026-02-01', durationDays: 5 },
  { startDate: '2026-03-01', durationDays: 5 },
];

const validFile = (obj: unknown) =>
  new File([JSON.stringify(obj)], 'export.json', { type: 'application/json' });

function renderSection() {
  return render(
    <MemoryRouter>
      <DataSection userId="user-1" />
    </MemoryRouter>
  );
}

describe('DataSection', () => {
  afterEach(() => {
    cleanup();
    vi.restoreAllMocks();
    localStorage.clear();
  });

  beforeEach(() => {
    globalThis.fetch = vi.fn((url: string | URL | Request) => {
      if (String(url).includes('/cycles')) {
        return Promise.resolve({ ok: true, json: async () => cycles } as Response);
      }
      return Promise.resolve({ ok: true, json: async () => ({}) } as Response);
    }) as unknown as typeof fetch;
  });

  it('shows the export date range and updates it when choosing Last N', async () => {
    renderSection();
    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent(/3 cycles/));

    fireEvent.click(screen.getByRole('radio', { name: /Last/ }));
    fireEvent.change(screen.getByRole('spinbutton', { name: /number of cycles/i }), {
      target: { value: '1' },
    });

    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent(/1 cycle:/));
  });

  it('rejects an invalid file with a message and shows no review', async () => {
    renderSection();
    const input = await screen.findByLabelText(/choose a file to import/i);
    await userEvent.upload(input, new File(['not json'], 'x.json', { type: 'application/json' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(/JSON/);
    expect(screen.queryByRole('group', { name: /review this import/i })).not.toBeInTheDocument();
  });

  it('stages a valid import only after the consent click', async () => {
    renderSection();
    const input = await screen.findByLabelText(/choose a file to import/i);

    await userEvent.upload(
      input,
      validFile({ schemaVersion: 1, cycles: [{ startDate: '2026-02-10', durationDays: 4 }] })
    );

    const review = await screen.findByRole('group', { name: /review this import/i });
    expect(review).toHaveTextContent(/update your history/i);
    expect(getPendingImport('user-1')).toBeNull(); // nothing staged yet

    await userEvent.click(within(review).getByRole('button', { name: /show it to me/i }));

    await waitFor(() => expect(getPendingImport('user-1')).not.toBeNull());
    expect(getPendingImport('user-1')!.cycles).toHaveLength(1);
    expect(screen.getByText(/not saved yet/i)).toBeInTheDocument();
  });
});
