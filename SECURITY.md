# Security policy

## Supported versions

Shield is pre-1.0 and ships from `master`. Until the first tagged release, only `master` receives security patches. After v1.0:

| Version | Supported |
|---------|-----------|
| latest (`master`) | yes |
| previous minor | yes |
| older | no — upgrade |

## Reporting a vulnerability

If you find a security issue in Shield itself (not in a dependency Shield warns *about* — that's the whole point of the product), please **do not** open a public issue.

Email: **security@nomercy.tv**

Include:
- The version / commit you tested against
- A minimal reproduction (HTTP request, lockfile sample, config snippet)
- The impact you observed
- Whether you believe this is exploitable in a default-config deploy

We aim to acknowledge within two working days and ship a patched build for serious issues without delay.

## Scope

In scope:
- Authentication bypass, privilege escalation, impersonation abuse
- Injection (SQL, command, header, CRLF, path traversal)
- Stored credential leaks (DataProtection keyring, OAuth tokens, OIDC client secrets, push VAPID keys, channel config blobs)
- Cross-tenant data leaks (e.g. a Maintainer seeing a finding from a source they weren't granted)
- CSP / CSRF / SameSite weaknesses on the cookie-auth surface
- Rate-limit bypass on `/api/auth/*` and `/api/security/fail2ban/*`
- API token scope confusion, replay after revoke, leak via logging
- Webhook payload injection (Discord/Slack/SMTP/ntfy)
- Push subscription hijack
- ForwardedHeaders / IP-spoofing when a proxy is misconfigured

Out of scope:
- Vulnerabilities in self-hosted dependencies Shield merely runs on (ASP.NET Core, SQLite, etc.) — report those upstream
- Local-only attacks (someone with root on the host)
- Denial-of-service requiring more than 1 RPS from a single client (the rate limiter caps that)
- Self-XSS where the operator pastes attacker-controlled HTML into a config field
- Reports from automated scanners with no exploit detail

## Hardening checklist for operators

The defaults are LAN-safe, not internet-safe. Before exposing Shield publicly:

- Reverse proxy or tunnel terminating TLS in front of Shield
- `Shield__Auth__RequireHttps=true`
- `Shield__Public=true`
- `Shield__SingleUser=false`
- `Shield__OpenApi__Enabled=false`
- `Shield__Auth__JwtSigningKey` ≥ 48 chars (`openssl rand -base64 48`)
- `Shield__Auth__DataProtectionMasterKey` ≥ 32 chars, NOT the dev default
- `Shield__Auth__CookieDomain=<your-hostname>`
- `Shield__ForwardedHeaders__KnownProxies` or `KnownNetworks` pinned to your proxy
- Backups of `/app/data/keys/` AND `/app/data/shield.db`

Shield's `ProductionSafetyGate` refuses to start when those are inconsistent. Read [`docs/internet-exposure.md`](docs/internet-exposure.md) for the full recipe.

## Credit

Researchers who report a verified issue privately will be credited in the release notes for the fix, unless they prefer to remain anonymous.
