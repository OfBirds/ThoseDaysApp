# Data export / import / backup — design

> Status: **implemented** (branch `feat/reminders-export-confidence`). Captures the design for
> letting a user download their data (full or a date-range patch), re-import it
> safely as a reviewed patch, and have the server keep periodic backups — all in
> service of the project's self-hosted, "your data is yours" ethos.

## Goal

Three related capabilities, one shared file format:

1. **Export** — download all your data, or just a chosen number of recent cycles
   (a *patch*), to a portable JSON file.
2. **Import** — bring a file back in as a **reviewed patch**: it replaces only the
   date window the file covers, never silently wipes the rest, and **nothing is
   written until the user explicitly saves it**.
3. **Backup** — the server periodically writes export files to disk on its own
   (toggleable), so there's always a recent snapshot to restore from.

This is the most philosophically on-brand area for an AGPL, self-hosted tracker:
the README leads with *"cycle data is about as personal as data gets, and it
should live on infrastructure you control."* It also pairs with the
data-retention promise in [`plan.txt`](../plan.txt) (*"user can delete all
data"*): export/backup first, then deleting is safe.

> **Audience-first, throughout.** Our users skew non-technical and risk-averse
> about this data, and the whole point of self-hosting is that there's no big
> support desk — just a hoster (possibly a partner). So **every control here gets
> plain-language copy and a tooltip**, every destructive action is spelled out in
> concrete dates before it happens, and the design makes it *hard* to lose data by
> accident. See [§9 UX & copy principles](#9-ux--copy-principles) — it is not an
> afterthought, it is a requirement.

## 1. The file format

A single **JSON** file. JSON because the data is small, nested, the frontend
already speaks it, and it round-trips losslessly — no CSV juggling, no new
dependency.

```json
{
  "schemaVersion": 1,
  "kind": "export",                 // "export" | "backup"
  "scope": "patch",                 // "full" | "patch"
  "exportedAt": "2026-06-13T09:00:00Z",
  "appVersion": "1.0.x",
  "range": { "start": "2026-03-01", "end": "2026-05-20" },  // covered window
  "cycleCount": 3,
  "account": { "email": "...", "createdAt": "...",
               "notifyReleases": true, "notifyPeriodReminder": false },
  "cycles": [
    { "startDate": "2026-05-01", "durationDays": 5, "corrected": true,
      "auto": false, "predictedStart": null, "createdAt": "..." }
  ]
}
```

Notes:

- `schemaVersion` is the forward-compat hinge — import branches on it so a v1 file
  still imports after the model grows.
- `range` is the **covered window**: `start` = the earliest cycle's start,
  `end` = the latest cycle's last bleeding day (`start + duration − 1`). This is
  what the patch logic and the user-facing messages key off.
- **No secrets.** `passwordHash` and `unsubscribeToken` are never serialized —
  enforced by an explicit export DTO, not by serializing entities.
- **Predictions are not exported.** They're derived; re-importing the cycles and
  regenerating reproduces them (`CycleService.GeneratePredictionsAsync`).
  Exporting them would only risk drift. `appVersion` is provenance only.

All cycle days are emitted as `yyyy-MM-dd` (matching the string-date convention in
`frontend/src/lib/predictions.ts`); timestamps are full ISO.

## 2. Export — full or patch

Default is **full** (everything). The user may instead export a **patch**: the
last *N* cycles.

- UI: a choice of *Full history* vs *Last N cycles*, with a small number input for
  N. **The moment they pick N, show the resulting date range** — e.g. *"Exports
  3 cycles: 1 Mar 2026 → 20 May 2026"* — so they always know exactly what's
  leaving the app before they click. (See §9 — this readout is a hard
  requirement, not a nicety.)
- Backend: `GET /api/user/{userId}/export?cycles=N` (omit `cycles` → full). A thin
  read: load the user + cycles (reuse `CycleService.GetUserCyclesAsync`), take the
  most recent N, project into the export DTO, set `scope`/`range`/`cycleCount`,
  return with `Content-Disposition: attachment; filename="<see §6>"`.

## 3. Import — a patch, never a blind replace

This is the delicate half. The earlier draft of this doc said "replace, not
merge"; **that was wrong for real histories** and is hereby superseded.

### Why patch

Consider a user with 5 years of history who imports a file covering 3 of those
years (or just a few cycles). A wholesale replace would **delete the surrounding
history and tear a gap** in their record. Instead, import **patches the covered
window only**:

- Let `[P0, P1]` be the file's `range` (`P0` = first imported start, `P1` = last
  imported bleeding day).
- Existing periods whose **start falls inside `[P0, P1]`** are removed and
  replaced by the imported ones.
- Existing periods **before `P0`** and **after `P1`** are **kept untouched.** The
  import is a patch in the middle (or end, or start) of the timeline — there may
  well be recorded history after the patch, right up to today.

### The two boundary seams (and why we report them)

Patching joins imported data onto existing data at up to two seams. A bad seam
(e.g. a 200-day gap because the file's block doesn't abut the neighbouring
history) silently skews the averages that drive predictions. So **before
committing, we compute both seams and show them to the user**:

| Seam | Definition | What we tell the user |
|------|-----------|------------------------|
| **Leading** | `daysBetween(lastDayOf(periodBeforePatch), P0)` — gap from the end of the period just before the patch to the patch's first day | "There's a **{n}-day gap** between your existing history and the start of this import." |
| **Trailing** | `daysBetween(P1, startOf(periodAfterPatch))` — gap from the patch's last day to the start of the next existing period | "After the import, there's a **{n}-day gap** until your next recorded period." |

Edge cases, each with its own plain message:

- **No period before** (patch is the earliest history) → no leading seam; say
  "This is the earliest history on record."
- **No period after** (patch reaches the most recent history) → no trailing seam;
  say "There's no recorded history after this import."
- **Overlap** at a boundary (an existing period starts inside the window) → it's
  replaced, per the rule above; the message notes how many existing cycles in the
  window will be replaced.

### The review message (user-facing)

Before anything is written, the user sees a clear summary, in plain language and
concrete dates — never jargon:

> **Review this import**
> This will update your history from **1 Mar 2026** to **20 May 2026** (**3
> cycles**). Your history **before 1 Mar 2026** and **after 20 May 2026** stays
> exactly as it is.
> • **5 cycles** currently in that range will be replaced.
> • There's a **2-day gap** between your earlier history and this import.
> • There's a **31-day gap** after this import until your next recorded period —
>   that's a bit longer than usual; double-check the dates.
> Nothing is saved yet — you can review it on the calendar first.

The "longer/shorter than usual" hint compares the seam against the user's typical
interval (we already compute it — `weightedInterval` / `intervalStdDev` in
`frontend/src/lib/stats.ts`) so an unusual seam gets gently flagged rather than
buried in a number.

## 4. Import never auto-applies — review, then "Save this history permanently"

Import is dangerous, so **agreeing to import does not write anything.** The flow:

1. User picks a file → we parse and validate it (§5).
2. User reads the review message (§3).
3. **"Not saved yet" consent prompt.** Before we load anything onto the calendar,
   we ask them to explicitly agree that this is being loaded **for review, not
   saved** — and reassure them that saving isn't a scary one-way door:
   > **Just so you know — this won't be saved yet**
   > We'll put this history on your calendar so you can look it over first.
   > **Nothing is saved until you decide to save it.** And even after you save it,
   > you can still edit your history later — saving isn't final.
   > [ Cancel ]  [ I understand — show it to me ]

   The reason for this explicit step: the audience is cautious, and the natural
   worry is *"if I load this, did I just overwrite everything?"* Agreeing here
   answers that up front (no), and the "you can still edit later" line lowers the
   stakes of the whole action — they're never one click away from something
   irreversible.
4. On agreement, we paint the imported patch onto the calendar as a **pending,
   unsaved** overlay, and show a persistent banner: *"Imported history — not saved
   yet. Review it, then click **Save this history permanently**."*
5. Only when the user clicks **"Save this history permanently"** do we commit
   (§5, backend patch-commit).

> **Reconciling "permanently" with "you can edit it later."** The button says
> *permanently* to distinguish saving from the temporary unsaved preview — it
> means "written to your account" rather than "locked forever." The consent copy
> and tooltips must make that clear so the two never feel contradictory: saved
> history is still editable through the normal calendar + Recalculate flow, just
> like any other recorded cycle.

### The new button

- A dedicated **"Save this history permanently"** button sits **next to
  Recalculate**, in a **distinct, intentionally different colour** (Recalculate is
  the everyday action; this is the deliberate, weighty one). The contrast is the
  signal — the user shouldn't have to read carefully to tell them apart.
- It only appears while an import is pending.

### Relationship to the existing draft model

The app already has a draft-vs-committed split (see
[recalc-spec.md](recalc-spec.md)): painting days lives in `localStorage` and
**Recalculate** is the only thing that writes. A pending import is a *distinct,
wider* staged state layered on the same idea — but it commits through its **own
patch path** (preserving before/after history), **not** through Recalculate's
full-replace.

That distinction creates one trap we must guard:

- **If the user clicks Recalculate while an import is pending**, Recalculate's
  normal behaviour (full replace from painted days) would throw the patch away.
  So we intercept it with a confirm:
  > **Discard imported history?**
  > You have imported history that hasn't been saved. Recalculating will
  > **discard it**, and **nothing will be saved**. To keep the import, cancel and
  > click *Save this history permanently* instead.
  On confirm: the pending import is discarded and we return to the last committed
  state — **nothing is written** (matches the literal "nothing will be saved").
  On cancel: nothing happens; the import stays pending so they can save it
  properly.

## 5. Import — backend

`POST /api/user/{userId}/import` — invoked by **"Save this history permanently"**,
*not* on file selection.

### Validation (reject early, reject loudly — before any write)

- `schemaVersion` is known (`== 1`) → else `400` with a clear message.
- Each cycle: parseable `startDate`; `durationDays` within sane bounds from
  `RecalcConfig`. Don't silently drop a bad row — reject the file and name the
  offending entry so the UI can explain it.
- Cap cycle count (e.g. a few thousand) so a malformed/huge file can't OOM.
- Account metadata in the file is **informational only**: import never overwrites
  the logged-in user's email/prefs and never creates accounts. It only ever
  patches the **cycles of the authenticated `userId`**. (Cross-user / admin
  "restore into a new account" is out of scope for v1.)

### The patch commit (atomic)

Within one transaction (mirrors the single atomic `SaveChanges` in
`CycleService.RecalculateAsync`):

1. Remove existing cycles whose start falls in `[P0, P1]`.
2. Insert the imported cycles (`Auto = false`, they're user-confirmed real data).
3. Keep everything outside the window.
4. Regenerate the forecast once (`GeneratePredictionsAsync`) so predictions
   reflect the patched history.

This needs a **new service method** — `PatchCyclesAsync(userId, importedCycles)` —
because the existing `RecalculateAsync` deliberately wipes *all* cycles, which is
exactly what we must not do here. The forecast-regen tail is shared.

Return the resulting cycles + forecast (same `RecalcResponse` shape) so the UI
refreshes to the saved truth.

## 6. Naming convention (export files **and** backups)

A consistent, human-readable name so both the user and the hoster can tell at a
glance what a file covers — critical when restoring a specific window.

```
thosedays-{kind}-{scope}-{rangeStart}_{rangeEnd}-{stamp}.json
```

| Segment | Example | Meaning |
|---------|---------|---------|
| `kind` | `export` / `backup` | who produced it |
| `scope` | `full` / `patch` | whole history vs a window |
| `rangeStart_rangeEnd` | `20260301_20260520` | covered window (`yyyyMMdd`), from `range` |
| `stamp` | `20260613T0900Z` | generation time (UTC) |

Examples:

- Full manual export: `thosedays-export-full-20210101_20260613-20260613T0900Z.json`
- 3-cycle patch export: `thosedays-export-patch-20260301_20260520-20260613T0900Z.json`
- Monthly server backup: `thosedays-backup-patch-20260501_20260605-20260601T0000Z.json`

> **Multi-user note:** server backups for more than one account must not collide,
> so backups get a short user segment: `thosedays-backup-{userShort}-patch-…`,
> where `userShort` is a non-secret prefix of the user id. (Manual exports are
> downloaded by the user themselves, so they don't need it.)

## 7. Server-side periodic backups

The server can write export files to disk on a schedule, so a recent snapshot
always exists even if the user never thinks to export.

- **Background job** — a new `BackupService : BackgroundService`, modeled on the
  periodic sweep shape used by reminders (see
  [period-reminders.md](period-reminders.md) §4): wake on an interval, write
  backups, sleep.
- **Toggle + config (env):**

  | Env key | Meaning | Default |
  |---------|---------|---------|
  | `BACKUP_ENABLED` | master on/off | off |
  | `BACKUP_INTERVAL` | how often (e.g. `monthly`) | `monthly` |
  | `BACKUP_DIR` | where files are written | `./backups` |
  | `BACKUP_CYCLE_COUNT` | fixed count to back up, or unset → window-based (below) | unset |

- **What each backup covers (the spillover rule):** by default a backup is a
  **patch** covering the periods within the interval **plus a spillover of one
  period on each side**. The spillover is deliberate — it preserves the boundary
  seams (§3), so the backup can later be re-imported as a clean patch that abuts
  the surrounding history without inventing a gap. If `BACKUP_CYCLE_COUNT` is set,
  back up that many most-recent cycles instead (and it should be **≥ what the
  interval would cover**, so the window is never under-captured).
- Backups reuse the **same export builder** and the **§6 naming convention**, so a
  backup file imports through the exact same reviewed-patch path as a manual file
  — no separate format, no separate import code.
- Retention: keep the last *K* backups (configurable later); prune older ones so
  the directory doesn't grow unbounded.

### Future step (noted so we don't forget)

> **Additional backup destinations.** Some self-hosters (the maintainer included)
> run a NAS or other storage. A later iteration can let `BACKUP_DIR` be a list, or
> add destination plugins (NAS mount, S3-compatible, etc.), so backups land
> somewhere off the app host. Out of scope for v1 — design the v1 `BackupService`
> so the "where do I write this file" step is a small seam that's easy to extend.

## 8. Frontend surface

A small **Data** section (likely the shared `/settings` route also used by the
reminder prefs in [period-reminders.md](period-reminders.md)):

- **Export** — Full vs Last-N choice, with the live date-range readout (§2); click
  triggers a browser download (anchor `download` / Blob URL — no new dependency).
- **Import** — file picker → parse + validate → review message (§3) → "show it to
  me" paints the pending overlay → **Save this history permanently** commits. The
  Recalculate-while-pending guard from §4 lives here too.
- After a save, refresh cycles/predictions from the server so the calendar shows
  the committed truth.

Connectivity: export/import are server round-trips, so v1 requires being online;
the offline (PWA-cached) story can come later.

## 9. UX & copy principles

This section is a **requirement**, not decoration. The users we're designing for
are largely non-technical, cautious with this exact data, and have no support
desk to fall back on — so the interface has to carry all the reassurance itself.

1. **Concrete dates, never jargon.** Every message names real dates ("from 1 Mar
   2026 to 20 May 2026"), never "the selected range" or "P0/P1". No "patch",
   "commit", "schema" in user-facing text — say "update your history", "save",
   "file".
2. **Tooltip on every control.** Export-scope selector, the N input, the Export
   button, Import, **Save this history permanently**, and Recalculate (about the
   discard guard) each get a short plain tooltip explaining what it does and what
   it touches. Examples:
   - *Save this history permanently:* "Writes the imported history to your
     account. Your history outside the imported dates is untouched."
   - *Recalculate (with import pending):* "Recalculates from what's on the
     calendar. This will discard the imported history you haven't saved yet."
   - *Export → Last N cycles:* "Saves just your most recent cycles to a file. We'll
     show you exactly which dates that covers."
3. **Tell them what stays safe.** Wherever something is replaced, also state
   plainly what is *kept* ("your history before/after these dates stays exactly as
   it is"). Reassurance reduces the "did I just delete everything?" panic.
4. **Nothing scary is one click.** The genuinely destructive/permanent actions
   (Save permanently, discard-on-recalc) are confirm-gated and visually distinct;
   the everyday ones aren't, so the weight matches the risk.
5. **"Nothing is saved yet" is visible the whole time** an import is pending — a
   persistent banner, not a toast that vanishes.
6. **The file is personal data** — a brief line on export ("this file contains
   your cycle history — keep it somewhere safe").

Copy should be drafted and reviewed as carefully as the code; a confusing sentence
here is a support ticket (or worse, lost data) there.

## 10. Tests

Everything below is in scope; **all three docs' features must ship with tests**
(reminders especially — see [period-reminders.md](period-reminders.md) §8).

Backend (xUnit + EF InMemory, alongside existing suites in `backend/Api.Tests/`):

- **Export full** vs **patch**: patch returns exactly the last N cycles and a
  `range` matching their span; full returns all; `scope`/`cycleCount` correct.
- **Secrets excluded** from any export (`passwordHash`, `unsubscribeToken` absent).
- **Patch commit preserves neighbours:** importing a window into a longer history
  removes only in-window cycles, keeps before/after, regenerates a forecast.
- **Seam math:** leading/trailing gaps computed correctly, including the
  no-before, no-after, and overlap edge cases.
- **Validation/atomicity:** unknown `schemaVersion`, bad date, out-of-range
  duration, over-cap count → `400` and **DB untouched**.
- **Round-trip:** export a window → import it back → identical in-window cycles,
  neighbours intact.
- **Naming convention** produced exactly per §6 (full, patch, backup-with-user).
- **Backup job:** with `BACKUP_ENABLED=false` → no-op; enabled → writes a file
  named per §6 covering the interval **plus spillover**; `BACKUP_CYCLE_COUNT`
  override honoured; reuses the export builder.

Frontend (Vitest + Testing Library):

- Export Last-N shows the date range as soon as N changes.
- Import does **not** write on file-select, on the review message, or on the
  "not saved yet" consent prompt — only on **Save this history permanently**.
- The **"not saved yet" consent prompt** appears before the calendar overlay is
  painted; cancelling it paints nothing; agreeing paints the pending overlay.
- Pending-import banner is present while unsaved; Recalculate-while-pending shows
  the discard confirm and writes nothing on confirm.
- Review message renders concrete dates, the replaced-count, and both seams (with
  the "longer than usual" hint when a seam exceeds typical interval ± stddev).
- Tooltips present on each control listed in §9.

## Open questions

- **CSV export?** JSON is canonical/round-trippable; a read-only CSV-of-cycles
  could be additive later. Out of scope for v1.
- **Backup retention default `K`** and **interval vocabulary** (`monthly` only, or
  also `weekly`/`daily`?) — pick sensible defaults, expose later.
- **Exact `userShort` length** in backup filenames (collision-safe but not
  leaking the full id) — settle when implementing §6.
- **Recalculate-with-pending-import outcome:** v1 discards the import and writes
  nothing (literal "nothing will be saved"). If users find an aborted Recalculate
  surprising, revisit whether it should proceed with the painted days after
  discarding.
