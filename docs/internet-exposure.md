# Exposing Shield on the public internet

Shield was designed for a LAN-only deploy. The defaults are dev-friendly: single-user
auto-auth, loose password policy, no rate limiting, no HSTS, no CSP, OpenAPI on. None
of that is safe on the public internet.

The production-safety gate **refuses to start** in any non-Development environment unless
the configuration is internally consistent for public exposure. This document covers the
recipe.

## TL;DR

1. Put Shield behind a reverse proxy that terminates TLS.
2. Set `Shield__Public=true`, `Shield__Auth__RequireHttps=true`, and
   `Shield__SingleUser=false`.
3. Generate fresh `Shield__Auth__JwtSigningKey` (≥48 chars) and
   `Shield__Auth__DataProtectionMasterKey` (≥32 chars).
4. Disable OpenAPI (`Shield__OpenApi__Enabled=false`, or unset).
5. Pair with a zero-trust layer (Cloudflare Access / Tailscale Funnel / Authentik OIDC)
   if the data being browsed is sensitive enough to warrant it.

## Required environment variables

| Variable | Required | Minimum | Notes |
|---|---|---|---|
| `Shield__Public` | yes | `true` | Marks the host as internet-exposed. Tightens password policy, blocks Swagger unconditionally, triggers OAuth token revocation + SecurityStamp bump on first boot in the new posture. |
| `Shield__Auth__RequireHttps` | yes when Public | `true` | Enables `Strict-Transport-Security`, `Secure` cookies, `SameSite=Strict`, and the HTTPS redirect. |
| `Shield__SingleUser` | should be `false` | `false` | The auto-Admin handler is a convenience for solo LAN deploys. Set `false` and register a real Admin via `/api/auth/register`. If you must keep it, set `Shield__Auth__AllowSingleUserInProduction=true` to acknowledge the risk. |
| `Shield__Auth__JwtSigningKey` | yes | 48 chars | `openssl rand -base64 48`. |
| `Shield__Auth__DataProtectionMasterKey` | yes | 32 chars | `openssl rand -base64 48`. Encrypts the keyring under `Shield__Auth__DataProtectionKeysPath` — losing it makes every stored secret unrecoverable. |
| `Shield__OpenApi__Enabled` | should be `false` | `false` | Swagger publishes every controller route and DTO shape. Public Shield refuses to serve it even when this is `true`. |
| `Shield__Auth__DataProtectionKeysPath` | optional | `/data/keys` (Docker) | Where the DataProtection keyring lives. Back this directory up. |

## Production-safety gate failures

The gate runs once at startup. When it fails it lists every offending knob plus a
remediation hint. The full failure list:

- **`Shield:SingleUser=true` outside Development is refused.** Solo operators on the
  public internet expose an auto-Admin session to anyone who reaches the host. Either
  flip `Shield__SingleUser=false` and register a real Admin via `/api/auth/register`,
  or explicitly accept the risk with `Shield__Auth__AllowSingleUserInProduction=true`
  (NOT recommended).

- **`Shield:OpenApi:Enabled=true` outside Development is refused.** Swagger reveals
  every endpoint to anyone who can `GET /swagger`. Unset the variable.

- **`Shield:Public=true` requires `Shield:Auth:RequireHttps=true`.** Cookies, JWTs, and
  OAuth callbacks travel over the wire; refusing HTTPS is a credentials-leak in waiting.

- **`Shield:Auth:JwtSigningKey` must be ≥48 characters in non-Development.** HMAC-SHA256
  collapses below 32 bytes of entropy; 48 base64 chars is the floor.

- **`Shield:Auth:DataProtectionMasterKey` must be ≥32 characters** and **must not be
  the dev default** (`dev-master-key-at-least-32-chars-long-xx`). Every install on the
  internet sharing that key would share the same DataProtection envelope.

## Caddy reverse proxy recipe

Save as `Caddyfile`:

```caddy
shield.example.com {
    encode zstd gzip

    # Forward client IP + scheme so Shield's HttpsRedirection sees the real https:// scheme
    # and does not bounce-loop back to the proxy.
    reverse_proxy shield:8842 {
        header_up X-Forwarded-Proto {scheme}
        header_up X-Forwarded-For {remote_host}
    }
}
```

Bring it up with the existing `docker-compose.yml` reference — the Compose file already
ships a `caddy` sidecar. Make sure your DNS A record for `shield.example.com` points at
the public IP, and Caddy will provision a Let's Encrypt cert on first request.

## Extra zero-trust layer (recommended)

The hardening above stops casual scanning + credential stuffing, but the moment Shield
is on the internet a fresh CVE in ASP.NET (or in Shield itself) becomes your problem on
the operator's timeline, not Microsoft's. For anything resembling sensitive data,
front Shield with one of:

- **Cloudflare Access** — free tier supports email-based OTP and Google/GitHub SSO,
  with WAF + bot mitigation in front of the origin.
- **Tailscale Funnel** — exposes the container only to your Tailscale tailnet plus
  optional allowlisted public endpoints.
- **Authentik / Keycloak OIDC** — forwards an authenticated header from a reverse
  proxy that Shield's OIDC layer will eventually consume (Phase 2).

## Operational checklist before flipping `Shield:Public=true`

- [ ] Reverse proxy terminating TLS with a valid certificate
- [ ] `Shield__Auth__JwtSigningKey` rotated from any pre-public value
- [ ] `Shield__Auth__DataProtectionMasterKey` backed up to a password manager
- [ ] At least one real Admin account registered (so `SingleUser=false` doesn't lock
  you out)
- [ ] OAuth integration secrets re-issued (the first boot in Public mode revokes every
  `IntegrationToken` and bumps every user's `SecurityStamp` — re-authenticate from a
  trusted machine)
- [ ] Backup strategy for `/data/shield.db` + `/data/keys/`
- [ ] Log shipping pointed at something durable so the audit log survives a host loss

When all boxes are ticked, set `Shield__Public=true` and restart. The gate will either
accept the configuration silently, or refuse to start with a concrete failure list.
