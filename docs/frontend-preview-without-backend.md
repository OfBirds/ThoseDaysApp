# Front-end-only preview (fake login + fake data)

> How to preview authenticated, data-backed screens **without** standing up the
> backend or a database — by faking the login session and stubbing the API in the
> browser. Useful for reviewing styling/layout changes fast. The technique is
> generic; the ThoseDays specifics are given as a worked example.

This is a **manual dev/preview** technique, not an automated test. For the test
suites see [testing.md](testing.md).

## When to use it

- You only changed front-end code (CSS, layout, a component) and want to *see* it.
- The screen lives **behind login** and/or **renders data fetched from the API**.
- You don't want to run Postgres + the backend just to look at a page.

If your change is on the login screen itself (or anything that renders pre-auth),
you don't need any of this — just run the dev server.

## Why it works (the two preconditions)

This trick relies on two properties of the app. Check both hold before reaching
for it; if they don't, see [Adapting to other apps](#adapting-to-other-apps).

1. **The client trusts a stored session at mount — no server round-trip to log in.**
   In ThoseDays, `AuthProvider` seeds its state straight from `localStorage` with
   no validation call (`frontend/src/context/AuthContext.tsx`):

   ```ts
   const [user, setUser] = useState<User | null>(() => {
     const stored = localStorage.getItem('user');
     return stored ? JSON.parse(stored) : null;
   });
   ```

   So the auth gate in `App.tsx` (`if (!user) return <LoginPage/>`) is satisfied by
   a synthetic `user` object — no backend, no password.

2. **Data fetches degrade gracefully.** Page loaders wrap `fetch` in `try/catch`
   and fall back to empty state, so a missing backend doesn't white-screen the app —
   it just shows "no data". That empty state is enough to verify the footer/header/
   chrome, and the fetch stub below fills in the data when you want populated charts.

## Step 1 — bypass login

