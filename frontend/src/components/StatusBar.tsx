import '../styles/status-bar.css';

interface StatusBarProps {
  averageCycleLength: number;
  averageInterval: number;
  totalCycles: number;
  nextPeriodDays: number | null;
}

function formatNext(days: number | null): string {
  if (days === null) return '—';
  if (days === 0) return 'today';
  return `in ${days} day${days === 1 ? '' : 's'}`;
}

function StatusBar({ averageCycleLength, averageInterval, totalCycles, nextPeriodDays }: StatusBarProps) {
  return (
    <div className="status-bar">
      <div className="status-next">
        <span className="status-next-label">Next period</span>
        <span className="status-next-value">{formatNext(nextPeriodDays)}</span>
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
