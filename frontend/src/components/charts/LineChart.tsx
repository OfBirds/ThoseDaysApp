// Responsive SVG line chart with an optional "typical range" band and mean line.
// Pure presentational: callers pass already-derived numbers. Colors come from CSS
// variables so it themes with the rest of the app.

interface LineChartProps {
  values: number[];
  labels?: string[]; // x tick labels, aligned to values
  band?: { lo: number; hi: number }; // shaded typical range
  mean?: number; // dashed reference line
  unit?: string;
  height?: number;
}

const W = 600;
const PAD = { top: 16, right: 16, bottom: 28, left: 34 };

function LineChart({ values, labels, band, mean, unit = '', height = 220 }: LineChartProps) {
  if (values.length < 2) {
    return <p className="chart-empty">Not enough data yet — log a few more cycles.</p>;
  }

  const H = height;
  const plotW = W - PAD.left - PAD.right;
  const plotH = H - PAD.top - PAD.bottom;

  const lo = Math.min(...values, band?.lo ?? Infinity);
  const hi = Math.max(...values, band?.hi ?? -Infinity);
  const padY = Math.max(1, (hi - lo) * 0.15);
  const yMin = Math.floor(lo - padY);
  const yMax = Math.ceil(hi + padY);

  const x = (i: number) => PAD.left + (values.length === 1 ? plotW / 2 : (i / (values.length - 1)) * plotW);
  const y = (v: number) => PAD.top + plotH - ((v - yMin) / (yMax - yMin)) * plotH;

  const line = values.map((v, i) => `${i === 0 ? 'M' : 'L'}${x(i).toFixed(1)},${y(v).toFixed(1)}`).join(' ');
  const ticks = [yMin, Math.round((yMin + yMax) / 2), yMax];
  const labelStep = Math.ceil(values.length / 6);

  return (
    <svg className="chart" viewBox={`0 0 ${W} ${H}`} role="img" preserveAspectRatio="xMidYMid meet">
      {band && (
        <rect
          x={PAD.left}
          y={y(band.hi)}
          width={plotW}
          height={Math.max(0, y(band.lo) - y(band.hi))}
          className="chart-band"
        />
      )}
      {ticks.map((t) => (
        <g key={t}>
          <line x1={PAD.left} x2={W - PAD.right} y1={y(t)} y2={y(t)} className="chart-grid" />
          <text x={PAD.left - 6} y={y(t) + 4} className="chart-axis" textAnchor="end">
            {t}
          </text>
        </g>
      ))}
      {mean !== undefined && (
        <line x1={PAD.left} x2={W - PAD.right} y1={y(mean)} y2={y(mean)} className="chart-mean" />
      )}
      <path d={line} className="chart-line" fill="none" />
      {values.map((v, i) => (
        <circle key={i} cx={x(i)} cy={y(v)} r={3.5} className="chart-dot">
          <title>{`${labels?.[i] ?? i + 1}: ${v}${unit ? ' ' + unit : ''}`}</title>
        </circle>
      ))}
      {labels &&
        labels.map((l, i) =>
          i % labelStep === 0 ? (
            <text key={i} x={x(i)} y={H - 8} className="chart-axis" textAnchor="middle">
              {l}
            </text>
          ) : null
        )}
    </svg>
  );
}

export default LineChart;
