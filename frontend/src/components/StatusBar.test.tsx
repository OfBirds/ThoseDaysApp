import { describe, it, expect, afterEach } from 'vitest';
import { render, screen, cleanup } from '@testing-library/react';
import StatusBar from './StatusBar';

describe('StatusBar', () => {
  afterEach(() => cleanup());

  it('shows "in N days" for a given prediction', () => {
    render(
      <StatusBar
        averageCycleLength={28}
        averageInterval={30}
        totalCycles={3}
        nextPeriodDays={5}
      />
    );

    expect(screen.getByText('in 5 days')).toBeInTheDocument();
  });

  it('shows "today" when daysUntil is 0', () => {
    render(
      <StatusBar
        averageCycleLength={28}
        averageInterval={30}
        totalCycles={3}
        nextPeriodDays={0}
      />
    );

    expect(screen.getByText('today')).toBeInTheDocument();
  });

  it('shows "in 1 day" (singular)', () => {
    render(
      <StatusBar
        averageCycleLength={28}
        averageInterval={30}
        totalCycles={3}
        nextPeriodDays={1}
      />
    );

    expect(screen.getByText('in 1 day')).toBeInTheDocument();
  });

  it('shows "—" when no prediction', () => {
    render(
      <StatusBar
        averageCycleLength={28}
        averageInterval={30}
        totalCycles={3}
        nextPeriodDays={null}
      />
    );

    expect(screen.getByText('—')).toBeInTheDocument();
  });

  it('renders past analysis stats', () => {
    render(
      <StatusBar
        averageCycleLength={28.5}
        averageInterval={30.2}
        totalCycles={3}
        nextPeriodDays={null}
      />
    );

    expect(screen.getByText('28.5 days')).toBeInTheDocument();
    expect(screen.getByText('30.2 days')).toBeInTheDocument();
    expect(screen.getByText('3')).toBeInTheDocument();
  });
});
