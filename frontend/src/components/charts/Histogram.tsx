// Distribution of cycle lengths — horizontal-labelled vertical bars.

import type { Bin } from '../../lib/stats';

interface HistogramProps {
  bins: Bin[];
  unit?: string;
  height?: number;
}

const W = 600;
const PAD = { top: 12, right: 16, bottom: 30, left: 28 };

function Histogram({ bins, unit = '', height = 180 }: HistogramProps) {
  if (bins.length === 0) {
    return <p className="chart-empty">Not enough data yet.</p>;
  }
  const H = height;
  const plotW = W - PAD.left - PAD.right;
  const plotH = H - PAD.top - PAD.bottom;
  const hi = Math.max(...bins.map((b) => b.count), 1);
  const gap = 4;
  const bw = plotW / bins.length - gap;

  return (
    <svg className="chart" viewBox={`0 0 ${W} ${H}`} role="img" preserveAspectRatio="xMidYMid meet">
      {bins.map((b, i) => {
        const h = (b.count / hi) * plotH;
        const x = PAD.left + i * (bw + gap);
        return (
          <g key={i}>
            <rect x={x} y={PAD.top + plotH - h} width={Math.max(1, bw)} height={h} className="chart-hist" rx={2}>
              <title>{`${b.label}${unit ? ' ' + unit : ''}: ${b.count}`}</title>
            </rect>
            {b.count > 0 && (
              <text x={x + bw / 2} y={PAD.top + plotH - h - 4} className="chart-axis" textAnchor="middle">
                {b.count}
              </text>
            )}
            <text x={x + bw / 2} y={H - 10} className="chart-axis" textAnchor="middle">
              {b.label}
            </text>
          </g>
        );
      })}
    </svg>
  );
}

export default Histogram;
