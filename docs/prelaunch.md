# Pre-launch playbook

Internal doc. Captures the locked-in identity + funding + launch posture, then sequences the work that has to land before the first public push.

## Locked-in decisions

### 1. Product identity

**Security tool with supply-chain warnings and live monitoring.** Both the dep-vuln watcher angle and the security-event correlator angle ship as core surfaces, not one over the other. Competitors land in different rows depending on the user's entry point:

- Self-hosters comparing against Trivy / Snyk / Dependabot find the dep-vuln watcher.
- Self-hosters comparing against Wazuh / OSSEC / Falco find the live monitoring + event ingestion.

The narrative is: *Shield watches your supply chain (manifests + lockfiles across npm, NuGet, Composer, Cargo, Go, Maven/Gradle, PyPI, RubyGems, Swift, Dart, Elixir, Vcpkg) and your live infrastructure (fail2ban, login attempts, session replay, crawler hits) from the same dashboard — one container, no agent fleet, no upsell.*

### 2. Funding model

**Donations and contributions.** No hosted tier, no paid features, no enterprise license. The project funds itself via:

- GitHub Sponsors on the repo.
- Open-collective / Liberapay / direct (whichever the contributor prefers).
- Code contributions count as funding — patches lower the maintenance load.

There is no `pricing` page, no contact-sales path, no per-seat cost. If somebody wants something done, they sponsor, they PR, or they wait.

### 3. Ship posture

**Continuous delivery, no lock-in, no big-bang launch.** Every push is a release; features land working one at a time and grow on their own cadence. There is no "v1.0 marketing moment" — the moment a feature works end-to-end it's in production behind the version bump that triggered the run.

Implications for everything downstream of this doc:
- No frozen feature list before launch — the README describes what shipped, not what's promised.
- No invite-only beta, no waitlist, no closed alpha. If somebody finds the repo, they can install it.
- No data lock-in. Every export path stays open (JSON dumps, SBOM, downloadable audit trail).
- No telemetry. No phone-home. The container speaks only to the feeds the operator configured + the channels they wired.
- Public release notes live in `CHANGELOG.md` and the version is bumped on every push that ships behaviour.

## Pre-launch sequence

Order matters. Each step gates the next.

### Stage 1: Repo + build is shippable
- [ ] `dotnet build Shield.sln -c Release` clean (no DLL-lock errors after stopping the running server)
- [ ] `cd src/Shield.Web && npm run build` clean
- [ ] `cd docker && docker build -f Dockerfile ..` produces a working image
- [ ] `docker compose up -d` starts cleanly with a fresh `.env`
- [ ] Quickstart from README literally works on a clean machine
- [ ] All tests pass OR every failure has a "why it's not a blocker" comment

#### Image smoke test (copy-paste, all four secrets ≥48 chars)

```bash
docker run -d --name shield-smoke \
  -p 18842:8842 \
  -e Shield__Auth__DataProtectionMasterKey="$(openssl rand -base64 48)" \
  -e Shield__Auth__JwtSigningKey="$(openssl rand -base64 48)" \
  -e Shield__Auth__ApiTokenPepper="$(openssl rand -base64 48)" \
  shield:0.1.0-pre

# Wait ~15s, then:
curl -sf http://localhost:18842/healthz
curl -s  http://localhost:18842/api/auth/me   # should 401 (no SingleUser in Production)
docker rm -f shield-smoke
```

Required Production env-var floor (anything missing trips ProductionSafetyGate):
- `Shield__Auth__DataProtectionMasterKey` — ≥48 chars, NOT the dev default
- `Shield__Auth__JwtSigningKey` — ≥48 chars
- `Shield__Auth__ApiTokenPepper` — non-empty (any `shld_` token validation hashes under this)
- `Shield__SingleUser=true` requires `Shield__Auth__AllowSingleUserInProduction=true` as an explicit escape hatch
- `Shield__Public=true` requires `Shield__Auth__RequireHttps=true` + `Shield__Auth__CookieDomain=<host>`

### Stage 2: Repo cosmetics
- [ ] README accurate against shipped feature set
- [ ] LICENSE present + correct
- [ ] SECURITY.md present
- [ ] CONTRIBUTING.md current
- [ ] CHANGELOG.md has a v0.1.0 entry
- [ ] `.github/ISSUE_TEMPLATE` + `PULL_REQUEST_TEMPLATE` present
- [ ] Repo description + topics set on github.com
- [ ] Default branch is `master`
- [ ] Public visibility flipped

### Stage 3: Build + deploy proof
- [ ] CI workflow runs on push + PR
- [ ] Tagged release produces a versioned Docker image on `ghcr.io/nomercylabs/shield`
- [ ] `shield:0.1.0-pre` tag pushed
- [ ] `docker compose up -d` against the published image succeeds end-to-end
- [ ] Stoney's own `shield-dev.nomercy.tv` instance runs the published image, not the local dev build

### Stage 4: Soft launch
- [ ] Pick 5 Mastodon accounts. Draft 5 DMs (personalised — not copy-paste).
- [ ] Send DMs over 48h, not all at once.
- [ ] Track responses in an issue or a private gist.
- [ ] Fix the top 3 issues they surface, or document them as known limitations.
- [ ] Don't add features during this window. Only fixes.

### Stage 5: Public launch
- [ ] r/selfhosted post — Sunday or Monday morning EST, screenshot heavy
- [ ] Wait 48h for Reddit feedback
- [ ] Refine the title + body based on top comment objections
- [ ] HN Show HN post — Mon/Tue morning EST
- [ ] Mastodon broadcast (longer-form thread with screenshots)
- [ ] Lobste.rs (only if HN response is positive — lobste.rs hates "promotional" posts)

### Stage 6: Maintenance posture
- [ ] Weekly: triage GitHub issues, fix bugs from the prior week, ship a patch tag
- [ ] Monthly: cut a minor version, write release notes
- [ ] Quarterly: re-evaluate the funding model decision against actual user count + maintenance hours

## What NOT to do before launch

- DO NOT pivot to the security command center identity until the dep-vuln launch has 100+ stars
- DO NOT add Wave-F (Linux/nginx/journald ingestion) until 5+ real testers have shipped Shield in production
- DO NOT add more advisory feeds until the existing 5 are healthy in steady state
- DO NOT add more parsers until users ask
- DO NOT promise SLA / support / enterprise features in launch copy
- DO NOT call Shield "production-ready" — call it "early" + actually mean it
