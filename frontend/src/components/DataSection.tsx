import { useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { addDaysIso } from '../lib/predictions';
import {
  parseImport, reviewImport, importWindow,
  type ImportDoc, type ImportReview, type ExistingCycle,
} from '../lib/importData';
import { savePendingImport } from '../lib/storage';

interface CycleResponse {
  startDate: string;
  durationDays: number;
}

const dayOf = (iso: string) => iso.slice(0, 10);
const pretty = (iso: string) =>
  new Date(iso + 'T00:00:00').toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' });

type ExportScope = 'full' | 'patch';

export default function DataSection({ userId }: { userId: string }) {
  const [cycles, setCycles] = useState<CycleResponse[]>([]);
  const [scope, setScope] = useState<ExportScope>('full');
  const [count, setCount] = useState(3);

  // Import flow state.
  const [pendingDoc, setPendingDoc] = useState<ImportDoc | null>(null);
  const [review, setReview] = useState<ImportReview | null>(null);
  const [importError, setImportError] = useState<string | null>(null);
  const [staged, setStaged] = useState(false);
  const fileRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    fetch(`/api/user/${userId}/cycles`)
      .then((r) => (r.ok ? r.json() : []))
      .then((c: CycleResponse[]) => setCycles([...c].sort((a, b) => dayOf(a.startDate).localeCompare(dayOf(b.startDate)))))
      .catch(() => {});
  }, [userId]);

  const existing: ExistingCycle[] = cycles.map((c) => ({ startDate: dayOf(c.startDate), durationDays: c.durationDays }));

  // Date range the current export selection covers.
  const exportRange = (() => {
    if (cycles.length === 0) return null;
    const selected = scope === 'full' ? cycles : cycles.slice(Math.max(0, cycles.length - count));
    if (selected.length === 0) return null;
    const first = selected[0];
    const last = selected[selected.length - 1];
    return {
      start: dayOf(first.startDate),
      end: addDaysIso(dayOf(last.startDate), Math.max(1, last.durationDays) - 1),
      n: selected.length,
    };
  })();

  const doExport = () => {
    const url =
      scope === 'patch' ? `/api/user/${userId}/export?cycles=${count}` : `/api/user/${userId}/export`;
    const a = document.createElement('a');
    a.href = url;
    a.download = '';
    document.body.appendChild(a);
    a.click();
    a.remove();
  };

  const onFile = async (file: File) => {
    setImportError(null);
    setStaged(false);
    const text = await file.text();
    const result = parseImport(text);
    if ('error' in result) {
      setImportError(result.error);
      setPendingDoc(null);
      setReview(null);
      return;
    }
    setPendingDoc(result.doc);
    setReview(reviewImport(result.doc, existing));
  };

  const confirmStage = () => {
    if (!pendingDoc) return;
    const win = importWindow(pendingDoc.cycles);
    savePendingImport(userId, {
      cycles: pendingDoc.cycles.map((c) => ({
        startDate: dayOf(c.startDate),
        durationDays: c.durationDays,
        corrected: c.corrected,
        auto: c.auto,
        predictedStart: c.predictedStart ?? null,
      })),
      range: win,
      schemaVersion: pendingDoc.schemaVersion,
    });
    setStaged(true);
    setPendingDoc(null);
    setReview(null);
    if (fileRef.current) fileRef.current.value = '';
  };

  const cancelImport = () => {
    setPendingDoc(null);
    setReview(null);
    setImportError(null);
    if (fileRef.current) fileRef.current.value = '';
  };

  return (
    <section className="settings-section" aria-labelledby="settings-data-heading">
      <h2 id="settings-data-heading" className="settings-section-title">Your data</h2>

      {/* Export */}
      <div className="data-block">
        <h3 className="data-subtitle">Export</h3>
        <p className="settings-row-help">
          Download a copy of your history. This file contains your cycle dates — keep it somewhere safe.
        </p>
        <div className="data-export-controls">
          <label title="Save your whole history to a file.">
            <input type="radio" name="export-scope" checked={scope === 'full'} onChange={() => setScope('full')} />
            Full history
          </label>
          <label title="Save just your most recent cycles. We'll show you exactly which dates that covers.">
            <input type="radio" name="export-scope" checked={scope === 'patch'} onChange={() => setScope('patch')} />
            Last
            <input
              type="number"
              min={1}
              value={count}
              disabled={scope !== 'patch'}
              onChange={(e) => setCount(Math.max(1, Number(e.target.value) || 1))}
              aria-label="Number of cycles to export"
            />
            cycles
          </label>
        </div>
        {exportRange && (
          <p className="data-range" role="status">
            This exports <strong>{exportRange.n}</strong> cycle{exportRange.n === 1 ? '' : 's'}:{' '}
            <strong>{pretty(exportRange.start)}</strong> → <strong>{pretty(exportRange.end)}</strong>.
          </p>
        )}
        <button className="data-button" onClick={doExport} disabled={cycles.length === 0}>
          Export
        </button>
      </div>

      {/* Import */}
      <div className="data-block">
        <h3 className="data-subtitle">Import</h3>
        <p className="settings-row-help">
          Bring a file back in. It updates only the dates the file covers — your history outside those
          dates stays exactly as it is. Nothing is saved until you review it and save it yourself.
        </p>
        <input
          ref={fileRef}
          type="file"
          accept="application/json,.json"
          onChange={(e) => e.target.files?.[0] && onFile(e.target.files[0])}
          aria-label="Choose a file to import"
        />

        {importError && <p className="settings-error" role="alert">{importError}</p>}

        {review && (
          <div className="import-review" role="group" aria-label="Review this import">
            <p>
              This will update your history from <strong>{pretty(review.rangeStart)}</strong> to{' '}
              <strong>{pretty(review.rangeEnd)}</strong> (<strong>{review.importedCount}</strong>{' '}
              cycle{review.importedCount === 1 ? '' : 's'}). Your history <strong>before</strong> and{' '}
              <strong>after</strong> these dates stays exactly as it is.
            </p>
            <ul className="import-review-list">
              {review.replacedCount > 0 && (
                <li><strong>{review.replacedCount}</strong> cycle{review.replacedCount === 1 ? '' : 's'} in that range will be replaced.</li>
              )}
              {review.leadingGap === null
                ? <li>This is the earliest history on record.</li>
                : <li>There's a <strong>{review.leadingGap}-day</strong> gap between your earlier history and this import.</li>}
              {review.trailingGap === null
                ? <li>There's no recorded history after this import.</li>
                : <li>There's a <strong>{review.trailingGap}-day</strong> gap after this import until your next recorded period.</li>}
            </ul>
            <p className="import-review-note">
              <strong>Nothing is saved yet.</strong> We'll put it on your calendar so you can look it over
              first — and even after you save, you can still edit your history later.
            </p>
            <div className="import-review-actions">
              <button className="data-button secondary" onClick={cancelImport}>Cancel</button>
              <button className="data-button" onClick={confirmStage}>I understand — show it to me</button>
            </div>
          </div>
        )}

        {staged && (
          <p className="data-range" role="status">
            Loaded onto your calendar — not saved yet. Open the{' '}
            <Link to="/">Calendar</Link> to review it, then click <strong>Save this history permanently</strong>.
          </p>
        )}
      </div>
    </section>
  );
}
