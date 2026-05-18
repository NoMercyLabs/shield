# Changelog

All notable changes to Shield will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.0] - unreleased

### Added

- **Security command center** — `/security` page aggregates fail2ban log events via an
  observer pattern. Shield reads and surfaces events; fail2ban remains the enforcer.
  Suspicious login attempts, lockouts, and revoked-session replays are logged as
  `SecurityEvent` rows and streamed to the SPA via the real-time notification bus.
- **KEV + EPSS enrichment feeds** — scheduled workers pull the CISA KEV catalog and FIRST
  EPSS scores; matching advisory rows are stamped with `IsKev`, `KevAddedAt`, `KevDueDate`,
  `EpssScore`, and `EpssPercentile` so findings surfaces exploit-context without manual
  lookup.
- **Canonical outbound User-Agent** — all HTTP clients share `Shield/{version}
  (+https://github.com/nomercylabs/shield)` via `ConfigureHttpClientDefaults`.
- **Slack and generic Webhook channels** — two new channel types join Discord, Ntfy, SMTP,
  and the in-app Inbox. Slack supports both legacy Incoming Webhooks and OAuth
  (`chat.postMessage`). Webhook supports arbitrary HTTP targets with custom headers and body
  templates.
- **Per-source ACL** — `SourceGroup` + `GroupMembership` + `SourceAccess` tables let admins
  restrict which sources Viewer/Maintainer users can see.
- **Invite + impersonation** — token-based email invites (7-day TTL, resendable, revocable)
  create users on acceptance. Admins can impersonate non-admin users via a short-lived
  `shield.impersonate` cookie with full audit trail.
- **API tokens** (`shld_` prefix) — scoped, expirable personal access tokens for CI and
  automation. Scopes: `findings:read`, `findings:write`, `sources:read`, `sbom:write`.
  Optional per-token source filter narrows visibility independently of role ACL.
- **Session tracking** — every sign-in (password, OAuth, invite accept, recovery code)
  creates a `UserSession` row. Password change hard-revokes sibling sessions. Revoked-cookie
  replay is detected and logged as a high-severity security event.

### Changed

- **Runtime upgraded to .NET 10** (was .NET 9). Minimum SDK: 10.0.100 (see `global.json`).
- `SingleUserAuthHandler` now stamps a `shield.auth.single-user` marker claim so
  `SessionTrackingMiddleware` can distinguish it from a real cookie-auth principal, fixing a
  CSRF / session-cookie misidentification 401 regression.
- Parser and feed projects consolidated into `Shield.Parsers` and `Shield.Feeds` assemblies,
  reducing solution complexity.
- `SettingsController` no longer falls back to `new HttpClient()` — all HTTP clients go
  through the named-client factory with the canonical User-Agent applied.

### Security

- Pinned `System.Security.Cryptography.Xml` to 10.0.6 to address GHSA-37gx-xxp4-5rgx and
  GHSA-w3x6-4m5h-cxqf.

## [0.1.0-alpha.1] - 2026-05-16

First public alpha. Scope = Phase 1 of the design spec.

### Added

- **Sources**
  - GitHub repo source with optional PAT (Octokit, Git Trees + Blob API, no clone)
  - Local folder source with default ignore list (`node_modules`, `vendor`, `bin`, `obj`, `.git`)
  - Per-source scan interval, on-demand `scan-now` endpoint
- **Parsers**
  - npm: `package-lock.json` (v1/v2/v3), `yarn.lock`, `pnpm-lock.yaml`
  - NuGet: `packages.lock.json` with `*.csproj` `<PackageReference>` fallback
  - Composer: `composer.lock` with direct vs dev split
  - Gradle: `gradle.lockfile` plus best-effort `build.gradle(.kts)` fallback
- **Feeds**
  - OSV.dev (incremental by `modified` timestamp, no key)
  - GitHub Advisory Database via Octokit GraphQL (PAT)
  - npm registry per-package metadata for maintainer drift / deprecation
- **Matcher**
  - Version-range evaluation per ecosystem (npm semver, NuGet `[1.0,2.0)`, composer, gradle)
  - Deterministic dedup key `sha256(sourceId | ecosystem | package | advisoryExternalId)`
  - Maintainer drift detector emits synthetic advisories from npm registry data
- **Channels**
  - Discord webhook (severity-coloured embeds)
  - In-app inbox (always on, persists until cleared)
  - 60-second drain worker with batching when 5+ findings land in one window
- **API**
  - `Sources`, `Findings`, `Channels`, `Feeds`, `Dashboard`, `Auth` controllers
  - OpenAPI exposed at `/swagger` in Development
  - `GET /healthz` for container probes
- **Auth**
  - ASP.NET Core Identity with cookie auth (UI) + JWT bearer (API clients)
  - `Shield:SingleUser=true` middleware short-circuits to Admin for solo installs
  - TOTP enrollment + verification endpoints (multi-user mode)
- **UI**
  - Vue 3 + Moooom + Tailwind v4 SPA built into `wwwroot`
  - Dashboard, Sources, Findings, Channels, Feeds, Settings pages
- **Distribution**
  - Multi-arch Docker image (`linux/amd64`, `linux/arm64`) at `ghcr.io/nomercylabs/shield`
  - SBOM attached to GitHub release artifact

### Known gaps (planned for Phase 2)

- Host scanning (agent + SSH)
- Trivy DB, deps.dev, Socket.dev feeds
- ntfy + SMTP channels
- OIDC plugin
- Encrypted channel configs at rest
- Registration UI for multi-user mode

[0.1.0-alpha.1]: https://github.com/nomercylabs/shield/releases/tag/v0.1.0-alpha.1
