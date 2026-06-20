# Backlog — planned / not-yet-started work

> Running list of things we mean to do but haven't. When an item ships, move it
> out of here (into its own design doc / RELEASE_NOTES) and delete the entry.

## UI updates — calendar today marker, countdown, return-to-month

**Status:** planning / draft, no code yet. Branch `ui_updates_2`.
**Design:** [ui-updates.md](ui-updates.md) (full spec + open questions).

Four small calendar / status-bar improvements:

1. **Mark today** on the calendar — currently no indication of the current date
   (chosen treatment: 2px `--accent` ring on the today cell).
2. **Countdown badge** in the lower-left of the next period's **first day only**
   (e.g. `5d`).
3. **"Next period in N days"** in the bottom status bar (separate from the
   backward-looking "past analysis" stats).
4. **"Return to current month"** button in the calendar header, shown only when
   the displayed month isn't the current one.

Open questions (see [ui-updates.md](ui-updates.md) §Open questions): today-cell
treatment, badge wording (`5d` vs `in 5d`), and whether out-of-range should block
with a confirm or stay a visible non-blocking warning.

---

### Notes

- **No `main` / `release` reconciliation needed.** Checked 2026-06-20: the two
  branches point to the **identical tree** (`git diff origin/main origin/release`
  is empty). The extra commits on `release` are merge-history artifacts only — no
  stranded features. Don't re-flag this.
