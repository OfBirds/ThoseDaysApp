import { useState, useEffect, useCallback } from 'react';
import '../styles/calendar.css';
import BloodDropIcon from './BloodDropIcon';
import { getDraft, saveDraft, getAutoUpdate, saveAutoUpdate, type Draft } from '../lib/storage';
import {
  isoDate, addDaysIso, daysBetween, spanDays, groupPeriods,
  computeAverages, findNextPrediction, predictionTier,
  isOutOfRange, findFutureDays, stdDevOf, predictionWindow, type RecalcConfig
} from '../lib/predictions';

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

interface NextPeriod {
  startIso: string;
  daysUntil: number;
  rangeLabel: string | null;
}

const SHORT_DATE = (iso: string) =>
  new Date(iso + 'T00:00:00').toLocaleDateString(undefined, { weekday: 'short', day: 'numeric' });

interface CalendarProps {
  cycles: Cycle[];
  onCommitted: () => void;
  userId: string;
  onNextPeriod?: (info: NextPeriod | null) => void;
}

const DEFAULT_CONFIG: RecalcConfig = {
  weights: [3, 2, 1],
  tailWeight: 1,
  defaultCycleLength: 28,
  defaultPeriodDuration: 5,
  cycleLengthMin: 21,
  cycleLengthMax: 35,
  periodDurationMin: 2,
  periodDurationMax: 10,
  confidenceFloor: 0.3,
  confidenceNominal: 0.7,
  confidenceMinIntervals: 2,
  bandK: 1
};

function cyclesToDays(cycles: Cycle[]): string[] {
  const set = new Set<string>();
  cycles.forEach(c => spanDays(c.startDate.slice(0, 10), c.durationDays).forEach(d => set.add(d)));
  return [...set].sort();
}

