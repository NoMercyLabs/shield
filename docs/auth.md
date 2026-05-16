# Auth

Shield's auth model is deliberately small. Defaults work for solo installs without any configuration. Multi-user is supported but Phase 1 has no UI for adding users yet — see below.

## Single-user mode (default)

```env
Shield__SingleUser=true
```

This is the default in `appsettings.json`. A request middleware short-circuits authentication and treats every incoming request as `Admin`. There is no login screen, no password to set, no session to manage.

**When to use it:**

- You're running Shield on `localhost` or behind a VPN
- You're running it behind a reverse proxy that handles auth (Caddy + Authelia, Cloudflare Access, etc.)
- You're the only person who will ever touch this instance

**When NOT to use it:**

- The Shield port is reachable from the public internet without an auth-enforcing proxy in front of it. Single-user mode skips ALL authentication checks — anyone who can reach `:8080` becomes Admin.

## Multi-user mode

```env
Shield__SingleUser=false
```

Disables the bypass middleware. ASP.NET Identity takes over:

- Cookie auth for the SPA
- JWT bearer for API clients (`Shield__Auth__JwtSigningKey` is the symmetric signing key)
- TOTP 2FA enrollment + verification endpoints
- Lockout on repeated failed sign-ins
- Roles: `Admin`, `Viewer`

### Seeding the first user

**Phase 1 has no registration endpoint and no admin-seed env var.** This is a known gap. To bootstrap multi-user mode today you need to insert a row into `AspNetUsers` in `shield.db` directly (and a matching row in `AspNetUserRoles` to grant `Admin`). Phase 2 will add either an admin-seed env var or a one-time setup wizard — pick whichever the issue thread converges on.

If you've turned off single-user mode without seeding a user, the login endpoint will reject every credential — there's nothing to authenticate against.

## OIDC

OIDC (Keycloak / Authentik / Auth0 / GitHub) is **planned for Phase 2**. The spec defines an `OidcConfig` row and the `Shield:Oidc:Enabled` flag, but Phase 1 ships **no OIDC handler code**. Don't set `Shield:Oidc:Enabled=true` yet — there's nothing on the other end of that flag.

When OIDC lands in Phase 2, it will:

- Add an OIDC handler alongside (not replacing) local Identity — admins choose whether to disable local sign-in
- Map OIDC claims to Shield roles via configurable claim -> role rules
- Be tested against Keycloak, Authentik, GitHub, and Auth0

A separate `docs/auth-oidc.md` will land alongside the implementation.

## API tokens

API clients can sign in via `POST /api/auth/login` and receive a JWT bearer token signed with `Shield__Auth__JwtSigningKey`. Send it as `Authorization: Bearer <token>` on subsequent requests.

In single-user mode the bypass middleware also short-circuits API requests, so no token is needed when `Shield__SingleUser=true`. Don't expose this to anything you don't fully trust.

## Agent auth

Linux agent enrollment (Phase 2) uses bearer tokens minted in the UI, hashed at rest with Argon2, single-use until rotated. Replay protection via timestamp + nonce window. None of this code ships in Phase 1.
