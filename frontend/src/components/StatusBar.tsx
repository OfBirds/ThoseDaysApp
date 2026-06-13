import '../styles/status-bar.css';

interface StatusBarProps {
  averageCycleLength: number;
  averageInterval: number;
  totalCycles: number;
  nextPeriodDays: number | null;
  nextPeriodRange?: string | null;
}

function formatNext(days: number | null): string {
  if (days === null) return '—';
  if (days === 0) return 'today';
  return `in ${days} day${days === 1 ? '' : 's'}`;
}

function StatusBar({ averageCycleLength, averageInterval, totalCycles, nextPeriodDays, nextPeriodRange }: StatusBarProps) {
  return (
    <div className="status-bar">
      <div className="status-next">
        <span className="status-next-label">Next period</span>
        <span className="status-next-value">{formatNext(nextPeriodDays)}</span>
        {nextPeriodDays !== null && nextPeriodRange && (
          <span
            className="status-next-range"
            title="Your cycles vary, so the date could land a little earlier or later — this is the likely window."
          >
            {nextPeriodRange}
          </span>
        )}
      </div>
      <div className="status-bar-content">
        <h2 className="status-title">Past analysis</h2>
        <div className="status-stats">
          <div className="stat">
            <span className="stat-label">Avg Cycle Length</span>
            <span className="stat-value">{averageCycleLength.toFixed(1)} days</span>
          </div>
          <div className="stat">
            <span className="stat-label">Avg Interval</span>
            <span className="stat-value">{averageInterval.toFixed(1)} days</span>
          </div>
          <div className="stat">
            <span className="stat-label">Total Cycles</span>
            <span className="stat-value">{totalCycles}</span>
          </div>
        </div>
      </div>
    </div>
  );
}

export default StatusBar;
