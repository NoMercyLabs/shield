# Channels

An **alert channel** is a destination Shield ships matched findings to.

| Field | Type | Notes |
|---|---|---|
| `Name` | string | Display name |
| `Type` | enum | `Discord`, `Ntfy`, `Smtp`, `Inbox`, `Slack`, `Webhook` |
| `ConfigJson` | string | Type-specific JSON. **Encrypted at rest** with ASP.NET Data Protection (`shield.channels` purpose). Never returned raw |
| `MinSeverity` | enum | Findings below this severity are filtered out for this channel |
| `Enabled` | bool | When false, the dispatcher skips this channel |

## Config schemas

### Discord

```json
{ "webhookUrl": "https://discord.com/api/webhooks/<id>/<token>" }
```

The webhook URL is encrypted server-side; reads return a redacted form (`https://discord.com/api/webhooks/****/****`) in the `configMasked` field.

### Slack

Supports two modes — legacy Incoming Webhook or OAuth (`chat.postMessage`). Exactly one field is required.

```json
{ "webhookUrl": "https://hooks.slack.com/services/<T>/<B>/<token>" }
```

Or, when using an OAuth bot token wired through **Settings → Integrations → Slack**:

```json
{ "channelId": "C0123456789" }
```

When `channelId` is present, `webhookUrl` is ignored and the stored OAuth access token is used to post.

### Ntfy

```json
{
  "url": "https://ntfy.sh/your-topic",
  "title": "Shield Alert",
  "priority": 4,
  "tags": ["shield", "vuln"],
  "authToken": "tk_..."
}
```

`title`, `priority`, `tags`, and `authToken` are optional. `priority` follows ntfy's 1–5 scale (default 3).

### SMTP

```json
{
  "host": "smtp.example.com",
  "port": 587,
  "useStartTls": true,
  "username": "alerts@example.com",
  "password": "...",
  "from": "alerts@example.com",
  "to": ["you@example.com"],
  "fromName": "Shield"
}
```

`username`, `password`, and `fromName` are optional. `useOAuth` is a boolean flag for OAuth-backed SMTP (requires the relevant integration token to be stored via **Settings → Integrations**).

### Webhook (generic outbound HTTP)

```json
{
  "url": "https://hooks.example.com/shield",
  "method": "POST",
  "headers": { "X-Api-Key": "..." },
  "bodyTemplate": null
}
```

`method` defaults to `POST`. `headers` and `bodyTemplate` are optional. When `bodyTemplate` is null, Shield posts a JSON object with the finding details. Shield sends the raw URL; no encryption is applied beyond the `ConfigJson` envelope.

### Inbox

No config. The in-app inbox is always on and persists until the user clears it. Creating an `Inbox` channel with an empty `configJson` (`{}`) is valid; the type is its own configuration.

## API

All endpoints require authentication. Channels are Admin-only — `[NoApiToken]` applies.

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
