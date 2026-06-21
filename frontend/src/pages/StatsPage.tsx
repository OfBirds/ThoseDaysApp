import { useEffect, useMemo, useState } from 'react';
import { apiFetch } from '../lib/api';
import { useAuth } from '../context/AuthContext';
import { findNextPrediction, predictionConfidence, type RecalcConfig } from '../lib/predictions';
import {
  toPeriods, summarize, histogram, currentCycle, periodDaySet, accuracy, recentRows,
  type CycleRecord,
} from '../lib/stats';
import LineChart from '../components/charts/LineChart';
import BarStrip from '../components/charts/BarStrip';
import Histogram from '../components/charts/Histogram';
import Ring from '../components/charts/Ring';
import Heatmap from '../components/charts/Heatmap';
import '../styles/stats.css';

interface Prediction {
  predictedStart: string;
  predictedDuration: number;
}

const DEFAULT_CONFIG: RecalcConfig = {
  weights: [3, 2, 1], tailWeight: 1,
  defaultCycleLength: 28, defaultPeriodDuration: 5,
  cycleLengthMin: 21, cycleLengthMax: 35,
  periodDurationMin: 2, periodDurationMax: 10,
  confidenceFloor: 0.3, confidenceNominal: 0.7, confidenceMinIntervals: 2, bandK: 1,
};

