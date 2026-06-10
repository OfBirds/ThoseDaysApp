# Recalculation Model — Spec

> Status: **draft, pre-implementation.** Extends the original functional spec in
> [`plan.txt`](../plan.txt). No code written against this yet.

## Glossary (kills the naming confusion)

| Term | Meaning | Typical | Drives |
|------|---------|---------|--------|
| **Cycle length** | gap from one period's start to the next start | ~28 days | *where* the next period lands |
| **Period duration** | number of bleeding days in one period | ~5 days | *how many* days each future period paints |

The field next to the Recalculate button is **cycle length** (start-to-start) —
it's the value that projects the forecast forward.

## 1. Draft vs. committed state

Two states for the user's data:

- **Draft (frontend-only):** painting/erasing period days and editing the two
  number fields live in **browser storage only** (localStorage, survives refresh).
  Nothing hits the API. UI shows an "unsaved changes" indicator.
- **Committed (DB):** pressing **Recalculate** is the *only* action that writes to
  the database. It commits the actual period days, recomputes the two averages,
  regenerates the forward forecast, and returns it to paint.

This applies to new edits **and** edits of already-saved data — to confirm any
change, the user runs Recalculate again. **No recalc → no future periods.**

### Draft storage (frontend)

- **Mechanism: `localStorage`** — survives refresh (nothing lost until
  Recalculate), synchronous, tiny payload. Consistent with the app, which already
  uses `localStorage` for `token`/`user`. *Not* `sessionStorage` (wiped on tab
  close), `IndexedDB` (overkill; reserved for the PWA offline cache of committed
  data), or in-memory React state (lost on refresh).
- **Keys are namespaced per user** so two accounts on one browser don't collide:

  | Key | Value |
  |-----|-------|
  | `tda:draft:<userId>` | `{ days: string[], cycleLength: number, periodDuration: number, dirty: boolean }` |
  | `tda:theme` | `"light"` \| `"dark"` |

  `days` are ISO `YYYY-MM-DD` local-date strings. Existing un-namespaced
  `token`/`user` keys are left as-is; new keys use the `tda:` prefix.
- **One storage helper module** (`frontend/src/lib/storage.ts`) wraps
  get/set/clear so the mechanism is centralized and swappable — if we outgrow
  `localStorage`, only that file changes.

## 2. The control bar

1. **Recalculate button** — commits draft → DB, runs the calc, paints the future.
2. **Cycle length field** — auto-filled from the weighted average; user-editable.
3. **Period duration field** — auto-filled from the weighted average; user-editable.

## 3. Tunable config (CHANGE HERE)

