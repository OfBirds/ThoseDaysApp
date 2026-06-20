# Stable tenant identity (auth/IdP migrations)

ThoseDays keys all user data on an internal **`User.Id` (GUID)** that we own and never changes. The
OIDC `sub` and the email are *mutable external links*, mapped onto that id by `OidcUserProvisioner`
(`IClaimsTransformation`). This is the cross-app standard; the canonical write-up lives in the
MulberryHeron repo: **`docs/adr/0012-stable-tenant-identity.md`**.

## Login-time resolution (`OidcUserProvisioner`)
1. **Find by `ExternalSubject == sub`** → fast path for returning sessions.
2. Else resolve **email + email_verified** (token claims, else the IdP `userinfo` endpoint — Zitadel
   access tokens omit email).
3. **Verified** email matching an existing row → **link** (`ExternalSubject = sub`); the `User.Id`,
   and all its data, is preserved across the IdP move.
4. **Unverified** email that *already owns a row* → **HOLD**: persist nothing, leave the `sub`
   un-rewritten (downstream `Guid.TryParse` fails → 401/403, a "verify your email" state). A later
   *verified* login self-heals into the row — no admin step, and an unverified address can never claim
   another person's data.
5. Else **create** a new password-less user.

This **replaced** the temporary `OIDC_LINK_ALLOW_UNVERIFIED_EMAIL` escape hatch (which relaxed step 3
to link unverified emails — a security hole that also produced duplicate accounts on cutover). The
config key and its compose/`.env` wiring were removed.

**Primary guard is the IdP:** enforce email-verification-on-registration in Crimson Raven so step 4
rarely fires; the app-side hold is defense-in-depth.

## Migrating to a new IdP/instance — deterministic, workaround-free
Never disable email verification or force a mass re-login to "fix" linking. Instead, an admin
**pre-seeds the identity map** so every login hits the fast `ExternalSubject` path immediately:

1. Export `(email, new_sub)` for every user from the **new** IdP instance.
2. Backfill by verified email (idempotent; run inside a transaction):
   ```sql
   -- for each (email, new_sub) from the new IdP export:
   UPDATE "Users"
      SET "ExternalSubject" = @new_sub
    WHERE lower("Email") = lower(@email)
      AND "ExternalSubject" IS DISTINCT FROM @new_sub;
   ```
3. After the cutover the first login matches on `ExternalSubject` — no dependence on login-time
   email verification, no relog, no duplicate accounts.

## ⚠️ Deploy gap (2026-06-20 audit)
This feature is on `main`/staging but **was never released to prod** — `thosedays-prod` has no
`ExternalSubject` column (latest applied migration predates `…AddExternalSubjectAndNullablePassword`),
so its users are still keyed only on the raw IdP `sub`. They're fine today, but a future `sub` change
would orphan them. **Release the stable-identity work to prod** (which runs the EF migration on boot)
to close this.
