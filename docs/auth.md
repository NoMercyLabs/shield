# Auth

Shield's auth stack supports solo installs without any configuration and full multi-user
deployments with roles, TOTP, API tokens, invite flows, and GitHub OAuth sign-in.

## Single-user mode

```env
Shield__SingleUser=true
```

When enabled, `SingleUserAuthHandler` auto-authenticates every request as an Admin-seeded
local user (`single-user@shield.local`). No login screen, no password, no session to manage.

The handler stamps a marker claim (`shield.auth.single-user`) on the synthetic principal so
`SessionTrackingMiddleware` — which ordinarily requires the `shield.session` session cookie —
can tell the difference and let SingleUser requests through without a session row.

**When to use it:**

- Running Shield on `localhost` or behind a VPN with your own auth-enforcing proxy.
- You're the only person who will ever touch this instance.

**When NOT to use it:**

- The Shield port is reachable from the public internet without an auth-enforcing proxy.
  `ProductionSafetyGate` blocks startup when `Shield__SingleUser=true` and
  `Shield__Auth__AllowSingleUserInProduction` is not explicitly set to `true`.

## Multi-user mode

```env
Shield__SingleUser=false
```

ASP.NET Core Identity handles authentication:

- Cookie auth (`shield.auth`) for the SPA — issued by `SignInManager` on login.
- Session tracking via a second opaque cookie (`shield.session`) — issued by
  `SessionCookieIssuer` on every sign-in path (password, invite accept, OAuth). The
  `SessionTrackingMiddleware` reads this cookie to coalesce `LastActiveAt` writes and
  enforce revocation. A missing or revoked `shield.session` cookie with a live `shield.auth`
  cookie returns 401 and signs the user out.
- JWT bearer for API clients — signed with `Shield__Auth__JwtSigningKey`.
- TOTP 2FA enrollment and verification endpoints.
- Lockout on repeated failed sign-ins.
- Roles: `Admin`, `Maintainer`, `Viewer`.

### First user

The first account registered via `POST /api/auth/register` becomes `Admin` automatically
(first-user-wins). Subsequent registrations land as `Viewer`.

## API tokens (`shld_` prefix)

Operators can mint scoped, long-lived tokens without exposing their main credentials.

```
POST /api/api-tokens
{ "name": "CI scanner", "scopes": ["findings:read", "sources:read"], "expiresInDays": 90 }
```

The plaintext token (`shld_…`) is returned **once** at creation time and hashed at rest.
Subsequent reads return a summary with the first 8 characters (`prefix`) for display only.

Available scopes: `findings:read`, `findings:write`, `sources:read`, `sbom:write`.

Tokens also accept an optional `sourceIdFilter` list to restrict which sources the token can
see — independently of the owning user's existing ACL.

Send as `Authorization: Bearer shld_<token>` on API requests. Channel and settings mutations
are blocked for API token principals (`[NoApiToken]` attribute on those controllers).

## Invite flow

Admins invite users via `POST /api/access/invite`. A time-limited token (7-day TTL) is
emailed to the invitee. The invite can carry a target role and source group membership.
Invitees accept at `/accept-invite?token=<token>`, which creates their `ShieldUser` row.

Invites can also be pre-bound to a GitHub identity — the invitee's first sign-in via GitHub
OAuth is automatically linked to the pre-bound account without a separate connect step.

## Impersonation

Admins can temporarily sign in as another (non-admin) user via
`POST /api/impersonation/start`. A short-lived `shield.impersonate` cookie carries the
payload; `ImpersonationMiddleware` swaps the `HttpContext.User` for the target on every
subsequent request. The impersonating admin's identity is preserved as claims
(`imp.admin.name`) so the audit log always attributes back to the real actor.
End impersonation with `POST /api/impersonation/stop`.

## GitHub OAuth (device flow + sign-in)

**As an integration** (connect a GitHub token for webhook comments, org-membership checks):
Shield ships with a published OAuth App client ID (`Ov23libI6hv5NBmkaxjV`) and uses
GitHub's Device Flow. Visit `https://github.com/login/device`, enter the code, and Shield
finishes the handshake server-side. Override the baked-in client ID with:

```env
Shield__OAuth__GitHub__DefaultClientId=<your-client-id>
```

**As a sign-in method**: GitHub can also be used as a primary identity provider. When an
OAuth App with a client secret is configured under **Settings → Integrations → GitHub**,
a **Sign in with GitHub** button appears on the login page. This uses an authorization-code
flow and creates (or links to) a `ShieldUser` row on first sign-in.

Disable device flow with:

```env
Shield__OAuth__GitHub__DeviceFlow__Enabled=false
```

## Cookies

| Cookie | Purpose |
|---|---|
| `shield.auth` | ASP.NET Identity application cookie — carries the authenticated session |
| `shield.session` | Opaque Shield session row token — used by `SessionTrackingMiddleware` for revocation and `LastActiveAt` tracking |
| `shield.impersonate` | Short-lived impersonation payload, AES-GCM protected |

## Data protection keyring

Shield wraps Discord webhook URLs, OAuth client secrets, SMTP passwords, and OIDC client
secrets through ASP.NET's `IDataProtection`. The keyring lives on the `shield-data` volume
at `/app/data/keys/`.

```env
Shield__Auth__DataProtectionMasterKey=<32+ char secret>
```

Required outside `Development`. Missing or empty key prevents startup. Generate with:

```bash
openssl rand -base64 48
```

A stolen DB file is unreadable without the master key. A recreated container with the same
key + the same volume decrypts everything exactly as before. Losing the key invalidates every
encrypted value and every active session cookie.

## JWT signing key

```env
Shield__Auth__JwtSigningKey=<48+ char secret>
```

Minimum 32 chars; `ProductionSafetyGate` refuses shorter keys in Production. Used to sign
bearer tokens returned by `POST /api/auth/login` and minted during OAuth callbacks.

## Audit log

Every admin-significant write — finding ack/resolve/suppress, source/channel mutations,
settings updates, OAuth connect/disconnect, session create/revoke, invite send/accept/revoke,
impersonation start/stop — is appended to `AuditEntries`. Access via
`GET /api/audit?page=&pageSize=&action=&targetType=` (Admin only) or the **Audit** page
in the SPA.

## Cloudflare Tunnel deployment

See `docs/deploy.md` for the full setup. Required env vars when fronting Shield with a
Cloudflare Tunnel:

```env
Shield__Public=true
Shield__Auth__RequireHttps=true
Shield__Auth__CookieDomain=shield.example.com
Shield__Auth__DataProtectionMasterKey=<32+ char secret>
Shield__Auth__JwtSigningKey=<48+ char secret>
Shield__ForwardedHeaders__KnownProxies=127.0.0.1
```

`ProductionSafetyGate` refuses to start if any of these are missing or below the minimum
when `ASPNETCORE_ENVIRONMENT=Production`.
