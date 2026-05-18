# Operator backup + restore

Shield stores everything an operator owns under `/app/data/` inside the container (host bind-mount usually `./data/`):

```
data/
├── shield.db                      ← findings, sources, settings, users, sessions, audit, security events
├── shield.db-shm + shield.db-wal  ← SQLite WAL (live; copy together with the .db)
├── feeds.db                       ← OSV / GHSA / KEV / EPSS / NpmRegistry advisory mirror
├── feeds.db-shm + feeds.db-wal
└── keys/
    └── key-XXX.xml                ← DataProtection keyring (encrypts AppSettings secrets)
```

**Losing `data/keys/`** without the `Shield__Auth__DataProtectionMasterKey` env var means **every encrypted setting becomes unrecoverable** — OAuth client secrets, OIDC client secrets, Discord/Slack webhook URLs, SMTP passwords. Back up `data/keys/` alongside the DBs.

**Losing `Shield__Auth__DataProtectionMasterKey`** when `keys/` is encrypted means the same thing. Store the master key in your password manager.

## Backup

### Online (no downtime, WAL-safe)

```bash
cd <shield-install-dir>
mkdir -p backups
tar czf backups/shield-$(date -u +%Y%m%d-%H%M%S).tgz data/
```

The SQLite WAL file is captured alongside the main `.db`. SQLite recovers from this pair on next open; no checkpoint needed.

### Cold (clean, smallest archive)

```bash
docker compose down
tar czf backups/shield-$(date -u +%Y%m%d-%H%M%S).tgz data/
docker compose up -d
```

Roughly 5 seconds of downtime. WAL is flushed into the main `.db` before tar; archive is ~30% smaller.

### Recommended cadence

| Surface | Cadence | Why |
|---|---|---|
| `data/` directory | Nightly | Findings + audit are append-mostly; small daily archive |
| `data/keys/` only | Once on install + after every key rotation | Tiny; the catastrophe-recovery anchor |
| Env file (`.env` or compose) | After every change | `Shield__Auth__DataProtectionMasterKey` and `Shield__Auth__JwtSigningKey` live here |

Keep at least 7 daily + 4 weekly archives. Drop the rest.

## Restore

### Same host, after data loss

```bash
docker compose down
rm -rf data/
tar xzf backups/shield-YYYYMMDD-HHMMSS.tgz
docker compose up -d
```

Shield runs the pending migrations on boot; if the archive predates a schema change, EF picks up where it left off. Tested across the v0.1 schema; older snapshots may need manual migration steps if the schema was breaking-changed (release notes call those out).

### New host

1. Ship the `.env` to the new host (or recreate the same values — `DataProtectionMasterKey` MUST match).
2. Drop the archive into the new install's `data/` location.
3. `docker compose up -d`.

If `DataProtectionMasterKey` differs, encrypted settings won't decrypt. Re-enter them via Settings.

## DataProtection key rotation

ASP.NET Core rotates DataProtection keys every 90 days by default. Old keys archive automatically — Shield's `data/keys/` accumulates one `key-XXX.xml` per rotation. Don't manually prune — old keys are still needed to decrypt settings written before rotation.

If `Shield__Auth__DataProtectionMasterKey` changes:
1. Existing keyring keys can no longer be decrypted by the new master → settings encrypted under the old master are lost.
2. The fix is **never to change the master key in place**. Treat it as immutable per install.
3. If you must rotate the master, do it offline: spin up a fresh `data/keys/` directory, re-enter every encrypted setting through the SPA, then start using the new master.

## What's NOT in `data/`

- **Application logs** — stdout of the container. Shipped through your log collector (journald / Loki / etc.), not Shield's job to retain.
- **Scan queue working state** — lives in `shield.db` (`ScanQueueEntries` table), so it's covered.
- **Push subscriptions** — in `shield.db`. Subscribers reauthorise on next browser visit if lost.

## Sanity check after restore

```bash
curl -sf http://localhost:8842/healthz                       # 200 ok
curl -sf http://localhost:8842/api/feeds                     # confirms feed states load
docker logs shield 2>&1 | grep -E 'Shield posture|migration' # confirms posture banner + clean migration
```

If `/api/feeds` errors with a DataProtection exception, your keyring + master-key pair is mismatched — see "New host" above.