function todayIso() {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

const fmt = (n: number, d = 1) => (Number.isFinite(n) ? n.toFixed(d) : '—');
const prettyDate = (iso: string) =>
  new Date(iso + 'T00:00:00').toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' });

function StatsPage() {
  const { user } = useAuth();
  const [cycles, setCycles] = useState<CycleRecord[]>([]);
  const [predictions, setPredictions] = useState<Prediction[]>([]);
  const [config, setConfig] = useState<RecalcConfig>(DEFAULT_CONFIG);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    if (!user) return;
    let alive = true;
    (async () => {
      try {
        const [c, p, cfg] = await Promise.all([
          apiFetch(`/api/user/${user.id}/cycles`).then((r) => (r.ok ? r.json() : [])),
          apiFetch(`/api/user/${user.id}/predictions`).then((r) => (r.ok ? r.json() : [])),
          fetch('/api/config').then((r) => (r.ok ? r.json() : DEFAULT_CONFIG)),
        ]);
        if (!alive) return;
        setCycles(c);
        setPredictions(p);
        if (cfg) setConfig(cfg);
      } finally {
        if (alive) setLoading(false);
      }
    })();
    return () => {
      alive = false;
    };
  }, [user]);

  const view = useMemo(() => {
    const periods = toPeriods(cycles);
    const summary = summarize(periods, config.weights, config.tailWeight, config.defaultCycleLength);
    const today = todayIso();
    const next = findNextPrediction(predictions, today);
    return {
      periods,
      summary,
      today,
      current: currentCycle(periods, today, next?.startIso ?? null, summary.weightedInterval || config.defaultCycleLength),
      hist: histogram(summary.intervals, 2),
      heat: periodDaySet(periods),
      acc: accuracy(periods),
      rows: recentRows(periods),
    };
  }, [cycles, predictions, config]);

  if (loading) return <div className="stats-page"><p className="chart-empty">Loading…</p></div>;

  const { periods, summary, current, hist, heat, acc, rows, today } = view;

  if (periods.length === 0) {
    return (
      <div className="stats-page">
        <h1 className="page-title">Statistics</h1>
        <p className="chart-empty">No cycles logged yet. Add a few on the calendar and your stats will appear here.</p>
      </div>
    );
  }

  const intervalLabels = periods.slice(1).map((p) => prettyDate(p.start).replace(/,.*/, ''));
  const intervalValues = summary.intervals;
  const durationLabels = periods.map((p) => prettyDate(p.start).replace(/,.*/, ''));
  const confidencePct = Math.round(
    predictionConfidence(summary.intervals, summary.weightedInterval || config.defaultCycleLength, config) * 100
  );

  return (
    <div className="stats-page">
      <h1 className="page-title">Statistics</h1>

      {/* KPI cards */}
      <section className="kpi-grid">
        <Kpi label="Cycles tracked" value={`${summary.totalCycles}`} />
        <Kpi label="Tracked span" value={`${Math.round(summary.trackedDays / 30.4)} mo`} sub={`${summary.trackedDays} days`} />
        <Kpi label="Avg cycle length" value={`${fmt(summary.meanInterval)}`} sub={`median ${fmt(summary.medianInterval, 0)} d`} />
        <Kpi label="Regularity" value={`±${fmt(summary.intervalStdDev)}`} sub="days variation" />
        <Kpi label="Shortest / longest" value={`${summary.shortestInterval || '—'} / ${summary.longestInterval || '—'}`} sub="cycle days" />
        <Kpi label="Avg period" value={`${fmt(summary.meanDuration)} d`} sub={`${summary.shortestDuration}–${summary.longestDuration} d range`} />
      </section>

      {/* Current cycle */}
      {current && (
        <section className="card current-cycle">
          <h2>Current cycle</h2>
          <div className="current-cycle-body">
            <Ring fraction={current.fraction} day={current.day} expectedLength={current.expectedLength} overdue={current.overdue} />
            <p className="current-cycle-note">
              You're on <strong>day {current.day}</strong> of an expected ~{current.expectedLength}-day cycle.
              {current.overdue && ' Your next period is past its predicted start.'}
            </p>
          </div>
        </section>
      )}

      {/* Forecast confidence */}
      <section className="card">
        <h2
          title="How sure we are about your predicted dates. It's higher when your recent cycles have been regular, lower when they vary a lot."
        >
          Forecast confidence
        </h2>
        <p className="card-hint">
          Your cycles vary by about <strong>±{fmt(summary.intervalStdDev)} days</strong>, so your
          predicted dates carry roughly <strong>{confidencePct}% confidence</strong>.{' '}
          {confidencePct >= 75
            ? 'Your cycles are quite regular, so the dates should land close.'
            : "Your cycles vary a fair bit, so treat each date as a best guess — it could come a little earlier or later."}
        </p>
      </section>

      {/* Cycle length over time */}
      <section className="card">
        <h2>Cycle length over time</h2>
        <p className="card-hint">Days between period starts. The shaded band is the typical {config.cycleLengthMin}–{config.cycleLengthMax}-day range; the dashed line is your mean.</p>
        <LineChart
          values={intervalValues}
          labels={intervalLabels}
          band={{ lo: config.cycleLengthMin, hi: config.cycleLengthMax }}
          mean={summary.meanInterval}
          unit="days"
        />
      </section>

      {/* Period duration */}
      <section className="card">
        <h2>Period duration</h2>
        <p className="card-hint">How many days each period lasted.</p>
        <BarStrip values={summary.durations} labels={durationLabels} unit="days" />
      </section>

      {/* Distribution */}
      <section className="card">
        <h2>Cycle length distribution</h2>
        <p className="card-hint">How often your cycles fall into each length bucket. A tight cluster means regular cycles.</p>
        <Histogram bins={hist} unit="days" />
      </section>

      {/* Heatmap */}
      <section className="card">
        <h2>Period days this year</h2>
        <Heatmap periodDays={heat} endIso={today} />
      </section>

      {/* Prediction accuracy */}
      <section className="card">
        <h2>Prediction accuracy</h2>
        {acc.count === 0 ? (
          <p className="card-hint">
            {acc.acceptedCount > 0
              ? `${acc.acceptedCount} forecast period${acc.acceptedCount === 1 ? '' : 's'} were accepted as predicted. Accuracy is measured once you correct an auto-filled period.`
              : 'No forecast periods have come due yet — accuracy will show here as predictions are tested against reality.'}
          </p>
        ) : (
          <>
            <p className="card-hint">
              Across {acc.count} corrected forecast{acc.count === 1 ? '' : 's'}, predictions were off by an average of{' '}
              <strong>{fmt(acc.mae)} days</strong>
              {Math.abs(acc.bias) >= 0.5 && <> and tended to run {acc.bias > 0 ? 'late' : 'early'} by {fmt(Math.abs(acc.bias))} days</>}.
            </p>
            <ul className="error-list">
              {acc.errors.map((e) => (
                <li key={e.start}>
                  <span>{prettyDate(e.start)}</span>
                  <span className={e.error === 0 ? '' : e.error > 0 ? 'err-late' : 'err-early'}>
                    {e.error === 0 ? 'exact' : `${e.error > 0 ? '+' : ''}${e.error} d`}
                  </span>
                </li>
              ))}
            </ul>
          </>
        )}
      </section>

      {/* Recent cycles list */}
      <section className="card">
        <h2>Recent cycles</h2>
        <table className="cycle-table">
          <thead>
            <tr><th>Start</th><th>Length</th><th>Period</th><th>vs avg</th><th>Source</th></tr>
          </thead>
          <tbody>
            {rows.map((r) => (
              <tr key={r.id}>
                <td>{prettyDate(r.start)}</td>
                <td>{r.interval === null ? '—' : `${r.interval} d`}</td>
                <td>{r.length} d</td>
                <td className={r.deviation === null ? '' : r.deviation > 0 ? 'err-late' : r.deviation < 0 ? 'err-early' : ''}>
                  {r.deviation === null ? '—' : `${r.deviation > 0 ? '+' : ''}${fmt(r.deviation)}`}
                </td>
                <td>
                  <span className={`badge badge-${r.corrected ? 'corrected' : r.auto ? 'auto' : 'logged'}`}>
                    {r.corrected ? 'corrected' : r.auto ? 'auto' : 'logged'}
                  </span>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </section>
    </div>
  );
}

function Kpi({ label, value, sub }: { label: string; value: string; sub?: string }) {
  return (
    <div className="kpi">
      <span className="kpi-value">{value}</span>
      <span className="kpi-label">{label}</span>
      {sub && <span className="kpi-sub">{sub}</span>}
    </div>
  );
}

export default StatsPage;
