# Changelog

All notable changes to Shield will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.0] - unreleased

### Added

- **Multi-signal supply-chain anomaly detection** — typosquat scoring now reads from
  `PackageMeta` data (weekly downloads + publish age + maintainer churn) instead of a
  curated allowlist. A name-similar candidate fires only when its own download count is
  below the popularity floor and it's brand-new / single-maintainer. The previous
  hardcoded list false-positived on legitimate top-tier packages that happened to be 2
  edits from a popular name (`y18n` vs `yarn`); the new scoring would not.
- **Registry feed sync for 7 ecosystems** — npm / NuGet / crates.io / PyPI / RubyGems /
  Packagist / Hex. Each pulls package metadata + weekly download counts into
  `FeedsDb.PackageMetas`. EF-backed sinks replace the in-memory placeholders that
  silently dropped every sync. `EfPackageNameSource<TTag>` filters inventory to one
  ecosystem per feed via a generic marker tag (`EcosystemTag.Npm`, …).
- **Multi-host source types** — `SourceType` enum extended with `GitlabRepo`,
  `BitbucketRepo`, `ForgejoRepo`, `GiteaRepo`, `CodebergRepo` alongside the existing
  `GithubRepo`. SPA renders correct repo URLs (per-host tree/branch path conventions)
  and surfaces an "Open repo" button on source detail.
- **Cloudflare `CF-Connecting-IP` extraction** — middleware walks
  `CF-Connecting-IPv6` → `CF-Connecting-IP` → `True-Client-IP` → `X-Real-IP` →
  `X-Client-IP` → `X-Forwarded-For` → `Forwarded-For` → `Forwarded` and picks the first
  public IP. Audit logs, security events, IP reputation, push subscriptions, and rate
  limiting all see the real client IP instead of the proxy peer. `CF-Visitor` is honoured
  for scheme upgrade.
- **Sortable table headers** — `useClientSort` composable + `<SortableTh>` component
  give every Shield table click-to-sort headers with `aria-sort` wiring and ▲/▼
  indicators. Sort preference persists per-table via `localStorage`. Default
  directions: names ascending, dates/counts/scores descending.
- **Periodic synthetic-advisory prune** — `SyntheticAdvisoryPruneWorker` runs every 6
  hours and deletes synthetic anomaly advisories whose package no longer appears in any
  source's inventory. Closes the cross-DB orphan loop the source-delete cascade can't
  reach.
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

- **Session-replay security event split** — `session.replay` separated into
  `session.revoked_cookie_replay` (High, real session-theft signal) and
  `session.stale_cookie_presented` (Low, cookie outlived its row after admin wipe or
  pruning — operator action, not an attack). Each `eventType` now resolves to a
  human-readable title + body via `security.event_label.*` and `security.event_body.*`
  i18n keys; the Security view renders the friendly label with the technical slug as a
  tooltip.
- **Runtime upgraded to .NET 10** (was .NET 9). Minimum SDK: 10.0.100 (see `global.json`).
- `SingleUserAuthHandler` now stamps a `shield.auth.single-user` marker claim so
  `SessionTrackingMiddleware` can distinguish it from a real cookie-auth principal, fixing a
  CSRF / session-cookie misidentification 401 regression.
- Parser and feed projects consolidated into `Shield.Parsers` and `Shield.Feeds` assemblies,
  reducing solution complexity.
- `SettingsController` no longer falls back to `new HttpClient()` — all HTTP clients go
  through the named-client factory with the canonical User-Agent applied.

### Fixed

- **Push subscription self-heal** — when notification permission is revoked after a
  subscribe, the SPA detects the mismatch (server row + dead local subscription) on
  mount / visibility / focus and clears both sides so the Enable button reappears.
  Subscribe flow unsubscribes any stale local subscription before re-registering so
  the new VAPID handshake is clean.
- **Findings deep-link filter reset** — clicking a notification into `/findings?…`
  now clears persisted `localStorage` filters; a stale "Critical only" pin no longer
  hides the High-severity finding the link is pointing at.
- **`@{user}` and email-address placeholders in i18n** — vue-i18n's message compiler
  treats bare `@` as a linked-reference (`@:key` / `@.modifier:key`). Catalog now
  escapes with `{'@'}` for impersonation messages and email placeholders; a
  `lint:locales` script in the build catches future drift.

### Security

- Pinned `System.Security.Cryptography.Xml` to 10.0.6 to address GHSA-37gx-xxp4-5rgx and
  GHSA-w3x6-4m5h-cxqf.
- Pinned `System.Linq.Dynamic.Core` to 1.7.2 to address GHSA-4cv2-4hjh-77rx (transitive
  via `WireMock.Net`).
- CI gates: `dotnet list package --vulnerable --include-transitive` after restore +
  `npm audit --audit-level=high` before SPA build so future transitive advisories fail
  loudly instead of being buried in build warnings.

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
