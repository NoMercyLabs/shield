namespace Shield.Api.Auth.External;

// Signin-flow contract: a provider knows how to start a device-style sign-in (returns a
// flow handle the SPA polls) and how to materialise an ExternalIdentity once the user has
// authorised the request upstream. Adding GitLab / Bitbucket / Gitea / Forgejo later is a
// matter of registering a new IExternalLoginProvider — the controller, store, contracts,
// SPA card, and audit pipeline already speak the generic shape.
//
// Distinct from `IOAuthProvider` (Auth/OAuthProviders/) on purpose: that family is the
// CONNECT-flow surface for source-scanning bearer tokens and writes to IntegrationToken.
// This family is the SIGNIN-flow surface and maps to AspNetUserLogins.
public interface IExternalLoginProvider
{
    // Stable lowercase key. SPA buttons + `/api/auth/external/{key}/...` route on this.
    string Key { get; }

    // Human-readable label rendered on the signin button.
    string DisplayName { get; }

    // lucide icon key the SPA renders next to the label. Keep alongside DisplayName so
    // future self-hosted-Gitea instances can ship their own brand without a SPA rebuild.
    string IconKey { get; }

    // Kicks off the upstream device-code request and returns the data the SPA needs to
    // walk the user through authorisation. `flowId` is server-issued; the upstream
    // device_code never crosses the wire.
    Task<ExternalLoginStartResult> StartSigninAsync(string returnPath, CancellationToken ct);

    // Polls the upstream flow keyed by the server-issued `flowId`. Outcomes:
    //   - `Pending` → caller surfaces "waiting" UI and re-polls
    //   - `SlowDown` → caller bumps its polling interval per RFC 8628
    //   - `Expired` / `Denied` → caller surfaces terminal state, drops the flow
    //   - `Ok(identity)` → caller checks AspNetUserLogins for a linked ShieldUser
    Task<ExternalLoginPollResult> PollSigninAsync(string flowId, CancellationToken ct);
}

// Returned by StartSigninAsync. flowId is the SPA-facing handle; the SPA polls
// /api/auth/external/{provider}/poll with it. verificationUriComplete may be null on
// providers that don't pre-fill the user_code in the verification URL.
public sealed record ExternalLoginStartResult(
    string FlowId,
    string UserCode,
    string VerificationUri,
    string? VerificationUriComplete,
    int Interval,
    int ExpiresIn
);

// Discriminated outcome of one poll cycle. Exactly one of the non-Status fields is set
// per outcome; the controller switches on Status to project the wire response.
public sealed record ExternalLoginPollResult(
    ExternalLoginPollStatus Status,
    ExternalIdentity? Identity = null
);

public enum ExternalLoginPollStatus
{
    Pending,
    SlowDown,
    Expired,
    Denied,
    Ok,
    Error,
}

// What the provider produces once the user has authorised the signin request. Provider
// matches the registered Key so downstream code (AspNetUserLogins lookup, audit log)
// stays string-keyed and never reaches back to the adapter.
public sealed record ExternalIdentity(
    string Provider,
    string SubjectId,
    string Login,
    string? Email,
    string? AvatarUrl,
    string AccessToken,
    IReadOnlyList<string> Scopes
);
