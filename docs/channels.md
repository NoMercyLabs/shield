# Channels

An **alert channel** is a destination Shield ships matched findings to. Phase 1 ships Discord webhooks and an in-app Inbox; ntfy and SMTP are roadmap items.

| Field | Type | Notes |
|---|---|---|
| `Name` | string | Display name |
| `Type` | enum | `Discord`, `Ntfy`, `Smtp`, `Inbox` |
| `ConfigJson` | string | Type-specific JSON. **Encrypted at rest** with ASP.NET Data Protection (`shield.channels` purpose). Never returned raw |
| `MinSeverity` | enum | Findings below this severity are filtered out for this channel |
| `Enabled` | bool | When false, the dispatcher skips this channel |

## Config schemas

### Discord

```json
{ "webhookUrl": "https://discord.com/api/webhooks/<id>/<token>" }
```

The webhook URL is encrypted server-side; reads return a redacted form (`https://discord.com/api/webhooks/****/****`) in the `configMasked` field. Test-send and dispatch decrypt internally before hitting Discord.

## API

All endpoints require authentication.

| Verb | Path | Purpose |
|---|---|---|
| `GET` | `/api/channels` | List all channels (config is masked) |
| `GET` | `/api/channels/{id}` | Channel detail (config is masked) |
| `POST` | `/api/channels` | Create a channel. Body: `{ type, name, configJson, minSeverity, enabled? }`. Returns `{ id, type, name, configMasked, minSeverity, enabled }` |
| `PUT` | `/api/channels/{id}` | Update channel. Body: `{ name, configJson, minSeverity, enabled }` — `configJson` is re-encrypted on every write |
| `DELETE` | `/api/channels/{id}` | Remove the channel. 204 No Content |
| `POST` | `/api/channels/{id}/test-send` | Decrypt config and dispatch a single synthetic test finding (Severity=Low, "Shield test alert"). Returns `{ success, delivered, error? }` |

## Encryption notes

`AlertChannel.ConfigJsonEncrypted` is the protected ciphertext stored in SQLite. The protector purpose is `shield.channels`. Rotating the Data Protection key ring invalidates existing rows — they'll surface as `[unreadable]` on reads and be skipped by the dispatch worker with a warning log. Re-add affected channels after rotation.
