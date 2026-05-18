# Feeds

A **feed** is an upstream source of advisory data that Shield mirrors into `feeds.db`. The matcher worker joins your inventory against the cached advisory rows; alerts fire when a join produces a new finding.

Phase 1 ships five feeds. OSV, GHSA, and npm Registry are **advisory feeds** — they import vulnerability records into `feeds.db`. KEV and EPSS are **enrichment feeds** — they run on their own schedule and annotate existing advisory rows with exploitation/scoring metadata; they do not produce new findings on their own. Phase 2 adds host vuln coverage and supply-chain risk scoring.

## Phase 1

### OSV.dev

| | |
|---|---|
| Auth | None |
| Default cadence | 15 minutes |
| Covers | Unified vulnerability database across all major package ecosystems (npm, NuGet, Composer, Gradle, PyPI, RubyGems, Go modules, and more) |
| Notes | Canonical aggregator. Free, no key, no rate limits worth worrying about. Phase 1 uses targeted-query mode driven by your inventory; Phase 2 adds full-mirror sync from the daily zip dump for offline robustness. |

OSV is the single best feed to start with — it covers the most ground for the least configuration. If you only enable one feed, enable this.

### GitHub Advisory Database (GHSA)

| | |
|---|---|
| Auth | GitHub PAT (set `Shield__Feeds__Ghsa__ApiKey`) |
| Default cadence | 15 minutes |
| Covers | Higher-fidelity advisories for npm, NuGet, Composer, Gradle, RubyGems, PyPI, Go, Rust |
| Notes | The PAT only needs `public_repo` scope — the advisory database is public. Synced incrementally via `publishedAt` cursor over Octokit's GraphQL client. |

GHSA often has more detailed `affected.ranges` data than OSV for npm/NuGet/Composer/Gradle ecosystems. Run both — duplicates are merged on `(Feed, ExternalId)` and dedup at the finding level catches the rest.

### npm registry

| | |
|---|---|
| Auth | None |
| Default cadence | 15 minutes |
| Covers | Per-package metadata: publish times, maintainer rotation, deprecation flags, tarball checksums |
| Notes | Used for **maintainer drift detection**, not CVEs. The matcher's `MaintainerDriftDetector` watches for suspicious patterns (a brand-new maintainer publishing immediately after being added, packages going from active to deprecated, tarball hash drift) and emits synthetic advisories tagged `Feed=NpmRegistry`. |

Maintainer drift is the early-warning signal that catches supply-chain attacks (Shai-Hulud, s1ngularity, slop-squat) days before a CVE gets cut. CVE feeds are reactive; maintainer drift is proactive.

### CISA KEV (Known Exploited Vulnerabilities)

| | |
|---|---|
| Auth | None |
| Default cadence | 15 minutes (scheduler-driven, same `FeedSyncWorker` as OSV/GHSA) |
| Source | `https://www.cisa.gov/sites/default/files/feeds/known_exploited_vulnerabilities.json` |
| Notes | Enrichment feed. Fetches the CISA KEV catalog and stamps matching advisory rows with `IsKev=true`, `KevAddedAt`, and `KevDueDate`. Does not create new advisories or new findings. Configured via `Shield:Feeds:Kev:Cadence`. |

### EPSS (Exploit Prediction Scoring System)

| | |
|---|---|
| Auth | None |
| Default cadence | 15 minutes |
| Source | FIRST.org daily CSV |
| Notes | Enrichment feed. Downloads the EPSS CSV and stamps matching advisory rows with `EpssScore` and `EpssPercentile`. Does not create new advisories. Configured via `Shield:Feeds:Epss:Cadence`. |

## Phase 2 (planned, not shipping yet)

### Trivy DB

OS package vulnerability database — Ubuntu USN, Debian DSA, Alpine secdb, RHEL OVAL, Amazon Linux. Single 24-hour download, used by host scanners (also Phase 2). Lights up when the Linux host source type lands.

### deps.dev

Google's transitive dependency metadata service. Provides resolution graphs, OSSF Scorecard scores, and license info per package. Used to enrich findings with "is this package well-maintained?" signal.

### Socket.dev

Free-tier supply-chain risk feed. Catches things pure CVE feeds miss: install-script telemetry, network calls in postinstall, obfuscated code, typosquat patterns. Per-package lookup, on-demand.

## Sync state and force refresh

Each feed has a row in `FeedSyncState`:

- `LastSuccessAt` — last clean sync
- `LastError` — most recent failure reason (if any)
- `NextRunAt` — when the worker will try next
- `Cursor` — incremental position (e.g. last `modified` timestamp seen)

The **Feeds** page in the UI shows all of this. Click **Force refresh** (or `POST /api/feeds/{feed}/refresh`) to enqueue an immediate sync without waiting for the scheduled tick.

If a feed is failing repeatedly, exponential backoff kicks in — successive failures push `NextRunAt` further out (up to 60 minutes). The alert queue is preserved; once the feed recovers, queued findings still go out.
