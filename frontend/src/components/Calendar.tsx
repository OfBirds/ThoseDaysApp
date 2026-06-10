import { useState, useEffect, useCallback } from 'react';
import '../styles/calendar.css';
import BloodDropIcon from './BloodDropIcon';
import { getDraft, saveDraft, getAutoUpdate, saveAutoUpdate, Draft } from '../lib/storage';

interface Cycle {
  id: string;
  startDate: string;
  durationDays: number;
  createdAt: string;
  corrected: boolean;
  auto: boolean;
}

interface Prediction {
  id: string;
  predictedStart: string;
  predictedDuration: number;
  confidence: number;
}

interface RecalcConfig {
  weights: number[];
  tailWeight: number;
  defaultCycleLength: number;
  defaultPeriodDuration: number;
  cycleLengthMin: number;
  cycleLengthMax: number;
  periodDurationMin: number;
  periodDurationMax: number;
}

interface CalendarProps {
  cycles: Cycle[];
  onCommitted: () => void;
  userId: string;
}

const DEFAULT_CONFIG: RecalcConfig = {
  weights: [3, 2, 1],
  tailWeight: 1,
  defaultCycleLength: 28,
  defaultPeriodDuration: 5,
  cycleLengthMin: 21,
  cycleLengthMax: 35,
  periodDurationMin: 2,
  periodDurationMax: 10
};

// --- date helpers (string-based to avoid timezone drift) ---
const isoDate = (date: Date) => {
  const yyyy = date.getFullYear();
  const mm = String(date.getMonth() + 1).padStart(2, '0');
  const dd = String(date.getDate()).padStart(2, '0');
  return `${yyyy}-${mm}-${dd}`;
};

const addDaysIso = (iso: string, n: number) => {
  const [y, m, d] = iso.split('-').map(Number);
  return new Date(Date.UTC(y, m - 1, d + n)).toISOString().slice(0, 10);
};

const daysBetween = (a: string, b: string) =>
  Math.round((Date.parse(b + 'T00:00:00Z') - Date.parse(a + 'T00:00:00Z')) / 86400000);

// Expand cycles/predictions (start + duration) into the set of ISO day strings.
function spanDays(startIso: string, duration: number): string[] {
  const out: string[] = [];
  for (let i = 0; i < Math.max(1, duration); i++) out.push(addDaysIso(startIso, i));
  return out;
}

function cyclesToDays(cycles: Cycle[]): string[] {
  const set = new Set<string>();
  cycles.forEach(c => spanDays(c.startDate.slice(0, 10), c.durationDays).forEach(d => set.add(d)));
  return [...set].sort();
}

// Collapse painted days into consecutive-day periods. Mirrors the backend.
function groupPeriods(days: string[]): { start: string; length: number }[] {
  const sorted = [...new Set(days)].sort();
  const periods: { start: string; length: number }[] = [];
  let runStart: string | null = null;
  let prev: string | null = null;
  let len = 0;
  for (const d of sorted) {
    if (runStart === null) { runStart = d; len = 1; }
    else if (d === addDaysIso(prev!, 1)) { len++; }
    else { periods.push({ start: runStart, length: len }); runStart = d; len = 1; }
    prev = d;
  }
  if (runStart !== null) periods.push({ start: runStart, length: len });
  return periods;
}

// Recent-favored weighted mean, rounded. Values newest → oldest. Mirrors the backend.
function weightedAvg(valuesNewestFirst: number[], weights: number[], tailWeight: number, fallback: number) {
  if (valuesNewestFirst.length === 0) return fallback;
  let ws = 0, wt = 0;
  for (let i = 0; i < valuesNewestFirst.length; i++) {
    const w = i < weights.length ? weights[i] : tailWeight;
    ws += valuesNewestFirst[i] * w;
    wt += w;
  }
  return wt > 0 ? Math.round(ws / wt) : fallback;
}

function computeAverages(days: string[], config: RecalcConfig) {
  const periods = groupPeriods(days);
  if (periods.length === 0) {
    return { cycleLength: config.defaultCycleLength, periodDuration: config.defaultPeriodDuration };
  }
  const durations: number[] = [];
  for (let i = periods.length - 1; i >= 0; i--) durations.push(periods[i].length);
  const intervals: number[] = [];
  for (let i = periods.length - 1; i >= 1; i--) intervals.push(daysBetween(periods[i - 1].start, periods[i].start));
  return {
    cycleLength: weightedAvg(intervals, config.weights, config.tailWeight, config.defaultCycleLength),
    periodDuration: weightedAvg(durations, config.weights, config.tailWeight, config.defaultPeriodDuration)
  };
}

