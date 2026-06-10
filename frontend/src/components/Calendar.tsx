import { useState, useEffect, useCallback } from 'react';
import '../styles/calendar.css';
import BloodDropIcon from './BloodDropIcon';
import { getDraft, saveDraft, Draft } from '../lib/storage';

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
  const [recalculating, setRecalculating] = useState(false);
  const [error, setError] = useState('');
  // Track whether the user manually changed a field. If not, recalc sends null so
  // the backend uses the weighted average (spec: override wins, else weighted).
  const [edited, setEdited] = useState({ cycleLength: false, periodDuration: false });

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

  // One-time init: config + predictions + seed the fields from current averages.
  useEffect(() => {
    fetch('/api/config')
      .then(r => (r.ok ? r.json() : null))
      .then(c => c && setConfig(c))
      .catch(() => {});

    fetch(`/api/user/${userId}/stats`)
      .then(r => (r.ok ? r.json() : null))
      .then(s => {
        if (!s) return;
        const stored = getDraft(userId);
        if (stored && stored.dirty) return; // keep unsaved edits
        setDraft(prev => ({
          ...prev,
          // averageInterval = cycle length, averageCycleLength = period duration
          cycleLength: s.averageInterval > 0 ? Math.round(s.averageInterval) : prev.cycleLength,
          periodDuration: s.averageCycleLength > 0 ? Math.round(s.averageCycleLength) : prev.periodDuration
        }));
      })
      .catch(() => {});

    void fetchPredictions();
  }, [userId, fetchPredictions]);

  // Seed the draft from the DB actuals, unless an unsaved (dirty) draft exists.
  useEffect(() => {
    const stored = getDraft(userId);
    if (stored && stored.dirty) {
      setDraft(stored);
      return;
    }
    setDraft(prev => ({
      ...prev,
      days: cyclesToDays(cycles),
      dirty: false
    }));
  }, [cycles, userId]);

  // Persist the draft whenever it changes (functional updates below stay race-free).
  useEffect(() => {
    saveDraft(userId, draft);
  }, [draft, userId]);

  const toggleDay = (iso: string) => {
    setDraft(prev => {
      const has = prev.days.includes(iso);
      const days = has ? prev.days.filter(d => d !== iso) : [...prev.days, iso].sort();
      return { ...prev, days, dirty: true };
    });
  };

  const setField = (key: 'cycleLength' | 'periodDuration', value: number) => {
    setDraft(prev => ({ ...prev, [key]: value, dirty: true }));
    setEdited(prev => ({ ...prev, [key]: true }));
  };

  const handleRecalculate = async () => {
    setError('');

    // Only warn about values the user actually typed (spec: "if a value is too big or small").
    const cl = edited.cycleLength ? draft.cycleLength : null;
    const pd = edited.periodDuration ? draft.periodDuration : null;
    const clBad = cl !== null && (cl < config.cycleLengthMin || cl > config.cycleLengthMax);
    const pdBad = pd !== null && (pd < config.periodDurationMin || pd > config.periodDurationMax);

    if (clBad || pdBad) {
      const ok = window.confirm(
        `Those values are outside the usual range ` +
        `(cycle ${config.cycleLengthMin}-${config.cycleLengthMax} days, ` +
        `period ${config.periodDurationMin}-${config.periodDurationMax} days).\n\nAre you sure?`
      );
      if (!ok) return;
    }

    setRecalculating(true);
    try {
      const res = await fetch(`/api/user/${userId}/recalculate`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          days: draft.days,
          cycleLength: cl,        // null → backend uses weighted average
          periodDuration: pd
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
      setEdited({ cycleLength: false, periodDuration: false });
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

  const predictionColor = (index: number) => {
    if (index === 0) return '#ff6b6b';
    if (index === 1) return '#ffb86b';
    return '#ffd966';
  };

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
              <BloodDropIcon color="#ff6b6b" />
              {isAuto && <span className="calc-badge" title="Auto-filled">✎</span>}
            </div>
          )}
          {predIndex >= 0 && (
            <div className="period-mark predicted" style={{ color: predictionColor(predIndex) }}>
              <BloodDropIcon color={predictionColor(predIndex)} />
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
        <div className="recalc-field">
          <label htmlFor="cycleLength">Cycle length</label>
          <input
            id="cycleLength"
            type="number"
            min={1}
            value={draft.cycleLength}
            onChange={e => setField('cycleLength', Number(e.target.value))}
          />
          <span className="recalc-unit">days</span>
        </div>
        <div className="recalc-field">
          <label htmlFor="periodDuration">Period duration</label>
          <input
            id="periodDuration"
            type="number"
            min={1}
            value={draft.periodDuration}
            onChange={e => setField('periodDuration', Number(e.target.value))}
          />
          <span className="recalc-unit">days</span>
        </div>
        <button className="recalc-button" onClick={handleRecalculate} disabled={recalculating}>
          {recalculating ? 'Recalculating…' : 'Recalculate'}
        </button>
        {draft.dirty && <span className="unsaved-badge" title="Unsaved changes">●&nbsp;Unsaved</span>}
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
        <div className="legend-item"><BloodDropIcon color="#ff6b6b" /> Actual Period</div>
        <div className="legend-item"><BloodDropIcon color="#ffb86b" /> Next Predicted</div>
        <div className="legend-item"><BloodDropIcon color="#ffd966" /> Future Predicted</div>
        <div className="legend-item"><span className="calc-badge inline">✎</span> Auto-filled</div>
      </div>
    </div>
  );
}

export default Calendar;