Inject a fake user + token, then let the app re-read it. Run this in the preview
page (DevTools console, or the preview tool's eval):

```js
localStorage.setItem('user', JSON.stringify({ id: 'preview-user', email: 'preview@local' }));
localStorage.setItem('token', 'preview-token');
location.reload();           // AuthProvider now seeds `user` from localStorage → app renders
```

That alone gets you onto every authenticated screen. Data-backed sections show
their empty state until you also do Step 2.

## Step 2 — fake the API data

Monkey-patch `window.fetch`: intercept the endpoints the target screen calls,
return fixtures, and **pass everything else through** to the real fetch. Install
the patch *before* the screen mounts.

```js
(() => {
  // --- build fixtures in the shapes the app expects (see table below) ---
  const cycles = [ /* { id, startDate, durationDays, corrected, auto, predictedStart? }, ... */ ];
  const predictions = [ /* { predictedStart, predictedDuration }, ... */ ];

  const realFetch = window.fetch.bind(window);
  window.fetch = (url, opts) => {
    const u = typeof url === 'string' ? url : (url && url.url) || '';
    if (/\/cycles(\?|$)/.test(u))
      return Promise.resolve(new Response(JSON.stringify(cycles),      { status: 200, headers: { 'Content-Type': 'application/json' } }));
    if (/\/predictions(\?|$)/.test(u))
      return Promise.resolve(new Response(JSON.stringify(predictions), { status: 200, headers: { 'Content-Type': 'application/json' } }));
    return realFetch(url, opts);   // anything else → real backend (see the 401 gotcha below)
  };
})();
```

**Patch `window.fetch`, not the app's wrapper.** ThoseDays routes its API calls
through `apiFetch` (`frontend/src/lib/api.ts`), which attaches the bearer token and
then calls the global `fetch` — so patching `window.fetch` still intercepts them.
If an app calls `fetch` directly that's also fine; either way you patch one place.

> **Gotcha — the 401 redirect.** `apiFetch` treats a `401` as an expired session:
> it clears storage and hard-navigates to `/` (kicking you back to login). With a
> **real backend running**, your fake `preview-token` is invalid, so *every
> un-stubbed* `apiFetch` call (reconcile, stats, prefs, …) 401s and bounces you
> out — you'll see the page flicker back to login. Two ways to avoid it:
> 1. **Run truly front-end-only** (no backend): un-stubbed calls reject with a
>    network error, which the page's `try/catch` swallows as empty state — no 401,
>    no redirect. This is the simplest setup.
> 2. **Stub every `apiFetch` endpoint** the screen *and the app shell* touch (the
>    shell calls reconcile + stats on load), so none reach the backend to 401.

### ThoseDays endpoint/response shapes

| Endpoint (per `frontend/src/pages/StatsPage.tsx`) | Shape |
|---|---|
| `GET /api/user/:id/cycles` | `Array<{ id, startDate, durationDays, corrected, auto, predictedStart? }>` (`CycleRecord` in `frontend/src/lib/stats.ts`) |
| `GET /api/user/:id/predictions` | `Array<{ predictedStart, predictedDuration }>` |
| `GET /api/config` | `RecalcConfig` (`frontend/src/lib/predictions.ts`); the page falls back to a default if absent, so it's fine to leave unstubbed |

To make the Statistics charts look realistic, generate ~12–14 cycles spanning a
year with intervals that vary around 28 days and durations of 4–6 days. Mark one
cycle `corrected` (with a `predictedStart` a day or two off) so the **prediction
accuracy** section has something to show, and make the last one or two `auto` so
the **current cycle** / **next predicted** sections populate.

## Step 3 — make the page actually re-fetch

Pages fetch inside a mount `useEffect`, so a screen that's **already mounted won't
re-run** its loader after you install the patch. Force a fresh mount with a
**client-side route bounce** (this keeps the patch — it's only in page memory):

```js
// e.g. go to Calendar then back to Statistics via the in-app nav links
[...document.querySelectorAll('a')].find(a => a.getAttribute('href') === '/')?.click();
[...document.querySelectorAll('a')].find(a => a.getAttribute('href') === '/stats')?.click();
```

Do **not** use `location.reload()` / F5 here — a full reload wipes the `fetch`
patch (see caveats).

## Caveats

- **In-memory only.** The `fetch` patch lives in the page's JS heap; a hard reload
  (F5/Ctrl-R) removes it and you're back to the real (missing) backend. The
  `localStorage` session *does* survive reloads — only the data stub doesn't.
- **This tab/preview session only.** Nothing is written to the DB or to source.
- **Controlled inputs.** If you go through the real login *form* instead of the
  `localStorage` bypass, note React inputs are controlled — setting `el.value`
  doesn't fire `onChange`. Use a native setter + dispatched `input` event, or
  `.click()` real elements. The Step 1 bypass sidesteps the form entirely.

## Adapting to other apps

The pattern generalizes; per app, check:

1. **How is the auth gate satisfied?** If the client validates the stored token
   against the backend on load (e.g. calls `/me` or refreshes the token at mount),
   the `localStorage` bypass alone won't work — you must **also stub that auth
   endpoint** in the Step 2 `fetch` patch (return a fake user/200).
2. **Which endpoints does the target screen hit, and in what shape?** Read the
   page's loader; stub exactly those, pass the rest through.
3. **What HTTP layer does it use?** This patches the global `fetch`. If the app
   uses `axios` or another client, patch that instead (e.g. an axios mock adapter,
   or intercept `XMLHttpRequest`).
4. **Re-fetch trigger.** Mirror Step 3 — remount the view however that app routes.

### If this becomes a recurring need

The in-memory patch is deliberately throwaway. For a reusable, reload-surviving
setup, prefer a real mocking layer over hand-patching `fetch`:

- **[MSW (Mock Service Worker)](https://mswjs.io/)** — intercepts at the network
  layer via a service worker, survives reloads, works across `fetch`/`axios`, and
  the same handlers can back component tests. This is the clean cross-app answer.
- A **`?mock=1` dev flag** wired into the app's API client to swap in fixtures.
- A **Vite dev-only middleware** that serves fixture JSON for the API routes.
