# Shield

Self-hosted dependency vulnerability watcher. Cross-ecosystem. Cross-host. Team-aware.

[![CI](https://github.com/nomercylabs/shield/actions/workflows/ci.yml/badge.svg)](https://github.com/nomercylabs/shield/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![Docker pulls](https://img.shields.io/docker/pulls/nomercylabs/shield?logo=docker)](https://github.com/nomercylabs/shield/pkgs/container/shield)

## What it does

Shield watches the lockfiles in the GitHub repos and local folders you point it at, joins them against advisories from OSV.dev, the GitHub Advisory Database, the npm registry, CISA's Known Exploited Vulnerabilities catalog, and EPSS, then routes alerts to Discord / Slack / ntfy / SMTP / generic webhooks / an in-app inbox / mobile push the moment a match shows up.

One Docker image. One SQLite volume. One pane of glass for the entire team — admin grants per-source access, invites collaborators via GitHub identity, and impersonates them when they say something's broken.

## Coverage

**Ecosystems (13):** npm, NuGet, Composer, Gradle, Maven, Python, Go, Rust, Ruby (Bundler), Swift (SwiftPM), Dart/Flutter (pub), Elixir (Hex), C/C++ (vcpkg).

**Advisory feeds:** OSV.dev (cross-ecosystem), GitHub Advisory Database, npm registry (deprecation + maintainer drift), CISA KEV (known exploited), EPSS (exploit probability scores).

**Alert channels:** Discord webhooks, Slack apps, ntfy, SMTP, generic outbound webhook, in-app inbox, mobile Web Push.

**Identity:** ASP.NET Identity with single-user mode, multi-user with role-based ACL (Admin / Maintainer / Viewer), GitHub OAuth signin (auth-code popup AND device flow), session revocation, 2FA TOTP, API tokens (scope-bound, source-filtered).

## Install from GHCR

Images are published to `ghcr.io/nomercylabs/shield` on every tagged release. No account
or token needed to pull — the package is public.

**Docker run (quickest):**

```bash
docker run -d --name shield \
  -p 127.0.0.1:8842:8842 \
  -v shield-data:/app/data \
  -e Shield__Auth__JwtSigningKey="$(openssl rand -base64 48)" \
  -e Shield__Auth__DataProtectionMasterKey="$(openssl rand -base64 36)" \
  ghcr.io/nomercylabs/shield:latest
```

**Docker Compose (recommended for production):**

```yaml
services:
  shield:
    image: ghcr.io/nomercylabs/shield:latest
    container_name: shield
    restart: unless-stopped
    ports:
      - "127.0.0.1:8842:8842"
    volumes:
      - shield-data:/app/data
    env_file:
      - .env

volumes:
  shield-data:
```

Copy `docker/.env.example` to `docker/.env`, fill in `Shield__Auth__JwtSigningKey` and
`Shield__Auth__DataProtectionMasterKey` (see [`docs/deploy.md`](docs/deploy.md)), then
`docker compose up -d`.

Open <http://localhost:8842>. Single-user mode is on by default — first request lands in
the dashboard. Pick a source via GitHub (one-click via the bundled OAuth App, no
registration needed) or browse a local folder, configure a channel, the next scan tick
populates findings.

**Save both env vars somewhere safe.** `Shield__Auth__DataProtectionMasterKey` encrypts
the keyring under `/app/data/keys` and must be supplied on every container restart —
recreate the container without it and every channel config, OAuth token, and OIDC client
secret becomes permanently unreadable.

Available tags:

| Tag | What you get |
|-----|-------------|
| `latest` | Latest stable release |
| `v0.1` | Latest patch in the 0.1 minor line |
| `v0.1.0` | Exact release |
| `v0.1.0-alpha.1` | Pre-release (not tagged `latest`) |

For production behind a tunnel/proxy see [`docs/internet-exposure.md`](docs/internet-exposure.md).

## Build from source

Clone the repo and build the image locally — useful for contributors or air-gapped hosts:

```bash
git clone https://github.com/nomercylabs/shield.git
cd shield/docker
cp .env.example .env
# fill in .env, then:
docker compose build --pull
docker compose up -d
```

The Compose file's `build:` block points at `docker/Dockerfile`. SPA assets are baked
into the runtime image, so any change under `src/Shield.Web/` requires a rebuild.

## Why Shield

Dependabot is push-style and locked to GitHub. Snyk and Sonatype are SaaS. Trivy is CLI-only. GitHub Advanced Security is enterprise-tier. There's a gap for a self-hosted, cross-host, polished tool that a small team owns end-to-end. That's Shield.

If you ship software you maintain on machines you don't own, you probably need this.

## Docs

- [Getting started](docs/getting-started.md) — Docker quickstart, env vars, first-run walkthrough
- [Deploy](docs/deploy.md) — production deploy on Docker (Proxmox, generic Linux host)
- [Internet exposure](docs/internet-exposure.md) — hardening recipe for public deploys via Cloudflare Tunnel
- [Auth](docs/auth.md) — single-user mode, multi-user, invite flow, OAuth providers, GitHub device flow, API tokens, 2FA
- [Sources](docs/sources.md) — GitHub repo + local folder config schemas, scan cadence
- [Feeds](docs/feeds.md) — what each advisory feed covers, refresh cadence, GHSA token setup
- [Channels](docs/channels.md) — Discord/Slack/ntfy/SMTP/webhook/inbox/push config + payload shapes
- [Architecture](docs/architecture.md) — pipeline, two-DB design, extension points

## License

MIT. See [LICENSE](LICENSE).

## Security

Found a vulnerability? See [SECURITY.md](SECURITY.md).