function Calendar({ cycles, onCommitted, userId, onNextPeriod }: CalendarProps) {
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
  const [recalcError, setRecalcError] = useState('');
  const [msgOpen, setMsgOpen] = useState(false);

  // Days that came from auto-filled (elapsed-forecast) cycles, for the marker.
  const autoDays = new Set(cyclesToDays(cycles.filter(c => c.auto)));

  // Today and next prediction — now using findNextPrediction from predictions.ts.
  const todayIso = isoDate(new Date());
  const nextPeriod = findNextPrediction(predictions, todayIso);
  const nextStartIso = nextPeriod?.startIso ?? null;
  const daysUntilNext = nextPeriod?.daysUntil ?? null;

  // Soft range around the next predicted start, from how regular recent cycles are.
  // Only shown when the spread is wide enough to be worth mentioning (> 1 day).
  const committedDays = cyclesToDays(cycles);
  const committedIntervals = (() => {
    const periods = groupPeriods(committedDays);
    const out: number[] = [];
    for (let i = 1; i < periods.length; i++) out.push(daysBetween(periods[i - 1].start, periods[i].start));
    return out;
  })();
  const nextRangeLabel = (() => {
    if (!nextStartIso) return null;
    const sigma = stdDevOf(committedIntervals);
    const { earliest, latest, halfWidth } = predictionWindow(nextStartIso, 1, sigma, config);
    return halfWidth >= 2 ? `${SHORT_DATE(earliest)} – ${SHORT_DATE(latest)}` : null;
  })();

  // Surface the next-period info to the parent (for the status bar).
  useEffect(() => {
    onNextPeriod?.(
      nextStartIso && daysUntilNext !== null
        ? { startIso: nextStartIso, daysUntil: daysUntilNext, rangeLabel: nextRangeLabel }
        : null
    );
  }, [nextStartIso, daysUntilNext, nextRangeLabel, onNextPeriod]);

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
    setRecalcError(''); // editing clears a stale recalc error; live checks take over
    setDraft(prev => {
      const has = prev.days.includes(iso);
      const days = has ? prev.days.filter(d => d !== iso) : [...prev.days, iso].sort();
      return { ...prev, days, dirty: true };
    });
  };

  const setField = (key: 'cycleLength' | 'periodDuration', value: number) => {
    setRecalcError('');
    setDraft(prev => ({ ...prev, [key]: value, dirty: true }));
  };

  // --- live validation: uses predictions.ts helpers ---
  const tooFarDays = findFutureDays(draft.days, todayIso, 3);
  const outOfRange = !autoUpdate && isOutOfRange(draft.cycleLength, draft.periodDuration, config);

  const activeMessage: { text: string; severity: 'error' | 'warning' } | null = recalcError
    ? { text: recalcError, severity: 'error' }
    : tooFarDays.length > 0
    ? {
        text:
          `Can't save periods more than 3 days in the future (through ${addDaysIso(todayIso, 3)}). ` +
          `Remove: ${tooFarDays.join(', ')}.`,
        severity: 'error'
      }
    : outOfRange
    ? {
        text:
          `Cycle/period values are outside the usual range ` +
          `(cycle ${config.cycleLengthMin}–${config.cycleLengthMax} days, ` +
          `period ${config.periodDurationMin}–${config.periodDurationMax} days).`,
        severity: 'warning'
      }
    : null;

  const handleRecalculate = async () => {
    if (tooFarDays.length > 0) return;
    setRecalcError('');
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
      setRecalcError(e instanceof Error ? e.message : 'Recalculation failed');
    } finally {
      setRecalculating(false);
    }
  };

  // --- calendar grid ---
  const daysInMonth = new Date(currentMonth.getFullYear(), currentMonth.getMonth() + 1, 0).getDate();
  const firstDay = new Date(currentMonth.getFullYear(), currentMonth.getMonth(), 1).getDay();

  // Use predictionTier from predictions.ts.
  const tierClass = (index: number) => (index === 0 ? 'tier-0' : 'tier-1');

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
      const predTier = isActual ? -1 : predictionTier(iso, predictions);
      const isToday = iso === todayIso;
      const isNextStart = iso === nextStartIso;

      cells.push(
        <div
          key={day}
          className={`calendar-day${isToday ? ' is-today' : ''}`}
          onClick={() => toggleDay(iso)}
        >
          <span className="day-number">{day}</span>
          {isNextStart && daysUntilNext !== null && (
            <span
              className="day-countdown"
              title={`${daysUntilNext} day${daysUntilNext === 1 ? '' : 's'} until your next period`}
            >
              {daysUntilNext === 0 ? 'today' : `${daysUntilNext}d`}
            </span>
          )}
          {isActual && (
            <div className={`period-mark actual${isAuto ? ' auto' : ''}`}>
              <BloodDropIcon />
              {isAuto && <span className="calc-badge" title="Auto-filled">✎</span>}
            </div>
          )}
          {predTier >= 0 && (
            <div className={`period-mark predicted ${tierClass(predTier)}`}>
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
  const now = new Date();
  const isCurrentMonth =
    currentMonth.getFullYear() === now.getFullYear() && currentMonth.getMonth() === now.getMonth();

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
          <div
            className={`recalc-msg${activeMessage ? ` active ${activeMessage.severity}` : ''}${msgOpen ? ' open' : ''}`}
            onMouseLeave={() => setMsgOpen(false)}
          >
            <button
              type="button"
              className="recalc-msg-icon"
              onClick={() => setMsgOpen(o => !o)}
              aria-label="Show validation message"
              tabIndex={activeMessage ? 0 : -1}
            >
              !
            </button>
            {activeMessage && <div className="recalc-popover" role="alert">{activeMessage.text}</div>}
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

      <div className="calendar-header">
        <button onClick={() => setCurrentMonth(new Date(currentMonth.getFullYear(), currentMonth.getMonth() - 1))}>
          ← Previous
        </button>
        <div className="calendar-title">
          <h2>{monthName}</h2>
          <button
            className={`today-button${isCurrentMonth ? ' is-hidden' : ''}`}
            onClick={() => setCurrentMonth(new Date())}
            tabIndex={isCurrentMonth ? -1 : 0}
            aria-hidden={isCurrentMonth}
          >
            ↩ Today
          </button>
        </div>
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
        <div className="legend-item period-mark tier-1"><BloodDropIcon /> Future Predicted</div>
        <div className="legend-item"><span className="calc-badge inline">✎</span> Auto-filled</div>
      </div>
    </div>
  );
}

export default Calendar;