All formula constants live in **one** config object so they can be adjusted
without touching logic. Backend is source of truth; frontend mirrors it (ideally
served from a `GET /api/config` endpoint so there's a single definition).

```jsonc
{
  // Weighted average: how strongly recent cycles count.
  // weights[0] applies to the most recent value, weights[1] to the next, etc.
  // Any value older than weights.length uses `tailWeight`.
  "weights": [3, 2, 1],
  "tailWeight": 1,

  // Fallbacks when there isn't enough history.
  "defaultCycleLength": 28,
  "defaultPeriodDuration": 5,

  // Outlier confirmation ("are you sure?") — inclusive normal ranges.
  "cycleLengthMin": 21,  "cycleLengthMax": 35,
  "periodDurationMin": 2, "periodDurationMax": 10,

  // How many future periods to project.
  "forecastCount": 15
}
```

## 4. Weighted average formula

Recent-favored weighted mean, rounded to a whole number.

```
weightFor(i)  // i = 0 is the most recent value
  = weights[i]            if i < weights.length
  = tailWeight            otherwise

weightedAvg(values)       // values ordered newest → oldest
  = round( Σ value[i]·weightFor(i)  /  Σ weightFor(i) )
```

Applied independently to the list of past **cycle lengths** and past **period
durations**. Empty/insufficient history → the `default*` fallback.

> Easily changeable: swap the `weights` array (e.g. `[5,3,2]`, or `[1]` for a
> flat mean) — no code change needed.

## 5. Outlier confirmation

If the user types a value outside the configured normal range, show an
**"Are you sure?"** confirm before accepting it.

- Confirm → use the entered value.
- Cancel → revert the field to its previous value.

## 6. Recalculate flow

1. Validate both fields → if out of range, "Are you sure?".
2. Commit the painted period days to the DB (the actuals).
3. Compute weighted cycle length + duration — unless the user overrode either
   field, in which case the override wins.
4. Generate the forecast: `next_start = last_actual_start + cycleLength`,
   repeated `forecastCount` times; each future period paints `duration` days.
5. Return forecast; frontend paints it.

## 7. Auto-painting

The user never has to hand-paint the forecast — Recalculate always generates and
paints it. "If a user forgets, it's painted as automatic."

## 8. Visual encoding

- **Actual periods:** red blood-drop (current).
- **Forecast periods:** distinct **color tier** *plus* a **"calculated" marker**
  (small symbol on the drop) so it's never color-only (WCAG AA). Near vs. far
  reuse the existing `#ffb86b` / `#ffd966` tiers.

## 9. Recalculation loading state

Recalculate commits to the DB and regenerates the forecast, so it is not
instant. While it runs, the UI must show that **something is happening**:

- The **Recalculate button** enters a busy state (disabled + spinner/label like
  "Recalculating…") so it can't be double-fired.
- A **loading overlay** covers the calendar/forecast area while the request is in
  flight, so the user doesn't read stale marks as the new result.
- On success the overlay clears and the freshly painted forecast appears; on
  error, show a message and leave the prior state intact.

Applies to any reconcile that does real work too (e.g. the app-load catch-up in
§11 if it has overdue forecasts to process).

## 10. Theme — dark / light toggle

No theme system exists today: `index.css` has a single light `:root`. Add:

- A **dark/light toggle button** (in the top header, near Logout).
- A **dark palette** as a `[data-theme="dark"]` override of the existing CSS
  variables in `index.css` (`--neutral`, `--text`, `--text-light`, `--border`,
  page background). The brand `--primary` / `--accent` stay; only surfaces and
  text invert.
- Persist the choice in **localStorage**; default to the OS preference via
  `prefers-color-scheme` on first load.
- Toggle works by setting `data-theme` on `<html>`; all components already use
  the CSS variables, so no per-component changes are needed.

## 11. Reconcile — marking forecasts as "passed"

*(This is the river we're now crossing. Proposal below; open decisions flagged.)*

When a forecast period's start date moves into the past, it becomes **real**.

**Model — "auto-marked, but real" (DECIDED):**
A forecast whose date has passed is **auto-converted into an actual `Cycle`**,
flagged `Auto = true`. It is real data: it **counts toward the weighted average**
like any other actual. The `Auto` flag only means "we filled this in for you" —
it drives a distinct marker on the calendar and lets the user spot entries they
never hand-confirmed. The user can **edit + Recalculate** any auto entry if
reality differed; editing clears the `Auto` flag (it becomes user-confirmed).

Rationale: the user can always correct these, so treating them as real keeps the
cadence and the average continuous without forcing a confirmation step.

- **When does it flip?** At **local midnight** — comparison uses the user's local
  date (`predictedStart.Date < localToday`), not server UTC, to avoid off-by-one.
- **Service was offline / user away for weeks?** Reconcile is a **catch-up loop,
  not a single step.** It walks every overdue forecast in chronological order and
  flips each to `elapsed`, in one pass. Idempotent — re-running is a no-op. So a
  3-month gap is resolved the next time reconcile runs; the app is "current"
  afterward.
- **Trigger — only on Recalculate?** **No.** Recommended triggers:
  1. **App load / login** (cheap, idempotent) — primary; covers the user who
     never clicks Recalculate.
  2. **Recalculate** (already commits + regenerates).
  3. *(Phase 2)* nightly cron at local midnight, for notifications.

  Reconcile-on-load is what makes "forgot to do it → painted as automatic" work
  without requiring a click.

### Decisions

- **D1. DECIDED** — Elapsed forecasts auto-convert into actual `Cycle`s flagged
  `Auto = true` on flip.
- **D2. DECIDED** — Auto entries are real and **feed the weighted average**.
  Editing an auto entry clears the `Auto` flag (becomes user-confirmed).
- **D3. Open** — Background cron in scope now, or is app-load + recalc reconcile
  enough for phase 1? (Recommended: app-load + recalc; cron later.)

### Data-model impact

- `Cycle` gains an **`Auto`** boolean (default `false`). Set `true` on
  auto-conversion, cleared on user edit. Distinct from the existing `Corrected`.
- Forecast/`Prediction` rows whose date passes are removed as forecasts and
  written as `Auto` `Cycle`s during reconcile.

## 12. Deferred ("not yet crossed")

- Confidence scoring, notifications.
- Background cron reconcile (§11, D3) — deferred to phase 2.
