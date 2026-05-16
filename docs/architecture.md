# Architecture

A friendly tour of the Shield codebase. For exhaustive detail see the design spec; this page is the public-facing summary.

## Stack

- .NET 9 minimal API
- EF Core + SQLite (two databases — see below)
- Vue 3 + Tailwind v4 SPA, built into `wwwroot` and served by the API host
- ASP.NET Core Identity (cookie + JWT)
- Background work via `BackgroundService`-derived hosted services

Single Docker image carries the API, the embedded SPA, and all background workers in one process.

## Pipeline

```
            [Sources: repo / folder / host (Phase 2)]
                       |
                       v
              SourceScanWorker
        (per-source schedule + on-demand)
                       |
                       v
          InventorySnapshot (versioned, contents-hashed)
            package@version + parent chain
                       |
                       |   <----- Feed cache (feeds.db)
                       |           ^
                       |           |
                       |   FeedSyncWorker
                       |   OSV / GHSA / npm registry
                       |   each on its own cadence
                       v
                   Matcher
        joins on (ecosystem, name@version) + range check
                       |
                       v
                   Finding
       dedup key = sha256(sourceId | eco | pkg | adv.externalId)
       same key -> update LastSeenAt, no duplicate alert
                       |
                       v
                  Alerter
       severity threshold per channel
       acknowledged / suppressed filtered
                       |
                       v
        Discord  |  In-app inbox
        ntfy / SMTP arrive in Phase 2
```

## Two-DB design

Shield stores configuration and state in `shield.db`, and cached advisory data in `feeds.db`. Two `DbContext` types, two SQLite files, one process.

| `shield.db` | `feeds.db` |
|---|---|
| Sources | Advisory |
| InventorySnapshot, InventoryItem | PackageMeta |
| Finding (dedup-keyed) | FeedSyncState |
| AlertChannel, AlertEvent | |
| InboxMessage | |
| ASP.NET Identity tables | |

The split exists because `feeds.db` is **wipeable**. If the cache gets corrupt or you want to force a full resync, stop the container, delete `feeds.db`, restart. Your config, source history, finding history, and acknowledgements all live in `shield.db` and are untouched.

Each context migrates independently on startup. The schemas evolve at their own pace.

## Background workers

| Worker | Cadence | Responsibility |
|---|---|---|
| `FeedSyncWorker` | per-feed (default 15 min) | Pull from OSV / GHSA / npm registry, write into `feeds.db` |
| `SourceScanWorker` | per-source `ScanInterval` | Scan repos / folders, write `InventorySnapshot` + items |
| `MatcherWorker` | reactive, drains `MatchQueue` | Joins inventory x advisories, writes new findings |
| `AlertDispatchWorker` | 60 second drain | Drain queued findings to channels, batches if 5+ in one window |

Workers communicate via in-process `Channel<T>` queues (`ScanQueue`, `MatchQueue`, `FeedRefreshQueue`) — no external message broker.

## Extensibility points

If you want to extend Shield, the seams are these four interfaces in `Shield.Core.Abstractions`:

- **`IParser`** — read a lockfile stream, emit `InventoryItem`s. Add a parser for a new ecosystem (Cargo, Go modules, PyPI, Hex...) by implementing this and registering it via DI.
- **`IFeedSync`** — pull advisories from an upstream source on a cadence, write `Advisory` rows. Add a new feed by implementing this and registering it.
- **`IScanner`** — given a `Source`, produce a `ScanResult` with a new `InventorySnapshot`. Two implementations ship in Phase 1 (`GitHubRepoScanner`, `LocalFolderScanner`); the Linux host scanner is Phase 2.
- **`IAlertChannel`** — given a list of `Finding`s, deliver them. Two implementations ship: Discord webhook and in-app inbox. ntfy and SMTP arrive in Phase 2.

All four are registered via the per-project `ServiceCollectionExtensions.cs` so adding a new implementation is "drop a class, call `services.AddX()` in `Program.cs`".

## Repository layout

```
shield/
  src/
    Shield.Api/            ASP.NET host, controllers, workers, wwwroot
    Shield.Web/            Vue 3 SPA (built into Shield.Api/wwwroot)
    Shield.Core/           Domain entities, interfaces, options, results
    Shield.Data/           EF Core DbContexts, configurations, migrations
    Shield.Scanners/       GitHubRepoScanner, LocalFolderScanner
    Shield.Parsers.Npm/    npm/yarn/pnpm lockfile parser
    Shield.Parsers.Nuget/  packages.lock.json parser
    Shield.Parsers.Composer/
    Shield.Parsers.Gradle/
    Shield.Parsers.Os/     stub for Phase 2
    Shield.Feeds.Osv/      OSV.dev sync
    Shield.Feeds.Ghsa/     GitHub Advisory Database sync
    Shield.Feeds.NpmRegistry/  per-package metadata for maintainer drift
    Shield.Feeds.DepsDev/  stub for Phase 2
    Shield.Feeds.Socket/   stub for Phase 2
    Shield.Feeds.TrivyDb/  stub for Phase 2
    Shield.Matcher/        version-range engine + maintainer drift detector
    Shield.Alerter/        AlertDispatcher (severity threshold, batching)
    Shield.Channels/       Discord + Inbox channel implementations
  agent/
    shield-agent/          stub for Phase 2 (Linux host scanner)
  tests/
    Shield.*.Tests/        xUnit per project
  docker/
    Dockerfile             multi-stage SDK build -> aspnet:9.0-alpine
    docker-compose.yml     Shield + reverse proxy + named volume
```

## Why these choices

- **SQLite, not Postgres.** Shield is sized for one operator watching their own stuff. SQLite handles thousands of advisories and millions of inventory rows without breaking a sweat. No external DB to back up means one fewer thing to forget.
- **Single image.** Everything in one container = one volume, one health check, one log stream. The cost is that you can't scale workers horizontally, but Shield's load profile doesn't need that.
- **No external broker.** `BackgroundService` + `Channel<T>` does everything an in-process queue needs. Adding RabbitMQ would be cargo culting.
- **Two DBs.** The `feeds.db` cache regenerates from upstream, and being able to wipe it without losing config is worth the tiny extra ceremony.
