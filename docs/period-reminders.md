# Period reminder notifications — design

> Status: **planning / draft**. No code yet. Captures the intended design for
> emailing opted-in users ahead of a predicted period, reusing the email +
> opt-in + unsubscribe machinery already built for release notifications.

## Goal

Proactively email each opted-in user a few days before their **next predicted
period**: *"ThoseDays — your next period is expected in 3 days (Tue 16 Jun)."*
Today the app forecasts 15 cycles ahead but never reaches out — the user has to
open the calendar to see what's coming. This closes that gap.

This is the `phase2` *"optional notifications"* item from [`plan.txt`](../plan.txt),
and it deliberately rides on the infrastructure already shipped for
[release notifications](notifications.md):

- `IEmailSender` / `SmtpEmailSender` — working outbound mail (`backend/Api/Services/`).
- `User.NotifyReleases` + `User.UnsubscribeToken` + `UnsubscribeController` — the
  opt-in / one-click-unsubscribe pattern we copy.
- `ReleaseNotifier : BackgroundService` — the background-job shape we mirror.
- `Prediction.PredictedStart` — the date the reminder fires against, already
  computed and stored.

**Principle:** opt-in, email-only, self-hosted. No third-party push service, no
new outbound dependency, no data leaving the box.

## 1. Why email (not Web Push)

The app is a PWA, so browser Web Push is technically possible — but it needs a
VAPID key pair, a service-worker push handler, per-device subscription storage,
and it only fires when the browser/OS cooperates. Email is already wired,
delivers regardless of device, and matches how release notices already work.
**Decision: email for v1.** Web Push can be a later additive channel; the
scheduling/eligibility logic below is channel-agnostic and would be reused.

## 2. Data model changes

Add to `backend/Api/Models/User.cs` (mirrors `NotifyReleases`):

| Field | Type | Default | Meaning |
|-------|------|---------|---------|
| `NotifyPeriodReminder` | `bool` | `false` | Opt-in to period reminders (off by default — explicit consent). |
| `ReminderLeadDays` | `int` | `2` | How many days before the predicted start to send. |

We do **not** add a per-cycle "already reminded" flag to every `Prediction`.
Instead, dedupe with a single `SystemSetting`-style marker per user so a daily
sweep can't double-send (see §4). Concretely, add to `User`:

| Field | Type | Meaning |
|-------|------|---------|
| `LastReminderSentFor` | `DateTime?` | The `PredictedStart` we last reminded about. Sweep skips a cycle whose start equals this. |

The existing `UnsubscribeToken` already covers unsubscribe; see §6 for scoping
unsubscribe to *which* email type.

### Migration

New EF Core migration `AddPeriodReminderPrefs` (three columns, all nullable or
defaulted — no backfill needed; `false`/`2`/`null` are correct for existing
rows). Follows the existing `AddNotificationsAndSettings` migration pattern.
Startup `Database.Migrate()` in `Program.cs:101` applies it automatically.

## 3. Eligibility — who gets an email today

A user is due for a reminder on a given run when **all** hold:

1. `IsActive && NotifyPeriodReminder`.
2. SMTP is configured (`SmtpOptions.IsConfigured`) — otherwise the sweep no-ops
   cleanly, same as a missing-host release run.
3. Their soonest prediction with `PredictedStart >= today` lands within
   `ReminderLeadDays` of today (i.e. `0 <= daysUntil <= ReminderLeadDays`).
4. `LastReminderSentFor != thatPredictedStart` (haven't already reminded for this
   exact cycle).

Reuse the "next prediction" definition already used on the frontend
(`findNextPrediction` in `frontend/src/lib/predictions.ts`): the soonest forecast
start `>= today`. The backend query is the mirror of
`CycleService.GetPredictionsAsync` filtered by date.

## 4. The background job

New `backend/Api/Services/ReminderNotifier.cs`, a `BackgroundService` modeled on
`ReleaseNotifier` but **periodic** rather than once-at-startup:

- Loop: run the sweep, then `await Task.Delay(interval, ct)`; default interval
  once per ~6h (configurable), so a daily reminder is timely without hammering.
  A startup run covers the "container just restarted" case.
- Each sweep opens a scope (`IServiceScopeFactory`, exactly like `ReleaseNotifier`),
  loads eligible users + their next prediction, and for each due user:
  - Build the email (§5), `await email.SendAsync(...)`, best-effort try/catch and
    keep going so one bad address can't block the batch (same posture as
    `ReleaseNotifier`).
  - On success set `user.LastReminderSentFor = predictedStart`; `SaveChanges`.
- Gate the whole thing behind a config flag so staging/local stay quiet, matching
  `NOTIFY_ON_DEPLOY`. New env key: **`NOTIFY_REMINDERS`** (`"true"` to enable).

Register in `Program.cs` next to the existing notifier:

```csharp
builder.Services.AddHostedService<ReminderNotifier>();
```

### Timezone note

`PredictedStart` is stored as a UTC `DateTime` whose `.Date` is the intended
local calendar day (see the `ToUtc` / `SpecifyKind` handling in `CycleService`).
"Days until" is a whole-day subtraction on `.Date`, so a 6-hourly sweep is well
inside tolerance and we don't need per-user timezones for v1. If we later want
"send at 8am local," store an offset on `User` and check it in the sweep.

## 5. The email

Add a `BuildReminderEmail(...)` helper (same shape as
`ReleaseNotifier.BuildEmail` — returns `(subject, html, text)`), or factor a tiny
shared email-layout helper both notifiers call.

