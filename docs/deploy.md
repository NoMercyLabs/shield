# Production deploy

This is the recipe for running Shield on a real server (Proxmox LXC, bare Docker host,
hypervisor VM) fronted by a TLS-terminating reverse proxy. For local development read
`CLAUDE.md`. For a one-line Docker spin-up read `docs/getting-started.md`.

## Topology

```
[ user ] --HTTPS--> [ Cloudflare edge ] --tunnel--> [ cloudflared ] --HTTP--> [ shield:8842 ]
```

Cloudflare Tunnel terminates TLS at the edge. `cloudflared` runs either:

- **On the Docker host** (recommended) — reaches Shield via `127.0.0.1:8842`. Simpler;
  one less container; survives Shield restarts because the tunnel daemon outlives the app.
- **As a Compose sidecar** — reaches Shield via the compose network at `http://shield:8842`.
  Useful when the host doesn't have systemd or you want everything in one stack.

Shield never speaks TLS itself. The container exposes plain HTTP on port 8842 and trusts
the proxy via `Shield__ForwardedHeaders__KnownProxies` to rewrite the request scheme.

## Step-by-step

### 1. Clone

```bash
git clone https://github.com/nomercylabs/shield.git
cd shield/docker
```

### 2. Configure

```bash
cp .env.example .env
# edit .env — every variable in the REQUIRED sections must have a real value
```

Required substitutions:

| Variable | How to fill |
|---|---|
| `Shield__Auth__JwtSigningKey` | `openssl rand -base64 48` — minimum 32 chars; shorter keys are rejected at startup |
| `Shield__Auth__DataProtectionMasterKey` | `openssl rand -base64 36` and **save it to a password manager** — required outside `Development`; missing key prevents startup |
| `Shield__Auth__CookieDomain` | Your tunnel hostname, e.g. `shield.example.com` |

The other knobs (`Shield__Public=true`, `Shield__Auth__RequireHttps=true`,
`Shield__SingleUser=false`, `ASPNETCORE_ENVIRONMENT=Production`) are correct as shipped.

### 3. Bring it up

Host-side cloudflared (recommended):

```bash
docker compose up -d
```

Sidecar cloudflared:

```bash
# Uncomment TUNNEL_TOKEN in .env first, then:
docker compose -f docker-compose.yml -f docker-compose.cloudflared.yml up -d
```

Also uncomment `Shield__ForwardedHeaders__KnownNetworks=172.16.0.0/12` in `.env` when
using the sidecar — cloudflared's container IP rotates inside the Docker bridge subnet,
so trusting the whole subnet is more robust than re-pinning a fixed IP.

### 4. Verify boot

```bash
docker compose logs -f shield
```

Wait for both of:

```
info: Microsoft.Hosting.Lifetime[14] Now listening on: http://[::]:8842
info: Microsoft.Hosting.Lifetime[0]  Application started. Press Ctrl+C to shut down.
```

A `Shield posture: Environment=Production Public=True RequireHttps=True ...` line should
appear above them — that's the ProductionSafetyGate confirming the chosen knobs.

If the gate refuses to start it lists every failing knob plus a remediation hint. The
container will exit 1 in that case; fix `.env` and `docker compose up -d` again.

### 5. Create the first admin

The image ships with `Shield__SingleUser=false`, so there's no auto-Admin. The first
account registered through `/api/auth/register` becomes Admin automatically (first-user-wins).

Open the SPA at your tunnel hostname and follow the register flow, OR:

```bash
curl -X POST https://shield.example.com/api/auth/register \
  -H 'Content-Type: application/json' \
  -d '{"username":"admin","password":"<long-random>","email":"you@example.com"}'
```

Verify:

```bash
curl -u admin:<password> https://shield.example.com/api/auth/me
# expect: { "id": "...", "username": "admin", "roles": ["Admin"] }
```

### 6. Close the door

Shield does not yet have a `Shield__Auth__AllowRegistration=false` switch — this is on
the v0.2 list. Until then, after creating the admin you can either:

- Put `/api/auth/register` behind Cloudflare Access (Zero Trust → Access → Applications)
  with an allow-list pointing at your own identity provider, OR
- Block the path in cloudflared's ingress config:
  ```yaml
  ingress:
    - hostname: shield.example.com
      path: ^/api/auth/register$
      service: http_status:404
    - hostname: shield.example.com
      service: http://localhost:8842
  ```

## Operations

### Volume backup

The named volume `shield-data` holds:

