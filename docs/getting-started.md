# Getting started

This walks you from zero to a running Shield instance with one source, one channel, and your first finding.

## Prerequisites

- Docker (or any container runtime that can run an OCI image)
- About 200 MB of disk for the SQLite databases plus advisory cache (grows with how much you scan)
- Optional: a GitHub Personal Access Token if you want to scan private repos or pull from the GitHub Advisory Database

That's it. No external database, no Redis, no message broker.

## Docker quickstart

```bash
docker run -d --name shield \
  -p 8080:8080 \
  -v shield-data:/data \
  -e Shield__Auth__JwtSigningKey="$(openssl rand -base64 48)" \
  ghcr.io/nomercylabs/shield:latest
```

Open `http://localhost:8080`. Single-user mode is the default — there's no login screen, you land directly on the dashboard.

### Environment variables

Shield reads configuration from `appsettings.json` and overrides from environment variables. The `__` (double underscore) is the .NET convention for nested keys.

| Variable | Default | What it does |
|---|---|---|
| `Shield__SingleUser` | `true` | Skip login entirely. Every request is treated as Admin. Use for solo installs only. |
| `Shield__Auth__JwtSigningKey` | (required) | Symmetric key for JWT bearer tokens (API clients). Minimum 32 characters. Generate with `openssl rand -base64 48`. |
| `Shield__Db__Shield` | `Data Source=data/shield.db` | SQLite connection string for config + state |
| `Shield__Db__Feeds` | `Data Source=data/feeds.db` | SQLite connection string for cached advisory data |
| `Shield__OpenApi__Enabled` | `true` | Expose `/swagger` |
| `Shield__Feeds__Osv__Enabled` | `true` | Run the OSV.dev sync worker |
| `Shield__Feeds__Osv__Cadence` | `00:15:00` | OSV sync interval (TimeSpan format) |
| `Shield__Feeds__Ghsa__Enabled` | `true` | Run the GitHub Advisory sync worker |
| `Shield__Feeds__Ghsa__Cadence` | `00:15:00` | GHSA sync interval |
| `Shield__Feeds__Ghsa__ApiKey` | `""` | GitHub PAT for the GHSA feed (required for that feed) |
| `Shield__Feeds__NpmRegistry__Enabled` | `true` | Run the npm registry feed |
| `Shield__Feeds__NpmRegistry__Cadence` | `00:15:00` | npm registry sync interval |

The mounted `/data` volume holds both `shield.db` (your config + findings) and `feeds.db` (cached advisories — wipeable, will resync on next worker tick).

## First-run walkthrough

### 1. Log in

In single-user mode (the default) there is nothing to do — every request authenticates as Admin. If you've turned off `Shield__SingleUser` you'll need a user seeded in the database before you can sign in (Phase 1 has no registration UI; Phase 2 will).

### 2. Add a Discord webhook channel

1. Discord -> right-click your channel -> **Edit Channel** -> **Integrations** -> **Webhooks** -> **New Webhook** -> copy the URL.
2. In Shield: **Channels** -> **New** -> Type `Discord`, paste the webhook URL into the config JSON:
   ```json
   { "webhookUrl": "https://discord.com/api/webhooks/.../..." }
   ```
3. Set `MinSeverity` (e.g. `High` if you only want loud findings to ping that room).
4. **Save**, then **Test send** — a sample alert should land in the Discord channel within a few seconds.

### 3. Add a GitHub repo source

1. **Sources** -> **New** -> Type `GithubRepo`.
2. Fill the config JSON. For a public repo:
   ```json
   { "owner": "nomercylabs", "repo": "shield", "branch": "master" }
   ```
   For a private repo, add a token (see [PAT scopes](#github-pat-scopes) below):
   ```json
   { "owner": "myorg", "repo": "private-svc", "branch": "main", "token": "ghp_..." }
   ```
3. Set the scan interval (e.g. `01:00:00` for hourly).
4. **Save**, then click **Scan now** to kick the first run instead of waiting.

### 4. See findings

Within a minute or two:
- The source row's `LastScannedAt` updates and `ItemCount` shows how many packages it parsed.
- The matcher worker joins the new inventory against advisories already cached.
- Findings appear under **Findings**. New findings fan out to your enabled channels via the alert dispatcher (60-second drain, batched if 5+ findings hit at once).

If nothing shows up:
- Hit **Feeds** and check the OSV / GHSA / npm registry rows — `LastSuccessAt` should be recent. `LastError` will tell you why if a feed is failing.
- Inventory is only useful once feeds have synced. First-time setup may take 15-20 minutes for the feed workers to populate `feeds.db` with enough data to match against.

## Common gotchas

### GitHub PAT scopes

For the **GHSA feed** (`Shield__Feeds__Ghsa__ApiKey`):
- `public_repo` is enough — the Advisory Database is public data.

For **GitHub repo sources** (the per-source `token` field):
- Private repos: `repo` (full read access)
- Public repos: token is optional but recommended — un-authenticated GitHub API calls are limited to 60/hour per IP. With a `public_repo`-scoped token you get 5,000/hour.

### Local folder paths are inside the container

When you create a `LocalFolder` source with `{ "path": "/data/myrepo", ... }`, that path is read inside Shield's container. You need to bind-mount your code in:

```bash
docker run -d --name shield \
  -p 8080:8080 \
  -v shield-data:/data \
  -v /home/me/code/myrepo:/data/myrepo:ro \
  -e Shield__Auth__JwtSigningKey="$(openssl rand -base64 48)" \
  ghcr.io/nomercylabs/shield:latest
```

A read-only mount (`:ro`) is fine — Shield never writes to source folders.

### Reverse proxy

Shield serves HTTP on port 8080. If you put it behind Caddy / Traefik / nginx, terminate TLS there and forward to `:8080`. The container does not handle TLS itself.

### Wiping the advisory cache

Lost trust in the cached advisory data? Stop the container, delete `feeds.db` from the volume, restart. The feed workers will resync on their next tick. `shield.db` (your config + findings history) is untouched.

## Next

- [Sources](sources.md) — every config field for every source type
- [Feeds](feeds.md) — what each feed covers and when to expect data
- [Auth](auth.md) — moving off single-user mode, OIDC plans
- [Architecture](architecture.md) — pipeline, two-DB design, extension points
