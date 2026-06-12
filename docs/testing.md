# Testing

> How to run the test suites and what each one covers.

## Harness layout

| Suite | Location | Framework | Type |
|-------|----------|-----------|------|
| Backend | `backend/Api.Tests/` | xUnit + EF Core InMemory | Unit + service-level integration |
| Frontend | `frontend/src/**/*.test.{ts,tsx}` | Vitest + Testing Library | Unit + component render |

Both stacks run in CI on every push to `main` and `release` before the image is built or
deployed (`.github/workflows/deploy.yml`, `test` job).

## Running tests

### Backend

```bash
# From the repo root:
dotnet test backend/ThoseDays.slnx -c Release
```

The solution file (`backend/ThoseDays.slnx`) references both `Api` and `Api.Tests`, so a
single `dotnet test` discovers everything. EF Core InMemory is used for database tests —
no real PostgreSQL needed.

### Frontend

```bash
# From the repo root:
npm ci --prefix frontend          # first time only
npm test --prefix frontend -- --run
```

Or from inside `frontend/`:

```bash
npm ci
npm test           # single run
npm run test:watch # watch mode
```

## What each suite covers

### Backend — `Api.Tests`

| File | What it tests |
|------|---------------|
| `CycleServiceLogicTests.cs` | Pure algorithms: `GroupDaysIntoPeriods`, `WeightedAverage`, `ComputeAverages`. No database. Tests consecutive runs, gaps, deduplication, banker's rounding edge cases, and fallback defaults. |
| `CycleServiceDbTests.cs` | CycleService against an EF Core InMemory database. Covers `RecalculateAsync` (actual replacement, override wins, forecast count), `ReconcileAsync` (overdue conversion to `Auto` cycles, idempotency, regeneration), `GeneratePredictionsAsync` (15 future, empty history), and Add/Update/Delete mutation flows. |
| `ReleaseNotifierTests.cs` | `BuildEmail` (HTML bullet wrapping, encoding, empty notes, plain-text variant) and `ExecuteAsync` (idempotency via `last_notified_version`, disabled/dev/missing-version idle paths, best-effort loop where one failure doesn't block others, recipient filter for `IsActive && NotifyReleases`). Uses a fake `IEmailSender` and EF Core InMemory. |

### Frontend — Vitest

| File | What it tests |
|------|---------------|
| `lib/storage.test.ts` | localStorage draft save/read round-trip, `tda:` key prefix, `clearDraft`, `getAutoUpdate`/`getFontScale` defaults, malformed JSON resilience, theme save/read. |
| `lib/predictions.test.ts` | All pure helpers extracted from `Calendar.tsx`: `isoDate`, `daysBetween`, `addDaysIso`, `spanDays`, `groupPeriods`, `weightedAvg`, `computeAverages`, `findNextPrediction`, `predictionTier`, `isOutOfRange`, `findFutureDays`. Covers today marker, countdown delta, next-vs-future classification (tier 0 vs 1), out-of-range validation, and future-date filtering. |
| `components/StatusBar.test.tsx` | Renders "Next period — in N days" for a given prediction; handles `0` (today), singular/plural, `null` (no prediction), and the past-analysis stats section. |

## InternalsVisibleTo convention

To test `internal` members without exposing them publicly, the `Api` project declares:

```xml
<InternalsVisibleTo Include="Api.Tests" />
```

This lets the test project call `internal` methods directly (e.g. `CycleService.GroupDaysIntoPeriods`,
`ReleaseNotifier.BuildEmail`) while keeping them hidden from external consumers.

## CI gate

The `test` job in `.github/workflows/deploy.yml` runs on every push to `main` and
`release`. Both `build` and `deploy` are gated behind it (`build` → `needs: test`,
`deploy` → `needs: build`), so a red suite blocks the image from being built or deployed.
