# UI updates — calendar today marker, countdown, return-to-month

> Status: **planning / draft**. No code yet. Four small calendar/status-bar
> improvements on branch `ui_updates_2`.

## The four changes

1. **Mark today** on the calendar — there's currently no indication of the
   current date.
2. **Countdown badge** in the **lower-left** of the next period's **first day
   only** (not every day of it) — e.g. `5d`.
3. **"Next period in N days"** in the bottom status bar.
4. **"Return to current month"** button — easy to get lost when paging into the
   future.

## Components involved

- `frontend/src/components/Calendar.tsx` — grid, `predictions`, `currentMonth`.
- `frontend/src/components/StatusBar.tsx` — bottom bar (needs next-period data).
- `frontend/src/App.tsx` — parent of both (where we lift next-period info).
- `frontend/src/styles/calendar.css`, `status-bar.css`, `index.css` (palette).

## The "next period" source of truth

`Calendar` already fetches `predictions` (`{ predictedStart, predictedDuration }`,
ascending). The **next** period = the first prediction whose `predictedStart >=
today` (fallback `predictions[0]`). From it:

- `nextStartIso` — the first day, used for the countdown badge.
- `daysUntil = daysBetween(todayIso, nextStartIso)` — used by badge + status bar.

Today is derived with the existing `isoDate(new Date())` helper (string-based, no
timezone drift).

## 1. Today marker

Add `iso === todayIso` → an `is-today` class on that `.calendar-day`.

**Chosen treatment:** a 2px **cell ring in `--accent`** (#FFB86B), via
`box-shadow: inset 0 0 0 2px var(--accent)` on `.calendar-day.is-today`.
`--accent` is **not** overridden in the dark theme, so the same soft orange
reads on both the light (#FFF) and dark (#181E2E) surfaces. The ring sits inside
the cell border and doesn't clash with the centered blood-drop marks. Adjustable
if it doesn't look right in practice.

## 2. Countdown badge (next period's first day, lower-left)

On the single cell where `iso === nextStartIso` and `daysUntil >= 0`, render a
small element in the lower-left corner:

- `daysUntil === 0` → `today`
- otherwise → `${daysUntil}d` (tooltip: `N days until your next period`)

Positioned absolute bottom-left (the day number is top-left, the period mark is
centered, so the corner is free). Small font (~0.6rem) so it fits on mobile.
Colored with `--pred-next` to tie it to the "next predicted" tier.

## 3. Status bar — "Next period in N days"

`StatusBar` has no prediction data today, so we lift it:

- `Calendar` gains an optional `onNextPeriod?(info: { startIso: string;
  daysUntil: number } | null)` prop, fired from an effect when `predictions`
  change.
- `App` holds `nextPeriod` state and passes `daysUntil`/`startIso` to
  `StatusBar`.
- `StatusBar` shows a distinct, highlighted lead line **separate** from the
  "Past analysis" stats (which are backward-looking): e.g. a prominent
  **"Next period — in 5 days"** using `--primary`, with `in progress` / `today`
  / `—` (no data) edge wording.

This keeps "past analysis" semantically clean and gives the future-looking number
visual priority.

## 4. Return-to-current-month button

In `.calendar-header`, add a small secondary **"Today"** button that resets
`currentMonth = new Date()`. Shown only when the displayed month isn't the
current month (so it's an obvious "you've wandered, click to come back" affordance
and doesn't clutter the default view). Placed under the month title.

## Implementation steps

1. Compute `todayIso` + `nextPeriod` (memoized) in `Calendar`.
2. Today marker: `is-today` class + CSS (chosen treatment).
3. Countdown badge on `nextStartIso` cell + CSS.
4. `onNextPeriod` prop → `App` state → `StatusBar` new highlighted line + CSS.
5. "Today" button in the calendar header + CSS, conditional on not-current-month.
6. Verify in the running app, both themes, plus a paged-to-future month.

## Refinements (round 2, from review)

- **No header jump.** The "Today" button is now always rendered and toggled with
  `visibility` (`.is-hidden`) instead of conditional mounting, so the header
  height is constant (measured 100.3px in both states).
- **Message as a popover, not a banner.** The full-width `.recalc-error` banner is
  replaced by a big `!` icon (textbox-height) sitting between the period field and
  the Recalculate button, with its space always reserved (no row shift). The
  message pops out on hover or click, keeping the old left-border message look.
- **Severity colors.** New `--warning` var (khaki `#B8860B` light / `#FFD966`
  dark). Errors stay red (`--error`), the out-of-range case is a khaki **warning**.
  Error takes priority over warning on the shared icon.
- **Out-of-range is now a live, non-blocking warning** (shown while editing in
  manual mode) instead of a blocking `window.confirm` gate — recalc proceeds. The
  future-dates case is still a hard error that blocks (and short-circuits before
  any DB write).

## Refinements (round 3, from review)

- **Live validation.** Both the future-dates **error** (red) and the out-of-range
  **warning** (khaki) are now derived every render, so the `!` message updates as
  you paint days / edit fields and **clears itself once fixed** — no longer only
  on Recalculate. Editing also clears a stale recalc (API) error. Only the hard
  future-dates error still blocks the save.
- **Prediction colors → 2 tiers, legend-matched.** Red is now reserved for
  actual/saved. Predictions are **orange** (next, `tier-0` = `--pred-next`) and
  **khaki** (future, `tier-1` = `--pred-later`); the third middle shade and the
  unused `tier-2` are gone. Legend: 🔴 Actual · 🟠 Next Predicted · 🟡 Future
  Predicted · ✎ Auto-filled — now matching exactly what's on the calendar, in
  both themes.

## Open questions

1. **Today color** — chip (default) vs ring vs other. (See §1.)
2. **Badge wording** — `5d` (compact, default) vs `in 5d`. Lower-left is tight,
   so compact is the lean.
3. **Dropped confirm gate** — out-of-range no longer blocks with a confirm; it's a
   persistent visible warning. Confirm if that's the wanted behavior or restore a
   gate.