function Calendar({ cycles, onCommitted, userId }: CalendarProps) {
  const [currentMonth, setCurrentMonth] = useState(new Date());
  const [predictions, setPredictions] = useState<Prediction[]>([]);
  const [config, setConfig] = useState<RecalcConfig>(DEFAULT_CONFIG);
  const [draft, setDraft] = useState<Draft>({
    days: [],
    cycleLength: DEFAULT_CONFIG.defaultCycleLength,
    periodDuration: DEFAULT_CONFIG.defaultPeriodDuration,
    dirty: false
  });
  const [autoUpdate, setAutoUpdate] = useState(getAutoUpdate);
  const [recalculating, setRecalculating] = useState(false);
  const [error, setError] = useState('');

  // Days that came from auto-filled (elapsed-forecast) cycles, for the marker.
  const autoDays = new Set(cyclesToDays(cycles.filter(c => c.auto)));

  const fetchPredictions = useCallback(async () => {
    try {
      const res = await fetch(`/api/user/${userId}/predictions`);
      if (res.ok) setPredictions(await res.json());
    } catch (e) {
      console.error('Error fetching predictions:', e);
    }
  }, [userId]);

  // One-time init: config + predictions.
  useEffect(() => {
    fetch('/api/config')
      .then(r => (r.ok ? r.json() : null))
      .then(c => c && setConfig(c))
      .catch(() => {});
    void fetchPredictions();
  }, [userId, fetchPredictions]);

  // Seed the draft from the DB actuals (and the fields from their average),
  // unless an unsaved (dirty) draft exists.
  useEffect(() => {
    const stored = getDraft(userId);
    if (stored && stored.dirty) {
      setDraft(stored);
      return;
    }
    const days = cyclesToDays(cycles);
    const avg = computeAverages(days, config);
    setDraft({ days, cycleLength: avg.cycleLength, periodDuration: avg.periodDuration, dirty: false });
  }, [cycles, userId, config]);

  // Live auto-update: when on, the fields track the painted calendar.
  useEffect(() => {
    if (!autoUpdate) return;
    const avg = computeAverages(draft.days, config);
    setDraft(prev =>
      prev.cycleLength === avg.cycleLength && prev.periodDuration === avg.periodDuration
        ? prev
        : { ...prev, cycleLength: avg.cycleLength, periodDuration: avg.periodDuration }
    );
  }, [draft.days, autoUpdate, config]);

  // Persist the draft whenever it changes (functional updates below stay race-free).
  useEffect(() => {
    saveDraft(userId, draft);
  }, [draft, userId]);

  useEffect(() => {
    saveAutoUpdate(autoUpdate);
  }, [autoUpdate]);

  const toggleDay = (iso: string) => {
    setDraft(prev => {
      const has = prev.days.includes(iso);
      const days = has ? prev.days.filter(d => d !== iso) : [...prev.days, iso].sort();
      return { ...prev, days, dirty: true };
    });
  };

  const setField = (key: 'cycleLength' | 'periodDuration', value: number) => {
    setDraft(prev => ({ ...prev, [key]: value, dirty: true }));
  };

  const handleRecalculate = async () => {
    setError('');

    // Past is fine, but periods can't be committed more than 3 days ahead.
    const cutoff = addDaysIso(isoDate(new Date()), 3);
    const tooFar = draft.days.filter(d => d > cutoff).sort();
    if (tooFar.length > 0) {
      setError(
        `Can't save periods more than 3 days in the future (through ${cutoff}). ` +
        `Remove: ${tooFar.join(', ')}.`
      );
      return;
    }

    // In manual mode the fields are user input, so warn on out-of-range values.
    if (!autoUpdate) {
      const cl = draft.cycleLength, pd = draft.periodDuration;
      const bad =
        cl < config.cycleLengthMin || cl > config.cycleLengthMax ||
        pd < config.periodDurationMin || pd > config.periodDurationMax;
      if (bad) {
        const ok = window.confirm(
          `Those values are outside the usual range ` +
          `(cycle ${config.cycleLengthMin}-${config.cycleLengthMax} days, ` +
          `period ${config.periodDurationMin}-${config.periodDurationMax} days).\n\nAre you sure?`
        );
        if (!ok) return;
      }
    }

    setRecalculating(true);
    try {
      const res = await fetch(`/api/user/${userId}/recalculate`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          days: draft.days,
          cycleLength: draft.cycleLength,
          periodDuration: draft.periodDuration
        })
      });
      if (!res.ok) throw new Error('Recalculation failed');

      const data = await res.json();
      setPredictions(data.forecast);
      setDraft({
        days: cyclesToDays(data.cycles),
        cycleLength: data.cycleLength,
        periodDuration: data.periodDuration,
        dirty: false
      });
      onCommitted();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Recalculation failed');
    } finally {
      setRecalculating(false);
    }
  };

  // --- calendar grid ---
  const daysInMonth = new Date(currentMonth.getFullYear(), currentMonth.getMonth() + 1, 0).getDate();
  const firstDay = new Date(currentMonth.getFullYear(), currentMonth.getMonth(), 1).getDay();

  const predictionIndexFor = (iso: string) =>
    predictions.findIndex(p => spanDays(p.predictedStart.slice(0, 10), p.predictedDuration).includes(iso));

  const tierClass = (index: number) => (index === 0 ? 'tier-0' : index === 1 ? 'tier-1' : 'tier-2');

  const renderDays = () => {
    const cells = [];
    for (let i = 0; i < firstDay; i++) {
      cells.push(<div key={`empty-${i}`} className="calendar-day empty" />);
    }

    for (let day = 1; day <= daysInMonth; day++) {
      const date = new Date(currentMonth.getFullYear(), currentMonth.getMonth(), day);
      const iso = isoDate(date);
      const isActual = draft.days.includes(iso);
      const isAuto = isActual && autoDays.has(iso);
      const predIndex = isActual ? -1 : predictionIndexFor(iso);

      cells.push(
        <div key={day} className="calendar-day" onClick={() => toggleDay(iso)}>
          <span className="day-number">{day}</span>
          {isActual && (
            <div className={`period-mark actual${isAuto ? ' auto' : ''}`}>
              <BloodDropIcon />
              {isAuto && <span className="calc-badge" title="Auto-filled">✎</span>}
            </div>
          )}
          {predIndex >= 0 && (
            <div className={`period-mark predicted ${tierClass(predIndex)}`}>
              <BloodDropIcon />
              <span className="calc-badge" title="Calculated">∿</span>
            </div>
          )}
        </div>
      );
    }
    return cells;
  };

  const monthName = currentMonth.toLocaleString('default', { month: 'long', year: 'numeric' });

  return (
    <div className="calendar">
      <div className="recalc-bar">
        <div className="recalc-controls">
          <div className="recalc-field">
            <label htmlFor="cycleLength">Cycle length</label>
            <div className="recalc-input-row">
              <input
                id="cycleLength"
                type="number"
                min={1}
                value={draft.cycleLength}
                onChange={e => setField('cycleLength', Number(e.target.value))}
              />
              <span className="recalc-unit">days</span>
            </div>
          </div>
          <div className="recalc-field">
            <label htmlFor="periodDuration">Period duration</label>
            <div className="recalc-input-row">
              <input
                id="periodDuration"
                type="number"
                min={1}
                value={draft.periodDuration}
                onChange={e => setField('periodDuration', Number(e.target.value))}
              />
              <span className="recalc-unit">days</span>
            </div>
          </div>
          <button className="recalc-button" onClick={handleRecalculate} disabled={recalculating}>
            {recalculating ? 'Recalculating…' : 'Recalculate'}
          </button>
          {draft.dirty && <span className="unsaved-badge" title="Unsaved changes">●&nbsp;Unsaved</span>}
        </div>
        <label className="auto-update-toggle">
          <input
            type="checkbox"
            checked={autoUpdate}
            onChange={e => setAutoUpdate(e.target.checked)}
          />
          Auto-update from calendar
        </label>
      </div>

      {error && <div className="recalc-error">{error}</div>}

      <div className="calendar-header">
        <button onClick={() => setCurrentMonth(new Date(currentMonth.getFullYear(), currentMonth.getMonth() - 1))}>
          ← Previous
        </button>
        <h2>{monthName}</h2>
        <button onClick={() => setCurrentMonth(new Date(currentMonth.getFullYear(), currentMonth.getMonth() + 1))}>
          Next →
        </button>
      </div>

      <div className="calendar-weekdays">
        <div>Sun</div><div>Mon</div><div>Tue</div><div>Wed</div><div>Thu</div><div>Fri</div><div>Sat</div>
      </div>

      <div className="calendar-body">
        <div className="calendar-grid">{renderDays()}</div>
        {recalculating && (
          <div className="calendar-loading">
            <div className="spinner" />
            <span>Recalculating…</span>
          </div>
        )}
      </div>

      <div className="calendar-legend">
        <div className="legend-item period-mark actual"><BloodDropIcon /> Actual Period</div>
        <div className="legend-item period-mark tier-0"><BloodDropIcon /> Next Predicted</div>
        <div className="legend-item period-mark tier-2"><BloodDropIcon /> Future Predicted</div>
        <div className="legend-item"><span className="calc-badge inline">✎</span> Auto-filled</div>
      </div>
    </div>
  );
}

export default Calendar;
