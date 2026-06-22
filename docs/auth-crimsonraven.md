# Auth — CrimsonRaven SSO (dual-auth, data-preserving)

> **Update (2026-06-22): CrimsonRaven is Keycloak now.** It hosts the themed login + **native
> email-verification, resend and forgot-password**, so the Zitadel-era app-side workarounds were
> ripped out: the unverified-email **hold middleware**, the **resend** endpoint + `app-mailer` PAT,
> and the IdP **logo-scrape** are gone, and `OidcUserProvisioner` now **links by email
> unconditionally** (Keycloak's `verifyEmail` is the sole gate). See the CrimsonRaven repo's
> `docs/keycloak-migration.md`. The dual-auth / break-glass design below still holds.

> Status: **live (clients registered, dual-auth).** The app accepts **both** CrimsonRaven
> (OIDC) tokens **and** its own signed HMAC JWT. The login screen is **CrimsonRaven-first**:
> when the IdP is reachable the user is sent straight to it; the email/password form is only
> shown when CrimsonRaven is offline/unconfigured (break-glass), with a maintenance notice.
> Decommissioning the local path is a deliberate later step.
>
> (The app is branded **Rosella Rhythm**; the repo/code namespace stays `thosedays` / `Api`.
> Prod is served at **`rosella.bearsoft.duckdns.org`**.)

## Why

Move authentication to **CrimsonRaven** (self-hosted Zitadel IdP) for SSO across homelab
apps, MFA/TOTP, Google login, and refresh/sessions — without dropping the working local
login or, critically, **losing any existing user's data**. This also closes a pre-existing
hole: before this change the backend validated nothing and trusted the `{userId}` in the
route, so any user could read another user's data by editing the URL.

## Identity model — link-by-email (the load-bearing bit)

ThoseDays' `User.Id` (GUID) owns all rows (`Cycle`, `Prediction`, prefs). That id must never
change, so the IdP `sub` is **mapped onto** the existing user rather than replacing it:

- `User.ExternalSubject` (nullable, unique) stores the CrimsonRaven `sub` once linked.
- `OidcUserProvisioner` (`IClaimsTransformation`, `Services/OidcUserProvisioner.cs`) runs
  per request for a CrimsonRaven principal and:
  1. finds the user by `ExternalSubject`; else
  2. if the token's email is **verified**, links by case-insensitive email to the existing
     row (this is what preserves cycles/predictions/prefs); else
  3. creates a new password-less user.
- It then rewrites the principal's `sub` to the ThoseDays `User.Id`, so
  `ResourceOwnershipFilter` and every `api/user/{userId}/...` route are **unchanged**.
- Idempotent: a GUID `sub` (locally-issued token, or already-mapped) is a no-op.

**Users are not copied into CrimsonRaven and passwords are not migrated** (the PBKDF2 hashes
aren't a Zitadel-verifiable format). Existing users **self-register** in CrimsonRaven with the
same email (or use Google); first login links by verified email. A user who hasn't verified
their email is treated as new (empty) until they do.

## Backend

- **Dual bearer schemes** (`Program.cs`): `"ThoseDays"` (HMAC, `JwtTokenService`) and
  `"CrimsonRaven"` (`AddJwtBearer` against `OIDC_AUTHORITY`/JWKS, `aud = OIDC_AUDIENCE`). A
  `"smart"` policy scheme is the default and forwards by peeking the bearer's `iss`. A
  default-deny `FallbackPolicy` requires auth everywhere; both schemes satisfy it.
- **`ResourceOwnershipFilter`** (global): a token for user A is rejected (403) on any route/
  query `userId` that isn't A's own.
- `RequireHttpsMetadata` is derived from the authority scheme (http homelab vs https).
- OIDC is **opt-in**: blank `OIDC_AUTHORITY` → only the local scheme runs.
- **`GET /api/auth/me`** (authenticated) returns `{ userId, email, notifyReleases }` — the SPA
  calls it after the OIDC callback to learn its ThoseDays `User.Id`.
- **`GET /api/config`** (anonymous) now returns the recalc constants **and** the OIDC fields
  `{ oidcEnabled, oidcOnline, oidcAuthority, oidcClientId, oidcLogoUrl, oidcLogoUrlDark }`
  (cached IdP liveness probe + scraped login logo).
- `User.PasswordHash` is now **nullable** (OIDC accounts have no local password). The local
  login rejects password attempts against password-less accounts.

## Frontend

- `oidc-client-ts` `UserManager`, configured at runtime from `/api/config` (`src/lib/oidc.ts`).
  Authorization Code + PKCE, `scope "openid profile email offline_access"`, silent renew.
- `apiFetch` (`src/lib/api.ts`) attaches the bearer to every app API call and, on a 401, clears
  the session and bounces to login. All `api/user/{userId}/...` calls go through it; the export
  download fetches the blob via `apiFetch` (an `<a href>` can't carry the token).
- `AuthContext` exposes `loginWithSSO()`, `completeSsoCallback()`, and the state
  `ssoOnline` / `ssoConfigured` / `authReady`. The `/auth/callback` route (`AuthCallbackPage`,
  switched in `App.tsx` before the login gate) finishes the code exchange, calls `/api/auth/me`,
  and stores `token` + `user`. **Logout** calls `signoutRedirect()` when an OIDC session exists.
- `LoginPage` is **CrimsonRaven-first**: when `authReady && ssoOnline` it auto-redirects via
  `loginWithSSO()`. The legacy email/password form is reached **only** when CrimsonRaven is
  down/unconfigured, then with a maintenance note.

## Config (flat env, per stack)

| Key | Meaning |
|---|---|
| `JWT_SIGNING_KEY` | HMAC key for the local login (≥32 chars; blank → ephemeral, logs out on restart). |
| `JWT_EXPIRY_DAYS` | Local-token lifetime (default 30). |
| `OIDC_AUTHORITY` | IdP issuer; must equal the token `iss`. Blank → SSO off. |
| `OIDC_CLIENT_ID` | Rosella Rhythm public/PKCE client id in CrimsonRaven. |
| `OIDC_AUDIENCE` | Expected `aud` in the access token (== client id for a PKCE app). |
| `OIDC_LINK_ALLOW_UNVERIFIED_EMAIL` | TEMP: link by unverified email (default false). |

## Org isolation + the Login-V2 branding limitation (PARKED)

Rosella lives in its **own CrimsonRaven org** (not the default org Fuel uses), and users stay
**shared across the whole instance**: a person registers once and SSO works into every app
(Fuel included), because we **don't** send an org scope and the project's authorization check is
**off** (`hasProjectCheck=false`). That part works and is verified (a default-org user logs
straight into the Rosella app).

**What does NOT work — per-app login-page branding (parked, pending investigation):** the plan
was to brand Rosella's login via the project's
`PRIVATE_LABELING_SETTING_ENFORCE_PROJECT_RESOURCE_OWNER_POLICY`. **Stock Login V2 ignores that
setting** — it renders only the **instance** label policy. The only first-screen override is the
org scope (`urn:zitadel:iam:org:id|domain`), which **also restricts login to that org's members**
→ that would break shared users, so we don't use it. Net: **in Login V2, a fixed per-app login
page and instance-wide shared users are mutually exclusive.** (Full write-up:
`CrimsonRaven/docs/zitadel-customization.md` → "Stock V2's hard limits".) Decision for now: keep
the org + shared users; the login shows CrimsonRaven's instance brand (normal for a shared SSO
IdP). Per-app branding would need a V2 fork.

> NOTE: `has_project_check=true` + a **project grant** to an org gates access per-org without an
> org scope (verified working) — but that's access control, not branding, and adds grant admin.

## Registered CrimsonRaven clients

Both are **public PKCE** clients (`app_type USER_AGENT`, `auth_method NONE`, grants code +
refresh, **JWT access tokens** — required so the backend can validate them against the JWKS),
in a dedicated **`Rosella Rhythm` org** per instance. `OIDC_CLIENT_ID == OIDC_AUDIENCE`.

| | Staging / local (`raven-staging…`) | Prod (`raven…`) |
|---|---|---|
| Org `Rosella Rhythm` | `378269015241392131` | `378269526107619332` |
| Project (no project check) | `378269015878926339` | `378269526778707972` |
| App | `378269016482906115` | `378269527349133316` |
| **Client id** | **`378269016482971651`** | **`378269527349198852`** |
| Redirect URIs | `http://localhost:3000/auth/callback`, `http://127.0.0.1:3000/auth/callback`, `https://thosedays-staging.bearsoft.duckdns.org/auth/callback` | `https://rosella.bearsoft.duckdns.org/auth/callback` |
| Post-logout | `http://localhost:3000`, `http://127.0.0.1:3000`, `https://thosedays-staging.bearsoft.duckdns.org` | `https://rosella.bearsoft.duckdns.org` |
| dev_mode | true | false |

The committed `deploy/.env.staging.example` / `.env.prod.example` carry these ids (the staging
client is also the local-dev client).

### HTTPS is required for both deployed stacks (not just prod)

Browser PKCE (`crypto.subtle`) only runs in a **secure context**, so a stack served as plain
http (e.g. LAN `http://192.168.4.55:9123`) **breaks the CrimsonRaven redirect**. Both staging
and prod must be served over HTTPS, fronted by the `.98` nginx (TLS via the
`*.bearsoft.duckdns.org` wildcard cert) — same pattern as fuel's `fuel-staging` / `swallow`:

| Host (HTTPS) | nginx `.98` → backend |
|---|---|
| `thosedays-staging.bearsoft.duckdns.org` | `192.168.4.55:9123` (staging `APP_PORT`) |
| `rosella.bearsoft.duckdns.org` | `192.168.4.55:9124` (prod `APP_PORT`) |

The redirect URIs are pre-registered on the clients, so the only remaining edge work per host is
the nginx server block (copy fuel's `swallow` block; change `server_name` + `proxy_pass`) and
confirming DNS resolves. Set the stack's `PUBLIC_BASE_URL` to the HTTPS host.

### Enabling SSO for local dev

Committed `appsettings*.json` keep `OIDC_*` blank (local login by default). To exercise
CrimsonRaven locally, run the backend with the staging client:

```
OIDC_AUTHORITY=https://raven-staging.bearsoft.duckdns.org \
OIDC_CLIENT_ID=378269016482971651 OIDC_AUDIENCE=378269016482971651 \
dotnet run --project backend/Api
```

The Vite dev server's `http://localhost:3000/auth/callback` is registered, so the full PKCE
round-trip works against a real CrimsonRaven account (with a verified email, for link-by-email).

## Verify

- `dotnet test backend/ThoseDays.slnx -c Release` — `JwtTokenServiceTests`,
  `OidcUserProvisionerTests` (link/verify/idempotency), `ResourceOwnershipFilterTests`.
- `npm test --prefix frontend -- --run` — `LoginPage.test.tsx` (SSO redirect + fallback form).
- End-to-end (needs the registered client + a CrimsonRaven account with a verified email): SSO
  redirect → CrimsonRaven login → `/auth/callback` → calendar shows that user's existing data;
  the local email/password login still works with `OIDC_AUTHORITY` blank.

## Open / later

- **PARKED — per-app login branding** (see "Org isolation…"). Stock Login V2 ignores org/project
  private labeling; revisit when investigating a V2 fork or an upstream fix.
- **Edge work per deployed stack**: an `.98` nginx vhost serving the HTTPS host → the stack's
  `APP_PORT`. The OIDC redirect URIs are already registered on both clients.
- **Phase 4 cutover** (separate): remove `JwtTokenService`, password logic, `/api/auth/*` (local),
  the `"ThoseDays"` scheme, and drop `PasswordHash`.
