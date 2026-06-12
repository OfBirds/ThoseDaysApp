// GitHub-style calendar heatmap of period days over the trailing ~12 months.
// One column per week, one cell per day; period days are filled.

import { addDaysIso } from '../../lib/stats';

interface HeatmapProps {
  periodDays: Set<string>;
  endIso: string; // usually today
  weeks?: number;
}

const CELL = 11;
const GAP = 3;
const TOP = 16; // room for month labels
const LEFT = 18; // room for weekday labels
const DOW = ['', 'M', '', 'W', '', 'F', ''];
const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

function Heatmap({ periodDays, endIso, weeks = 53 }: HeatmapProps) {
  // Anchor the last column on the Sunday of this week so rows line up by weekday.
  const end = new Date(endIso + 'T00:00:00Z');
  const endDow = end.getUTCDay();
  const lastSunday = addDaysIso(endIso, -endDow);
  const firstSunday = addDaysIso(lastSunday, -(weeks - 1) * 7);

  const cols: { iso: string; filled: boolean }[][] = [];
  const monthMarks: { col: number; label: string }[] = [];
  let lastMonth = -1;

  for (let w = 0; w < weeks; w++) {
    const col: { iso: string; filled: boolean }[] = [];
    for (let d = 0; d < 7; d++) {
      const iso = addDaysIso(firstSunday, w * 7 + d);
      col.push({ iso, filled: periodDays.has(iso) });
      if (d === 0) {
        const month = Number(iso.slice(5, 7)) - 1;
        if (month !== lastMonth) {
          monthMarks.push({ col: w, label: MONTHS[month] });
          lastMonth = month;
        }
      }
    }
    cols.push(col);
  }

  const width = LEFT + weeks * (CELL + GAP);
  const height = TOP + 7 * (CELL + GAP);

  return (
    <svg className="heatmap" viewBox={`0 0 ${width} ${height}`} role="img" aria-label="Period days over the last year">
      {monthMarks.map((m) => (
        <text key={m.col} x={LEFT + m.col * (CELL + GAP)} y={11} className="chart-axis">
          {m.label}
        </text>
      ))}
      {DOW.map((d, i) =>
        d ? (
          <text key={i} x={2} y={TOP + i * (CELL + GAP) + CELL - 1} className="chart-axis">
            {d}
          </text>
        ) : null
      )}
      {cols.map((col, w) =>
        col.map((cell, d) =>
          cell.iso <= endIso ? (
            <rect
              key={cell.iso}
              x={LEFT + w * (CELL + GAP)}
              y={TOP + d * (CELL + GAP)}
              width={CELL}
              height={CELL}
              rx={2}
              className={cell.filled ? 'heat-cell heat-on' : 'heat-cell'}
            >
              <title>{cell.iso}{cell.filled ? ' — period' : ''}</title>
            </rect>
          ) : null
        )
      )}
    </svg>
  );
}

export default Heatmap;