- `/app/data/shield.db` — config, sources, channels, findings, users (**precious**)
- `/app/data/feeds.db` — cached advisories (regenerable; the workers resync on next tick)
- `/app/data/keys/` — DataProtection keyring (**precious — losing it invalidates every stored
  OAuth token, every Discord webhook URL, every OIDC client secret, AND every active
  session cookie**)

Snapshot it while Shield is running (SQLite WAL mode handles concurrent readers):

```bash
docker run --rm \
  -v shield-data:/data:ro \
  -v "$(pwd)":/backup \
  alpine tar czf /backup/shield-$(date +%F).tar.gz -C / data
```

Restore on a fresh host:

```bash
docker volume create shield-data
docker run --rm \
  -v shield-data:/data \
  -v "$(pwd)":/backup \
  alpine sh -c "cd / && tar xzf /backup/shield-2026-05-16.tar.gz"
docker compose up -d
```

Test the restore on a throwaway host before you need it. A backup nobody has restored
from is a wish, not a backup.

### Logs

```bash
docker compose logs -f shield                         # live tail
docker compose logs --since=1h shield                 # last hour
docker compose logs --tail=200 shield | grep -i error # recent errors
```

The Compose file caps json-file logs at 3 × 10 MB per container. Ship to durable storage
(Loki, Promtail → S3, journald with `--log-driver=journald`) for anything you need to
audit later.

### Migrations

EF Core migrations run automatically on boot for both `ShieldDbContext` and
`FeedsDbContext`. Watch for `Applying migration` lines on the first boot after an
upgrade. If a migration fails Shield exits and the container restarts in a loop — read
`docker compose logs shield` and fix forward (the broken migration must be authored
correctly upstream; a deploy is not the place to hand-patch SQL).

### Upgrade

```bash
docker compose pull         # pulls the new tag if using the published image
docker compose up -d        # recreates the shield container, runs migrations
docker compose logs -f shield
```

Watch for `Now listening` + `Application started`. If the upgrade boots cleanly, prune
the old image:

```bash
docker image prune -f
```

If the gate refuses to start with the new image (new env vars, tightened minimums), fix
`.env` and re-run `docker compose up -d`. The previous DB schema is untouched until the
new image successfully runs its migrations.

### Runtime image

The published image is `ghcr.io/nomercylabs/shield`. The compose file ships with a local
build target (`shield:local`) while the first versioned tag is being cut. To switch to the
published image, uncomment the `image: ghcr.io/nomercylabs/shield:latest` line in
`docker/docker-compose.yml` and comment out the `build:` block.

### Building from source

```bash
docker compose build --pull
docker compose up -d
```

Build context is the repo root, dockerfile is `docker/Dockerfile`. SPA assets are baked
into the runtime image — any change under `src/Shield.Web/` requires a rebuild + redeploy.

## Choosing cloudflared host vs sidecar

| | Host-side | Sidecar |
|---|---|---|
| Setup complexity | Higher (install cloudflared on the OS) | Lower (everything is `docker compose up -d`) |
| Survives Shield container restarts | Yes | Yes (sidecar reconnects on its own) |
| Survives host reboots | Needs systemd unit | Yes (Docker autostarts the stack) |
| Tunnel logs land in | systemd-journald | `docker compose logs cloudflared` |
| Easy to move to another host | No (re-install per host) | Yes (move the `.env` + compose files) |
| Best for | Long-lived dedicated boxes (Stoney's Proxmox) | Ephemeral / multi-stack hosts |

Default to host-side unless you have a reason not to. The sidecar adds one more thing
that can crash-loop independently of Shield itself.

## Hard things to remember

- **Wiping the `shield-data` volume invalidates every encrypted secret and every active
  cookie.** Don't `docker volume rm` it as a debugging step. Stop, snapshot, then wipe
  if you must.
- **SPA changes require an image rebuild.** Editing files under `src/Shield.Web/` and
  restarting the container does nothing — the runtime image was published with the old
  `wwwroot/`. `docker compose build` (or pulling a new tag) is mandatory.
- **The first user wins.** If you stand up a public tunnel before registering the admin,
  the next person to discover `/api/auth/register` is your admin. Either register
  through localhost / the LAN BEFORE flipping DNS, or put Cloudflare Access in front of
  the host from day one.
- **`ASPNETCORE_ENVIRONMENT=Development` disables every safety gate.** Don't set it in
  `.env` "to debug". Read `docker compose logs` instead.
