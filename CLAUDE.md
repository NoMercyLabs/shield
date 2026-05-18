# Shield — Agent / Contributor Notes

Shield is a self-hosted cross-ecosystem dependency vulnerability warning system.
Phase 1 scope is in `.github/`, `docs/`, `CHANGELOG.md`, `README.md`.

## Hard rules for AI agents working in this tree

**NEVER run git.** No `git stash`, no `git reset`, no `git checkout`, no
`git commit`, no `git push`, no `git add`. Not even "to diagnose a build
error". A previous agent's `git reset --hard` after a sibling agent's
commit nuked five uncommitted edits — including the matcher dedup fix
that the headline product feature depends on. The CTO commits between
waves. You ship files; the CTO ships history.

If a build fails because the working tree is dirty in a confusing way,
return your report describing the conflict instead of trying to "clean
up" with git. The CTO has more context than you do.

**NEVER kill the dev server on port 8842 without restarting it after.**
The CTO is using the running server to demo + verify. If you must rebuild
and the build fails because a DLL is locked, find the PID via
`netstat -ano | grep ':8842.*LISTENING'`, stop it with
PowerShell `Stop-Process -Id <pid> -Force`, rebuild, then **restart
immediately** with:

```bash
cd c:\Projects\NoMercyLabs\shield\src\Shield.Api
Shield__Auth__JwtSigningKey="local_dev_key_for_arc_demo_at_least_32_chars_long" \
Shield__Auth__DataProtectionMasterKey="dev-master-key-at-least-32-chars-long-xx" \
Shield__SingleUser=true \
Shield__Db__Shield="Data Source=C:/Projects/NoMercyLabs/shield/data/shield-demo.db" \
Shield__Db__Feeds="Data Source=C:/Projects/NoMercyLabs/shield/data/feeds-demo.db" \
Shield__OpenApi__Enabled=true \
ASPNETCORE_ENVIRONMENT=Development \
dotnet run --no-build -c Release -- --urls "http://localhost:8842" \
  > /tmp/shield-demo.log 2>&1 &
disown
```

**Why localhost and not 0.0.0.0:** binding to `0.0.0.0` makes Windows Defender Firewall prompt on every rebuild because the binary signature changes and the new exe wants to listen on a non-loopback interface. `localhost` binding bypasses the firewall entirely. The public-exposure story is for the published Docker image, not local dev.

**Why ASPNETCORE_ENVIRONMENT=Development:** the ProductionSafetyGate (see `Hardening/ProductionSafetyGate.cs`) refuses to boot with `SingleUser=true`, `OpenApi=true`, or the dev master key in Production. Use Production only when you've replaced all three with real secrets.

**Why master key + dev default:** the keyring under `bin/Release/net9.0/data/keys/` is AES-wrapped. If you start without the env var after a previous boot used one, the runtime can't decrypt and dies on Identity startup. Pass the dev key, or wipe the `keys/` folder.

If your build fails because the dev DB locked or migrations conflict,
report it — do not wipe `data/*.db` without flagging.

## Code conventions

- C# formatted by CSharpier (`dotnet csharpier format <project>`). A
  PostToolUse hook in `.claude/settings.json` runs this on every Edit/Write.
- Vue / TypeScript: no prettier config yet — if you add one, format the
  whole `src/Shield.Web/` tree in the same commit.
- No `Co-Authored-By` trailers on commits.
- DI: services that touch `DbContext` (Scoped) must themselves be Scoped.
  Don't promote them to Singleton — the captive-dep bug bit twice already.
- New migrations: `dotnet ef migrations add <Name> --context ShieldDbContext --output-dir Migrations/Shield`
  (or `--context FeedsDbContext --output-dir Migrations/Feeds`).
  Update the migration's `Up` to ALTER existing tables when extending —
  don't `CreateTable` something a previous migration already created.
- **i18n keys are snake_case + meaning-only, not English words.** Match the convention
  in `foghorn/apps/editor/src/i18n/en.json`: top-level keys are domains (`app`, `nav`,
  `workspace`, `entry`, `publish`), leaves are snake_case action/role names
  (`open_btn`, `commit_placeholder`, `pause_ms`, `pick_folder_btn`), and contextual
  grouping nests one level (`workspace.picker.title`). **Wrong:**
  `auth.signInTitle`, `common.save`, `nav.dashboard` (camelCase + leaf is the English).
  **Right:** `screen.signin.title`, `action.save`, `nav.dashboard` keeps domain but
  uses snake_case + a leaf that won't change if the copy changes.

## What lives where

- `src/Shield.Api/` — host, controllers, workers, OAuth, settings, channels glue
- `src/Shield.Core/` — domain entities + interfaces
- `src/Shield.Data/` — EF Core + two `DbContext`s (Shield + Feeds)
- `src/Shield.Scanners/` — `GitHubRepoScanner`, `LocalFolderScanner`, parser registry
- `src/Shield.Parsers.<Eco>/` — one project per ecosystem. Yes, this is
  over-engineered for a single-deploy product. Consolidating to
  `Shield.Plugins/Parsers/<Eco>/` is on the v0.2 list.
- `src/Shield.Feeds.<Source>/` — same shape, one per advisory feed (OSV, GHSA,
  npm registry, …)
- `src/Shield.Channels/` — Discord, Inbox, Ntfy, Slack, SMTP, Webhook
- `src/Shield.Matcher/` — version range parsing + finding dedup
- `src/Shield.Web/` — Vue 3 + Tailwind v4 SPA (builds into `../Shield.Api/wwwroot/`)
- `tests/` — xUnit per source project + WireMock for HTTP feeds
- `agent/shield-agent/` — Phase 2 host-side scanner (stub)
- `docker/` — multi-stage Dockerfile + Compose example
- `docs/` — public-facing docs (getting-started, sources, feeds, auth, architecture)

## Building

```bash
dotnet build Shield.sln -c Release
dotnet test Shield.sln -c Release --no-build
cd src/Shield.Web && npm run build
```

The SPA build emits straight into `../Shield.Api/wwwroot/` — keep this in
mind if you're tempted to `clean` either side; you'll wipe the other.
