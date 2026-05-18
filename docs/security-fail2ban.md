# Security: fail2ban correlation

Shield is the **observability + correlation** layer for the security signal across your
self-hosted stack. **fail2ban is the enforcer** — it runs in a Docker container on each
host you want guarded, watches the relevant log files, and uses iptables to drop bad IPs.
Shield does NOT replicate that enforcement. Shield ingests fail2ban's ban/unban events,
correlates them with its own internal signals (failed logins, rate-limit rejections,
session replay attempts, crawler hits), and shows everything on one timeline.

```
fail2ban (Docker, each host)  ── action.d/shield.conf ──►  Shield /api/security/fail2ban/event
   ▲                                                              │
   │                                                              ▼
   │                                                       SecurityEvent table
   │                                                              │
   ▼                                                              ▼
fail2ban-client set <jail> banip <ip>  ◄── Shield ban-request flow
                                            (Wave-F: outbound integration)
```

## Setup

### 1. Generate an ingest key

One key per Shield instance — NOT one per fail2ban host.

```bash
openssl rand -base64 32
```

Set it on the Shield host:

```bash
export Shield__Security__Fail2BanIngestKey="<the key from above>"
```

When using docker-compose, add it to the Shield service's `environment:` block. Rotate
annually; on rotation, redeploy fail2ban with the new key in the same maintenance window.

### 2. Wire fail2ban

Copy `docker/fail2ban/action.d/shield.conf` from this repo into the fail2ban container's
`/etc/fail2ban/action.d/`. The simplest pattern with docker-compose is a bind mount:

```yaml
services:
  fail2ban:
    image: crazymax/fail2ban:latest
    volumes:
      - ./fail2ban/action.d:/etc/fail2ban/action.d:ro
      - ./fail2ban/jail.d:/etc/fail2ban/jail.d:ro
      - /var/log:/var/log:ro
    cap_add:
      - NET_ADMIN
      - NET_RAW
    network_mode: host
```

Edit `shield.conf` and replace:

- `<SHIELD_BASE>` with your Shield instance's reachable URL (e.g.
  `https://shield.example.com`)
- `<YOUR_KEY>` with the ingest key from step 1

Reference the action from each jail by chaining it onto the existing action:

```ini
# /etc/fail2ban/jail.d/sshd.conf
[sshd]
enabled = true
action  = %(action_)s
          shield
```

Reload fail2ban:

```bash
docker exec fail2ban fail2ban-client reload
```

### 3. Verify

1. Open Shield's Security view (`/security` — Admin only) — it should already show
   the live indicator as connected.
2. Induce a ban from a throw-away IP (e.g. SSH a fake user three times). fail2ban will
   ban the IP, and within a couple of seconds the event should appear in the Timeline
   with `Source=fail2ban` `EventType=fail2ban.ban`. The Hosts tab should now list the
   fail2ban container's hostname with a recent `Last seen` timestamp.
3. The same event should arrive as a Web Push notification on any subscribed admin
   device, titled "IP banned by fail2ban".

## What Shield observes on its own

Even without any fail2ban deployed, Shield emits the following events to the same
timeline. Operators can use these as authoritative signals to feed BACK into fail2ban
filters (point your jail's `failregex` at Shield's log).

| Event type | Source | Severity | Trigger |
|---|---|---|---|
| `login.failed` | `shield.auth` | Medium | `/api/auth/login` returns 401 from a wrong password |
| `login.lockout` | `shield.auth` | High | `/api/auth/login` hits Identity's lockout threshold |
| `apitoken.failed` | `shield.apitoken` | Medium | `Authorization: Bearer shld_…` with an unknown/revoked token; only the token prefix is logged |
| `session.replay` | `shield.auth` | High | Cookie matches a revoked `UserSession` row (token leaked, device lost) |
| `rate.limit` | `shield.ratelimit` | Low | Any ASP.NET rate limit policy rejected the request |
| `crawler.detected` | `shield.crawler` | Low | A bot user-agent hit the SPA's link-unfurl path |

## IP reputation rollup

For every event with a non-null `RemoteIp`, Shield upserts an `IpReputations` row:

- `EventCount` — rolling 30-day count. Resets when the previous activity falls outside
  the window so a long-quiet IP doesn't carry stale reputation.
- `Score` — weighted: `Low=1`, `Medium=3`, `High=8`, `Critical=20`.
- `CurrentlyBanned` — mirror of fail2ban's view. Flips true on `fail2ban.ban`, false
  on `fail2ban.unban`. **NOT Shield's enforcement.**
- `Notes` — operator-attached free text (e.g. "known scanner from VPS pool").

The IP detail drawer in the SPA shows the last 100 events for that IP plus a
"Request fail2ban ban" button (Wave-F target).

## Wave-F: outbound enforcement (not yet implemented)

The `POST /api/security/ips/{ip}/request-ban` endpoint records the admin's intent
and broadcasts it via SignalR. The actual outbound call to `fail2ban-client set <jail>
banip <ip>` is deferred to Wave-F because it requires:

- SSH key management to reach the fail2ban host (no remote API exists)
- OR a small Shield-Agent component that runs alongside fail2ban and exposes a signed
  webhook

Until Wave-F lands, the UI surfaces "Awaiting fail2ban confirmation" after a ban
request; when fail2ban subsequently bans the IP (via its own filter or operator
intervention), its inbound event flips `CurrentlyBanned`, closing the loop.

## Troubleshooting

- **401 from /api/security/fail2ban/event**: the `X-Shield-Fail2Ban-Key` header doesn't
  match `Shield:Security:Fail2BanIngestKey`. Confirm both ends with `printenv` /
  `docker exec fail2ban env`. The check is constant-time so timing won't help an
  attacker guess the key.
- **429 from /api/security/fail2ban/event**: the `fail2ban-ingest` rate policy capped
  the host at 600 events/minute. Real attacks burst above that; if you sustain higher
  rates, the policy is the right place to retune.
- **fail2ban bans but no Shield event**: tail fail2ban's log inside the container —
  curl errors land there. Common cause: `<SHIELD_BASE>` is `localhost` from the
  container's perspective; use the host-network IP or the docker-compose service name.

## Security model recap

- One ingest key per Shield instance. Rotate annually.
- Shield never executes `iptables`. fail2ban remains the only path that drops packets.
- The ingest endpoint accepts events from any host that holds the key — keep it
  confidential. If a host is compromised, rotate the key immediately and re-deploy
  every fail2ban container with the new value.
