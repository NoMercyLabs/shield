# Launch post drafts

Pre-written copy for the four launch surfaces in `prelaunch.md` Stage 4-5. Edit freely before posting — these are starting points, not commandments.

---

## 1. Mastodon DM template (pre-launch, 5 trusted strangers on infosec.exchange / hachyderm.io)

Pick 5 accounts that have actually posted about self-hosted security tooling in the last 30 days. Personalise the first sentence per recipient — the rest stays.

```
Hey [name] — saw your post about [thing they actually said]. I built a thing I think you'd have opinions on.

Shield: self-hosted dep-vuln watcher + live security-event monitor. One Docker container.
- Scans github + forgejo + gitlab + local folders across npm/nuget/composer/cargo/go/maven/gradle/pypi/rubygems/swift/dart/elixir/vcpkg
- Pulls OSV + GHSA + KEV + EPSS + npm advisories, deduplicates findings
- Ingests fail2ban events on the same dashboard (observer pattern — fail2ban stays the enforcer)
- Free, MIT, donations-funded, no hosted tier, no telemetry, no lock-in
- shield-dev.nomercy.tv is a live instance you can poke

Looking for honest reactions before I post anywhere else. Brutal feedback welcome.

Repo: github.com/nomercylabs/shield
Install: docker pull ghcr.io/nomercylabs/shield:latest && see README
```

Track responses in a private gist. Fix the top 3 issues across the 5 replies before moving to Reddit.

---

## 2. r/selfhosted post (after Mastodon round, Sunday or Monday EST)

Title (pick one — first option is the safest):

- **`Shield — supply-chain vuln watcher + live security monitor, self-hosted, one container, MIT`**
- `I built a self-hosted dep-vuln watcher because Snyk wants $$$ and Dependabot needs GitHub`
- `Shield: scan your repos for vulns + tail fail2ban from the same dashboard, no SaaS`

Body:

```markdown
Hey r/selfhosted —

I got tired of three things: Snyk wanting per-developer pricing, Dependabot only working inside GitHub, and Wazuh being a three-tier deploy for what should be a single container. So I built Shield.

**What it does:**

- Scans your repos (GitHub / Forgejo / GitLab / local folders) across 13 ecosystems (npm, NuGet, Composer, Maven, Gradle, PyPI, RubyGems, Go, Rust, Swift, Dart, Elixir, Vcpkg)
- Cross-references against OSV + GHSA + CISA KEV + EPSS + npm advisories
- Suggests fixes; for npm it can open a PR with a `dependencies` bump or an `overrides` block for transitive deps
- Ingests fail2ban events from your other hosts (observer pattern — fail2ban stays the enforcer; Shield correlates + dashboards)
- Web push notifications, Discord/Slack/Webhook/Ntfy/SMTP channels, optional email invites for collaborators
- Multi-user with per-source ACLs, or single-admin mode if you're a solo operator

**What it's not:**

- A SIEM. fail2ban is the enforcer; Shield just watches.
- A SaaS. There is no hosted tier and no plan to launch one.
- Free-trial-then-pay. MIT, donations-funded. Code contributions = funding.

**Install:**

```yaml
services:
  shield:
    image: ghcr.io/nomercylabs/shield:latest
    ports: ["8842:8842"]
    environment:
      Shield__Auth__DataProtectionMasterKey: "<openssl rand -base64 48>"
      Shield__Auth__JwtSigningKey: "<openssl rand -base64 48>"
      Shield__Auth__ApiTokenPepper: "<openssl rand -base64 48>"
    volumes: ["./data:/app/data"]
```

That's it. Quickstart in the repo.

**Live instance to poke:** shield-dev.nomercy.tv (read-only single-user demo)

**Repo:** github.com/nomercylabs/shield

Brutal feedback welcome — first public release, lots of rough edges. Tell me what breaks.
```

