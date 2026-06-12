// Compact bar strip — used for period duration over time beneath the line chart.

interface BarStripProps {
  values: number[];
  labels?: string[];
  unit?: string;
  height?: number;
}

const W = 600;
const PAD = { top: 10, right: 16, bottom: 22, left: 34 };

function BarStrip({ values, labels, unit = '', height = 120 }: BarStripProps) {
  if (values.length === 0) {
    return <p className="chart-empty">No data yet.</p>;
  }
  const H = height;
  const plotW = W - PAD.left - PAD.right;
  const plotH = H - PAD.top - PAD.bottom;
  const hi = Math.max(...values, 1);
  const gap = 2;
  const bw = plotW / values.length - gap;

  return (
    <svg className="chart" viewBox={`0 0 ${W} ${H}`} role="img" preserveAspectRatio="xMidYMid meet">
      <text x={PAD.left - 6} y={PAD.top + 8} className="chart-axis" textAnchor="end">
        {hi}
      </text>
      {values.map((v, i) => {
        const h = (v / hi) * plotH;
        const x = PAD.left + i * (bw + gap);
        return (
          <rect key={i} x={x} y={PAD.top + plotH - h} width={Math.max(1, bw)} height={h} className="chart-bar" rx={1}>
            <title>{`${labels?.[i] ?? i + 1}: ${v}${unit ? ' ' + unit : ''}`}</title>
          </rect>
        );
      })}
    </svg>
  );
}

export default BarStrip;
