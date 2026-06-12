// Progress ring for the cycle in progress: day N of an expected length.

interface RingProps {
  fraction: number; // 0..1
  day: number;
  expectedLength: number;
  overdue: boolean;
}

const SIZE = 160;
const STROKE = 14;
const R = (SIZE - STROKE) / 2;
const C = 2 * Math.PI * R;

function Ring({ fraction, day, expectedLength, overdue }: RingProps) {
  const dash = Math.min(1, fraction) * C;
  return (
    <svg className="ring" viewBox={`0 0 ${SIZE} ${SIZE}`} role="img" aria-label={`Day ${day} of ${expectedLength}`}>
      <circle cx={SIZE / 2} cy={SIZE / 2} r={R} className="ring-track" fill="none" strokeWidth={STROKE} />
      <circle
        cx={SIZE / 2}
        cy={SIZE / 2}
        r={R}
        className={overdue ? 'ring-progress ring-overdue' : 'ring-progress'}
        fill="none"
        strokeWidth={STROKE}
        strokeDasharray={`${dash} ${C}`}
        strokeLinecap="round"
        transform={`rotate(-90 ${SIZE / 2} ${SIZE / 2})`}
      />
      <text x="50%" y="46%" className="ring-day" textAnchor="middle">
        {day}
      </text>
      <text x="50%" y="64%" className="ring-sub" textAnchor="middle">
        {overdue ? `of ~${expectedLength} (overdue)` : `of ~${expectedLength}`}
      </text>
    </svg>
  );
}

export default Ring;
