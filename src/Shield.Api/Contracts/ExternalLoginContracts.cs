namespace Shield.Api.Contracts;

// Public listing for `GET /api/auth/external/providers`. The SPA renders one button per
// entry; IconKey is a lucide icon name so we don't ship per-provider asset URLs.
public sealed record ExternalLoginProviderInfo(string Key, string DisplayName, string IconKey);

public sealed record ExternalLoginProvidersResponse(
    IReadOnlyList<ExternalLoginProviderInfo> Providers
);

// Body for `POST /api/auth/external/{provider}/start`. ReturnPath is the in-app route to
// redirect to once signin completes (defaults to "/"); validated server-side so an
// open-redirect can't piggyback.
public sealed record ExternalLoginStartRequest(string? ReturnPath);

// Mirrors the device-flow start shape so the SPA's existing DeviceLoginPanel can render
// for any registered provider without per-provider plumbing.
public sealed record ExternalLoginStartResponse(
    string FlowId,
    string UserCode,
    string VerificationUri,
    string? VerificationUriComplete,
    int Interval,
    int ExpiresIn
);

public sealed record ExternalLoginPollRequest(string FlowId);

// Status discriminates the rest of the payload:
//   - "pending" / "slow_down"  → keep polling (slow_down bumps interval +5s per RFC 8628)
//   - "expired" / "denied"     → terminal; the SPA shows the matching error
//   - "ok" + identity == null  → the user IS authenticated upstream and a Shield session
//                                cookie is now set on this response. SPA refetches /me.
//   - "ok" + identity present + needsInvite == true → no AspNetUserLogins link exists for
//                                this (provider, subject); SPA shows "ask the admin to
//                                invite you" with the captured identity for copy-paste.
public sealed record ExternalLoginPollResponse(
    string Status,
    bool NeedsInvite = false,
    ExternalLoginIdentityInfo? Identity = null,
    string? ReturnPath = null,
    // Short-lived signed envelope that proves the identity was just verified upstream. The
    // SPA passes this back to /api/auth/accept-invite alongside the invite token so the
    // accept handler doesn't need to re-probe GitHub. Issued only when NeedsInvite=true.
    string? AcceptanceTicket = null
);

public sealed record ExternalLoginIdentityInfo(
    string Provider,
    string Login,
    string? Email,
    string? AvatarUrl
);
