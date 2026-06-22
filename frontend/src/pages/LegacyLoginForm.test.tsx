import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const login = vi.fn().mockResolvedValue(undefined);
const register = vi.fn().mockResolvedValue(undefined);

vi.mock('@bearsoft/auth-core/react', () => ({
  useAuth: () => ({ login, register }),
}));

import LegacyLoginForm from './LegacyLoginForm';

beforeEach(() => {
  vi.clearAllMocks();
  window.location.hash = '';
  localStorage.clear();
});

describe('LegacyLoginForm', () => {
  it('shows the live password checklist only in the register view and tracks rule state', async () => {
    const user = userEvent.setup();
    render(<LegacyLoginForm />);

    expect(screen.queryByText('One special character')).toBeNull();
    await user.click(screen.getByRole('button', { name: 'Create Account' }));

    const rule = () => screen.getByText('One special character').closest('li')!;
    const lengthRule = () => screen.getByText('At least 8 characters').closest('li')!;
    expect(rule().className).not.toContain('met');

    await user.type(screen.getByLabelText('Password'), 'abc');
    expect(lengthRule().className).not.toContain('met');

    await user.clear(screen.getByLabelText('Password'));
    await user.type(screen.getByLabelText('Password'), 'Str0ng!pw');
    expect(lengthRule().className).toContain('met');
    expect(rule().className).toContain('met');
  });

  it('signs in with email + password by default', async () => {
    const user = userEvent.setup();
    render(<LegacyLoginForm />);
    await user.type(screen.getByLabelText('Email'), 'a@b.c');
    await user.type(screen.getByLabelText('Password'), 'Str0ng!pw');
    await user.click(screen.getByRole('button', { name: 'Sign In' }));
    expect(login).toHaveBeenCalledWith('a@b.c', 'Str0ng!pw');
  });
});
