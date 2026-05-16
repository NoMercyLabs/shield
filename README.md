# Shield

Cross-ecosystem dependency vulnerability warning system. Self-hosted. Headless API + web UI.

[![CI](https://github.com/nomercylabs/shield/actions/workflows/ci.yml/badge.svg)](https://github.com/nomercylabs/shield/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![Docker pulls](https://img.shields.io/docker/pulls/nomercylabs/shield?logo=docker)](https://github.com/nomercylabs/shield/pkgs/container/shield)

## What it does

Shield watches the lockfiles in your code — every `package-lock.json`, `yarn.lock`, `pnpm-lock.yaml`, `packages.lock.json`, `composer.lock`, and `gradle.lockfile` — across the GitHub repos and local folders you point it at. It pulls advisories from OSV.dev, the GitHub Advisory Database, and the npm registry feed (for maintainer drift and deprecation signals), joins them against your inventory, and pushes alerts to a Discord webhook and an in-app inbox the moment a match shows up. One Docker image, one SQLite volume, one pane of glass.

## Quickstart

```bash
docker run -d --name shield \
  -p 8080:8080 \
  -v shield-data:/data \
  -e Shield__Auth__JwtSigningKey="$(openssl rand -base64 48)" \
  ghcr.io/nomercylabs/shield:latest
# Open http://localhost:8080 — single-user mode is on by default
```

The first request lands you straight in the dashboard. Add a source from **Sources -> New**, configure a Discord webhook from **Channels**, and the next scan tick will start populating findings.

## Roadmap

| Phase | Status | Scope |
|---|---|---|
| Phase 1 | shipping now | GitHub repo + local folder sources, npm/NuGet/Composer/Gradle parsers, OSV + GHSA + npm registry feeds, Discord + in-app inbox, ASP.NET Identity auth with single-user mode, Vue + Moooom UI, single Docker image |
| Phase 2 | planned | Linux host scanning via agent + SSH (dpkg/rpm/apk), Trivy DB feed, deps.dev + Socket.dev feeds, ntfy + SMTP channels, OIDC plugin (Keycloak/Authentik/Auth0/GitHub), inventory diff view, encrypted channel configs |
| Phase 3 | planned | Dev-machine pre-install hook (`nm-shield`), CycloneDX SBOM upload mode, public docs site, hosted SaaS offering |

## Docs

- [Getting started](docs/getting-started.md) — Docker quickstart, env vars, first-run walkthrough
- [Sources](docs/sources.md) — GitHub repo + local folder config schemas
- [Feeds](docs/feeds.md) — what each advisory feed covers
- [Auth](docs/auth.md) — single-user mode, multi-user, OIDC plans
- [Architecture](docs/architecture.md) — pipeline, two-DB design, extension points

## Why Shield

We built Shield because learning about npm worms (Shai-Hulud, s1ngularity, slop-squat campaigns) from a YouTube video is unacceptable for a self-hosted product that ships to other people's hardware. Dependabot is a great push-style PR bot but it doesn't watch hosts, doesn't merge ecosystems, and doesn't fan out to the channels we already pay attention to. Snyk and Socket are excellent tools that we don't fully control. Shield is the boring answer: a single Docker container, our advisory feeds joined against our inventory, alerts to the chat we read.

If you ship software you maintain, you probably need the same thing.

## Not yet

Shield is honest about what hasn't landed yet:

- **No host scanning.** Linux package coverage (Debian/Ubuntu/Alpine/RHEL via dpkg/rpm/apk) is Phase 2. The agent stub exists but the ingest API isn't wired.
- **No OS-level CVE feed.** Trivy DB is Phase 2 — it lights up when host scanning lands.
- **No SBOM upload.** CycloneDX/SPDX ingest is Phase 3.
- **Channel configs aren't encrypted at rest.** The DB column is named `ConfigJsonEncrypted` but Phase 1 stores plaintext. Don't put a Discord webhook URL in Shield that you wouldn't put in a private GitHub repo.
- **OIDC is not implemented.** Auth is ASP.NET Identity (cookie + JWT) with single-user mode. OIDC arrives in Phase 2.
- **No registration UI.** Multi-user mode currently requires a user seeded directly in the database. Use single-user mode for solo installs.
- **No ntfy or SMTP channels.** Discord and in-app inbox only in Phase 1.

## License

MIT. See [LICENSE](LICENSE).
