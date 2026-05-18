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

## GitHub webhook integration

Shield can post a check comment on every pull request that introduces a new vulnerable dependency. The flow:

1. Configure the secret. POST `/api/webhooks/secrets` (admin only) with the JSON body:
   ```json
   { "githubSecret": "<long random string>", "dependabotSecret": "<another long random string>" }
   ```
   Both fields are stored encrypted in the `AppSettings` table (`webhooks.github.secret`, `webhooks.dependabot.secret`) using the same `shield.settings` data protector as the rest of the runtime-mutable settings.
2. In your GitHub repo (or org) **Settings -> Webhooks -> Add webhook**:
   - **Payload URL**: `https://<your-shield>/api/webhooks/github`
   - **Content type**: `application/json`
   - **Secret**: the value you stored in step 1
   - **Events**: select **Pull requests** (we listen for `opened`, `synchronize`, `reopened`)
3. Make sure the matching `GithubRepo` source already exists in Shield (its `Name` is `<owner>/<repo>`).
4. Open a PR. Shield runs a one-shot scan of the PR head ref, diffs against the latest tracked snapshot, matches added packages against the advisory store, and posts (or updates) a single comment marked with a `<!-- shield:pr-<number> -->` sentinel.

Authentication for the comment uses the GitHub OAuth token stored via **Settings -> Integrations**; if no token is connected, Shield logs the result but skips the comment.

### Dependabot consumer

If your org publishes the Dependabot Alert webhook, point it at `https://<your-shield>/api/webhooks/dependabot` with the second secret. Each alert is persisted as an `Advisory` row (`Feed = Ghsa`, `ReferencesJson` tagged `DEPENDABOT`) and — when the repo matches a known `Source` — a re-match is queued so any newly-acknowledged fixes propagate to the findings list immediately. Cross-validation against OSV / GHSA happens via the existing matcher pipeline; duplicate GHSA IDs from multiple feeds upsert by `(Feed, ExternalId)`.

Signature verification on both endpoints uses `X-Hub-Signature-256` (HMAC-SHA256), validated in constant time. A mismatch returns `401 Unauthorized`; a missing or malformed header also returns `401`.

### Health badge

Anonymous endpoint: `GET /api/badge/{owner}/{repo}.svg` returns a shields.io-style flat SVG with the open finding counts for the matching source (e.g. `shield | 1C 3H 2M 0L`). The response is cached for 5 minutes (`Cache-Control: max-age=300`) and is the only anonymous endpoint Shield exposes besides `/healthz`. Drop the URL straight into a README:

```markdown
![Shield](https://your-shield.example.com/api/badge/NoMercyLabs/shield.svg)
```

When no source matches, the badge shows `shield | not watched` in grey.

## Per-source access control

By default, Admin users see all sources. Non-admin users (Viewer, Maintainer) only see sources they have been granted access to.

Access is managed via **Source Groups** and **direct grants**, both configured under `GET/POST /api/access/groups` and `POST /api/access/sources/{id}/grant`.

| Entity | Purpose |
|---|---|
| `SourceGroup` | Named collection of users. An admin creates a group, adds members, then grants the group access to one or more sources. |
| `GroupMembership` | Binds a Shield user to a group. |
| `SourceAccess` | A single grant — either a direct `userId` or a `groupId` — with an access level (`Read` or `Triage`). |

**Effective level** for a user on a given source = max of all `SourceAccess` rows that reach that user (direct grants + every group they belong to).

Admins bypass all ACL checks and see every source regardless of grants.

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