Time of day: Sunday 9-11am EST or Monday 6-9am EST. Reddit's front page rotates fast; weekday mornings catch the SRE/ops crowd before standups.

Screenshots to attach: dashboard, findings filter row, source-detail with a real vulnerability, security-events tab.

Wait **48 hours** before posting on HN — read the top 3 objections from Reddit, fold them into the HN title and body.

---

## 3. Show HN post (after Reddit, Monday/Tuesday morning EST)

Title (Show HN: prefix is required — 80-char limit):

`Show HN: Shield – self-hosted vuln watcher for npm/nuget/composer/etc, one Docker container`

Body (HN strips most markdown — keep it plain):

```
Hey HN —

Shield is a self-hosted dependency vulnerability watcher I built because Snyk wants per-seat pricing and Dependabot is GitHub-only. One Docker container, MIT, donations-funded, no hosted tier.

It scans repos across GitHub, Forgejo, GitLab, and local folders for 13 ecosystems (npm, NuGet, Composer, Maven, Gradle, PyPI, RubyGems, Go, Rust, Swift, Dart, Elixir, Vcpkg). Findings come from OSV + GHSA + CISA KEV + EPSS + npm advisories, deduplicated. For npm it can open a PR with the fix — either a direct bump or an `overrides` block for transitive deps.

The same dashboard also ingests fail2ban events. Shield isn't an enforcer (fail2ban stays the enforcer) — it correlates and dashboards. Web Push for high-severity findings, channels for Discord/Slack/Webhook/Ntfy/SMTP.

Design choices I expect questions on:

- SQLite + EF Core, two databases (findings vs feeds mirror). Operator owns the data.
- DataProtection envelope encryption — operator brings a master key, every settings row encrypted at rest.
- No telemetry. No phone-home. Container speaks only to the feeds you configured.
- Every push is a release. There is no "v1.0 moment".

Live instance: shield-dev.nomercy.tv

Repo: github.com/nomercylabs/shield

This is my first public release. Honest feedback wanted, especially on the scanner internals.
```

HN reads "Show HN" titles literally. Don't add adjectives. The shorter and more concrete the title, the better.

Post Monday or Tuesday between 8-10am EST. Avoid Wed/Thu (PM/marketing churn) and weekends (low traffic).

---

## 4. Lobste.rs post (optional, only if HN response is positive)

Title:

`Shield: self-hosted vulnerability watcher and live security monitor`

Tags: `security`, `release`, `programming`

Body: a tighter version of the HN body. Lobste.rs's audience prefers technical justification over marketing — lead with the architectural decisions, not the value prop.

```
Built Shield because the existing supply-chain vuln tools (Snyk, Sonatype, Trivy + Grafana) are
either SaaS-only, agent-fleet deploys, or pure scanners with no triage UI. Self-hosted,
single-container, MIT, donations-funded.

Stack: ASP.NET 10 + EF Core + SQLite, Vue 3 SPA. Two SQLite files — one for operator state
(findings, sources, audit), one for the advisory feed mirror. DataProtection envelope encrypts
every settings row at rest. No telemetry.

13 ecosystem parsers, 5 advisory feeds (OSV/GHSA/KEV/EPSS/npm registry), 6 channels (Inbox/
Discord/Slack/Webhook/Ntfy/SMTP). Optional Web Push for high-severity findings.

Live instance to poke: shield-dev.nomercy.tv
Repo: github.com/nomercylabs/shield

First public release; rough edges exist. Looking for technical feedback on the scanner internals
and the feed-correlation logic.
```

---

## Post-launch follow-up

Whatever feedback comes back, surface it as GitHub issues with a `launch-feedback` label. Triage weekly. The first round of contributors will pattern-match on how the maintainer handles the first 10 issues — if they sit untouched, the wave dies.

The funding model says **donations + contributions**. Set up the GitHub Sponsors page + Open Collective / Liberapay before the launch posts go out — every "this is great where do I donate" needs a link.
