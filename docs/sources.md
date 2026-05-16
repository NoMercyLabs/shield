# Sources

A **source** is anything Shield scans for dependencies. Phase 1 ships two source types: GitHub repos and local folders. Linux host scanning (agent + SSH) is Phase 2.

Every source has these fields regardless of type:

| Field | Type | Notes |
|---|---|---|
| `Name` | string | Display name in the UI |
| `Type` | enum | `GithubRepo`, `LocalFolder`, `LinuxHost` (Phase 2) |
| `ConfigJson` | string | Type-specific JSON, schemas below |
| `ScanInterval` | TimeSpan | How often `SourceScanWorker` re-scans this source. Examples: `01:00:00` = hourly, `00:15:00` = every 15 minutes |
| `Enabled` | bool | When false, scheduler skips this source entirely |

Each scan produces an `InventorySnapshot` with a deterministic `ContentsSha`. Identical inventory across scans = same hash, so you can tell at a glance whether a scan changed anything.

## GitHub repo

Reads lockfiles via the Git Trees + Blob API. No clone happens — Shield fetches only the files it can parse, by name.

### Config schema

```json
{
  "owner": "nomercylabs",
  "repo": "shield",
  "branch": "master",
  "token": "ghp_xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"
}
```

| Field | Required | Notes |
|---|---|---|
| `owner` | yes | GitHub user or organisation |
| `repo` | yes | Repository name (no owner prefix) |
| `branch` | no | Defaults to `main` if omitted |
| `token` | no for public, yes for private | GitHub PAT. See [PAT scopes](getting-started.md#github-pat-scopes). Optional for public repos but recommended (rate limits) |

### What it scans

Whatever filename matches a registered parser. As of Phase 1 that means:

- `package-lock.json`, `yarn.lock`, `pnpm-lock.yaml` (npm parser)
- `packages.lock.json` plus `*.csproj` fallback (NuGet parser)
- `composer.lock` (Composer parser)
- `gradle.lockfile` plus `build.gradle` / `build.gradle.kts` fallback (Gradle parser)

Shield walks the entire repo tree at the configured branch — monorepos with multiple lockfiles are handled correctly; each is parsed and aggregated into one snapshot per scan.

## Local folder

Walks a directory tree on the host filesystem (mounted into Shield's container) and feeds recognised lockfiles to the parsers.

### Config schema

```json
{
  "path": "/data/myrepo",
  "ignoreGlobs": ["node_modules", "vendor", "bin", "obj"]
}
```

| Field | Required | Notes |
|---|---|---|
| `path` | yes | Absolute path **inside the container**. You must bind-mount your code in (see Getting Started) |
| `ignoreGlobs` | no | Folder names to skip. Defaults to `["node_modules", "vendor", "bin", "obj", ".git"]` if omitted |

The path is read inside Shield's container. If your code lives at `/home/me/code/myrepo` on the host, mount it:

```bash
docker run -d --name shield \
  -v /home/me/code/myrepo:/data/myrepo:ro \
  ...
```

Then set `"path": "/data/myrepo"` in the source config. Read-only is fine — Shield never writes to source folders.

`ignoreGlobs` is currently exact folder-name matching, not glob expansion. `node_modules` matches the folder name; `**/node_modules/**` is unnecessary (and won't do what you might think yet).

### Auto-detected git remote

When the configured `path` is a git working tree, Shield reads `<path>/.git/config` on every scan and records the `origin` remote on the source. The detected remote appears in the source detail view (`detectedRemote: { host, owner, repo, remoteUrl, branch }`). For sources whose origin lives on `github.com`, admins see a **Promote to GitHub source** action that creates a sibling `GithubRepo` source pointing at the same repo — so both filesystem scanning and GitHub-API scanning run side by side. The original LocalFolder source stays put.

Hosts eligible for detection (and the Promote action) come from `Shield:Scanners:DetectedRemoteHosts` in configuration — a comma-separated string, defaulting to `github.com,gitlab.com,bitbucket.org`. Other hosts (Gitea, Forgejo, self-hosted GitLab) are parsed but only recorded if you add them here. Promote-to-GitHub still requires `host == github.com` regardless of the whitelist.

## Per-source scan interval

`ScanInterval` is a .NET `TimeSpan`. Common values:

| Cadence | TimeSpan |
|---|---|
| Every 5 minutes | `00:05:00` |
| Every 15 minutes | `00:15:00` |
| Hourly | `01:00:00` |
| Every 6 hours | `06:00:00` |
| Daily | `1.00:00:00` |

`SourceScanWorker` ticks every minute and dispatches sources whose `LastScannedAt + ScanInterval` is in the past. Use `POST /api/sources/{id}/scan-now` (or the **Scan now** button) to bypass the schedule for a one-off run.

## Errors and stale state

If a scan fails, the source's `LastError` field captures the message and the UI flags the source as unhealthy. The most recent successful `InventorySnapshot` is kept — Shield never throws away a good snapshot because a later scan failed.

GitHub rate-limit responses are honoured: Shield reads `X-RateLimit-Reset` and defers remaining work to the next worker tick rather than burning through the budget.