- **Subject:** `ThoseDays — your next period is expected in {n} day(s)`
- **Body:** the expected start date (formatted), the lead time, a link to open
  the app (`PUBLIC_BASE_URL`), and the unsubscribe footer.
- **Unsubscribe link:** `"{baseUrl}/api/unsubscribe?token={token}&kind=reminder"`
  — see §6.

Keep the gentle, non-alarming tone; this is a heads-up, not a medical alert.

## 6. Unsubscribe scoping

`UnsubscribeController` currently flips `NotifyReleases` unconditionally. To let a
user unsubscribe from reminders without losing release emails (and vice-versa),
extend it with an optional `kind` query param:

- `kind=reminder` → clear `NotifyPeriodReminder`.
- `kind=release` (or absent, for backward-compat with already-sent release
  emails) → clear `NotifyReleases`.

Still GET, still idempotent, still token-scoped — no behavioral change to
existing links.

## 7. Frontend — the opt-in UI

Users must be able to turn this on and pick a lead time. Two pieces:

1. **Settings surface.** There's no settings page today (preferences like theme
   and font scale live in `localStorage` via `frontend/src/lib/storage.ts`).
   Reminder prefs are *server-side* (the sweep runs without a browser), so they
   need an API, not localStorage. Add a small "Notifications" section — either a
   new `/settings` route or a panel reachable from the existing UI.
2. **API to read/write prefs.** New endpoints on a `UserController` (or extend
   auth):
   - `GET  /api/user/{userId}/prefs` → `{ notifyPeriodReminder, reminderLeadDays }`
   - `PUT  /api/user/{userId}/prefs` → updates them.
   Return the prefs on login too (extend `AuthResponse`) so the UI can show
   current state without an extra round-trip.

### Copy & clarity (same audience care as export/import)

Our users skew non-technical and cautious; the opt-in must read plainly and never
feel like it does more than it does (see
[data-export-import.md](data-export-import.md) §9 for the shared principles):

- Checkbox: **"Email me before my period"** with a tooltip — *"We'll send a
  reminder a few days before your next expected period. You can turn this off any
  time, including from a link in the email."*
- Lead-days input: labelled **"How many days before?"**, clamped to a sane range
  (e.g. 1–7), tooltip — *"How early the reminder arrives."*
- A quiet note that it needs an email on file and that the host must have email
  set up — phrased reassuringly, not as an error.
- Make clear it's **email only** and that **no data leaves the app** beyond the
  reminder itself.

## 8. Tests

Reminders are the feature most worth testing hard — they fire on a schedule,
touch real users, and a bug means either silence or spam. **This must ship with a
thorough suite.**

Backend (xUnit + EF InMemory, mirrors existing suites in `backend/Api.Tests/`).
Factor the sweep so its core is a **pure, time-injected `IsDue` / sweep function**
(pass "now" in, don't read the clock) so eligibility is unit-testable without
waiting on the `BackgroundService` loop:

- **Eligibility / lead window:** due when `0 <= daysUntil <= ReminderLeadDays`;
  not due just outside it; respects each user's own `ReminderLeadDays`; boundary
  days (`daysUntil == 0`, `== leadDays`) handled exactly.
- **Opt-in gating:** opted-out (`NotifyPeriodReminder == false`) → never due;
  inactive user → never due.
- **Dedupe / idempotency:** two sweeps back-to-back send **exactly once**
  (`LastReminderSentFor` set after the first); a regenerated forecast with a *new*
  `PredictedStart` **re-arms** and sends again; the same `PredictedStart` does not.
- **No next prediction** (empty/elapsed forecast) → not due, no throw.
- **SMTP not configured** (`SmtpOptions.IsConfigured == false`) → sweep no-ops
  cleanly, sends nothing (fake `IEmailSender` records zero calls).
- **Master flag:** `NOTIFY_REMINDERS` off → sweep doesn't run / sends nothing.
- **Batch isolation:** with several due users, one `SendAsync` throwing does **not**
  block the rest (others still sent, failure logged) — mirror `ReleaseNotifier`'s
  best-effort posture, and assert the thrower's `LastReminderSentFor` is **not**
  advanced (so it retries next sweep).
- **Email content:** `BuildReminderEmail` subject/body contain the expected start
  date, the lead time, the app link, and the `kind=reminder` unsubscribe link.
- **Unsubscribe scoping:** `GET /api/unsubscribe?token=…&kind=reminder` clears
  only `NotifyPeriodReminder`; `kind=release`/absent clears only `NotifyReleases`;
  unknown/empty token → 404/400 as today; idempotent on re-click.

Frontend (Vitest + Testing Library):

- Prefs panel renders current server state (checkbox + lead days).
- Toggling/changing lead days issues the `PUT` with the new values; failure
  surfaces a non-destructive error.
- Tooltips present on the checkbox and lead-days input.

## 9. Rollout / config summary

| Env key | Where | Effect |
|---------|-------|--------|
| `NOTIFY_REMINDERS` | prod env file | Master on/off for the sweep (off on staging/local). |
| `SMTP_*` | existing | Required; sweep no-ops without them. |
| `PUBLIC_BASE_URL` | existing | The link in the email. |

No new infra, no new dependency. Ships dark (flag off + per-user opt-in off), so
turning it on is deliberate.

## Open questions

- **Email verification.** Registration (`AuthController`) doesn't verify the
  address today. Reminders to an unverified/typo'd address just bounce
  harmlessly, but a verify step would be a sensible prerequisite if we lean on
  email more.
- **Lead-time granularity.** v1 sends once, `ReminderLeadDays` before. Do we ever
  want a second same-day nudge? Out of scope for v1 — the dedupe marker is
  per-cycle, so adding a second offset later means tracking which offsets fired.
