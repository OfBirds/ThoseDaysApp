# Prediction confidence / variability — design

> Status: **implemented** (branch `feat/reminders-export-confidence`). Captures the design for
> turning the unused, hard-coded `Confidence` field into a real, variability-based
> signal and surfacing a ± range on predictions.

## Goal

Make predictions **honest about uncertainty**. Today the forecast is a single
point — `next_start = last_start + round(weighted_avg_interval)` — and every
`Prediction` is stamped with a constant `Confidence = 0.85f`
(`CycleService.RegenerateForecastAsync`, `backend/Api/Services/CycleService.cs`).
For a user with regular cycles that point guess is fine; for an irregular user
it's actively misleading, presented with the same false certainty.

We already *compute* the raw material for a confidence signal — interval standard
deviation — on the stats page (`stdDev` / `intervalStdDev` in
`frontend/src/lib/stats.ts`). This feature wires that variability into the
prediction itself: a real confidence score **and** a "expected between X and Y"
window.

## 1. The model

Keep the point estimate exactly as-is (the weighted-mean interval — don't touch
the well-tested `ComputeAverages` / `WeightedAverage` path). Add an uncertainty
band derived from the **spread of recent intervals**.

Definitions (all from the user's actual intervals, newest-favored to match the
existing forecast basis):

- `mu` = weighted mean interval (already computed; the point estimate).
- `sigma` = standard deviation of intervals (population std dev — same formula as
  `stats.ts:stdDev`).
- **Window** for the Nth future period: `mu*N ± k*sigma*sqrt(N)` around the
  cumulative start, i.e. uncertainty **grows with how far out** the prediction is
  (errors compound cycle over cycle — a 6-cycles-out date is far less certain than
  the next one). `k` ≈ 1 gives a ~68% band; make `k` a `RecalcConfig` knob.
- **Confidence score** for a prediction: a normalized, bounded function of
  relative spread, e.g. `confidence = clamp(1 - (sigma / mu), floor, 1)`. Low
  variability → high confidence; erratic history → honest low confidence.

### Cold-start / thin history

- 0–1 intervals: no meaningful spread. Fall back to a fixed nominal confidence and
  **no band** (or a wide default band), and flag it as low-data. This mirrors how
  `ComputeAverages` already falls back to `DefaultCycleLength` with thin history.
- Clamp `confidence` to a floor (e.g. 0.3) so we never display "0%".

All constants (`k`, confidence floor, min intervals for a band) go in
`backend/Api/Config/RecalcConfig.cs` next to the existing tunables, and are
already shipped to the frontend via `GET /api/config` (`ConfigController`), so the
UI and backend stay in lockstep — same pattern the recalc formula already uses.

## 2. Backend changes

`Prediction` already has the `Confidence` field — no schema change needed for the
score. For the band we add two nullable columns (or compute on read; see §4):

| Field | Type | Meaning |
|-------|------|---------|
| `EarliestStart` | `DateTime?` | Lower edge of the predicted-start window. |
| `LatestStart` | `DateTime?` | Upper edge of the predicted-start window. |

In `RegenerateForecastAsync`:

- Compute `sigma` once from the same intervals `ComputeAverages` uses (thread it
  out of `ComputeAverages`, or add a sibling that returns `(cycleLength,
  periodDuration, intervalStdDev)` so we don't walk the periods twice).
- Replace the hard-coded `Confidence = 0.85f` with the computed score.
- For each future period `i` (1-based), set the band per §1, widening with `i`.

Surface the new fields in `PredictionResponse` (`backend/Api/DTOs/CycleDto.cs`)
and the projections in `PredictionsController`.

## 3. Frontend changes

Two surfaces consume predictions: the **calendar** (`Calendar.tsx`) and the
**stats page** (`StatsPage.tsx`).

- **Next-period readout** (status bar / countdown): show the point estimate as
  today, plus a softer range — e.g. *"in ~3 days (Mon–Thu)"* — when a band exists
  and is wider than ±1 day. If confidence is high/band is tight, keep it clean and
  just show the single date.
- **Calendar painting:** optionally render the band's edge days in a lighter shade
  than the predicted core (the palette already has a tiered orange→khaki scheme in
  `frontend/src/styles` and `predictionTier` in `predictions.ts` to build on).
  Keep it subtle — the point estimate stays the visual anchor.
- **Stats page:** add a confidence indicator near the existing prediction-accuracy
  panel. The page already computes `intervalStdDev`; now it can show *"your cycles
  vary by ±N days, so predictions carry M% confidence,"* tying the abstract number
  to something the user feels.

The frontend can compute the same band client-side from the cycle list (it
already mirrors the backend math in `predictions.ts`), so the band is available
even for the live draft before a recalc — but the committed numbers come from the
backend so the two never disagree.

### Copy & clarity

Uncertainty is easy to word badly. Follow the shared plain-language principles in
[data-export-import.md](data-export-import.md) §9: no jargon ("confidence
interval", "sigma"), say it in human terms — *"Your cycles vary by about ±N days,
so this date could land a little earlier or later."* A low-confidence forecast
should read as honest and calm, **not** alarming ("we're less sure about this
one — expect it sometime that week"), since the audience reads health uncertainty
anxiously. A tooltip on the confidence indicator explains what drives it (how
regular recent cycles have been).

## 4. Store vs. compute — a decision to make

Two viable shapes:

| Approach | Pros | Cons |
|----------|------|------|
| **Store** `EarliestStart`/`LatestStart` on `Prediction` | Consistent with stored `Confidence`; one source of truth; no recompute on every read | Migration; must regenerate on any formula change |
| **Compute on read** (derive band in the controller/frontend from cycles + config) | No schema change; formula tweaks need no data migration | Slight duplication of the band math front/back |

**Leaning: compute on read** for the *band* (it's cheap and derivable), and use
the already-stored `Confidence` for the score. That avoids a migration entirely
and keeps the band a pure function of `(intervals, config)`. The table in §2 is
the fallback if we decide the band must be query-filterable later.

## 5. Tests

Backend (xUnit + EF InMemory):

- Regular history (low sigma) → high confidence, tight band.
- Erratic history (high sigma) → low confidence (clamped to floor, never 0),
  wide band.
- Band widens monotonically with horizon (period 6's window ⊇ period 1's).
- Thin history (0–1 intervals) → nominal confidence, no/empty band, no throw.
- Point estimate unchanged vs. current behavior (regression guard on
  `ComputeAverages`).

Frontend (Vitest): range shown only when band exceeds the tight threshold;
high-confidence case renders a single date; stats confidence indicator reflects
`intervalStdDev`.

## 6. Why this is worth doing

- The `Confidence` field is **already in the model, the DTO, and the API
  response** — it's plumbed end-to-end and currently lies (constant `0.85`).
  This makes an existing-but-fake signal real.
- It's the highest-integrity feature for a *health* app: telling an irregular user
  "we're not sure — expect it sometime this week" is more useful and more honest
  than a confident wrong date.
- Zero new dependencies; all the math (`stdDev`, weighted mean) already exists in
  `stats.ts` and `CycleService`.

## Open questions

- **Band model.** `±k*sigma*sqrt(N)` assumes roughly independent cycle-to-cycle
  variation. Fine for a v1 heuristic; if we later want rigor, a proper predictive
  interval is a bigger statistical lift and probably not worth it at this scale.
- **Display threshold.** What ± width flips the UI from "single date" to "range"?
  Start with ±1 day as the cutoff and tune from real data.
