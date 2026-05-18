using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Shield.Api.Auth;

// Recognises `Authorization: Bearer shld_<secret>` and authenticates the request as the
// owning user, plus an `api-token` claim carrying the token id and a `api-scope` claim per
// granted scope. Returns NoResult on anything that isn't an shld_ bearer so cookie/JWT/
// SingleUser fall-through schemes still get a shot.
public sealed class ApiTokenAuthHandler : AuthenticationHandler<ApiTokenAuthOptions>
{
    public const string SchemeName = "ApiToken";
    public const string TokenIdClaim = "api-token";
    public const string ScopeClaim = "api-scope";
    public const string SourceFilterClaim = "api-source-filter";

    private readonly IApiTokenStore _tokenStore;
    private readonly UserManager<ShieldUser> _userManager;

    public ApiTokenAuthHandler(
        IOptionsMonitor<ApiTokenAuthOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IApiTokenStore tokenStore,
        UserManager<ShieldUser> userManager
    )
        : base(options, logger, encoder)
    {
        _tokenStore = tokenStore;
        _userManager = userManager;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? header = Request.Headers.Authorization;
        if (
            string.IsNullOrEmpty(header)
            || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
        )
            return AuthenticateResult.NoResult();

        string presented = header.Substring("Bearer ".Length).Trim();
        if (!presented.StartsWith(ApiTokenStore.TokenPrefix, StringComparison.Ordinal))
            return AuthenticateResult.NoResult();

        string? remoteIp = Context.Connection.RemoteIpAddress?.ToString();
        ApiToken? token = await _tokenStore.FindByPlaintextAsync(presented, remoteIp);
        if (token is null)
        {
            // Log only the token prefix — full plaintext is sensitive and the secret half
            // is randomised, so a prefix is enough to spot scanning bursts in the timeline.
            int prefixLength = Math.Min(presented.Length, ApiTokenStore.TokenPrefix.Length + 4);
            await TryLogSecurityAsync(
                "apitoken.failed",
                Severity.Medium,
                detailsJson: $"{{\"prefix\":\"{presented.Substring(0, prefixLength)}\"}}"
            );
            return AuthenticateResult.Fail("Invalid or revoked API token.");
        }

        ShieldUser? user = await _userManager.FindByIdAsync(token.UserId.ToString());
        if (user is null)
            return AuthenticateResult.Fail("API token owner no longer exists.");

        IList<string> roles = await _userManager.GetRolesAsync(user);

        List<Claim> claims =
        [
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.UserName ?? string.Empty),
            new(TokenIdClaim, token.Id.ToString()),
        ];
        foreach (string role in roles)
            claims.Add(new(ClaimTypes.Role, role));
        if (!string.IsNullOrEmpty(token.Scopes))
        {
            foreach (string scope in token.Scopes.Split(',', StringSplitOptions.RemoveEmptyEntries))
                claims.Add(new(ScopeClaim, scope));
        }
        if (!string.IsNullOrEmpty(token.SourceIdFilter))
            claims.Add(new(SourceFilterClaim, token.SourceIdFilter));

        ClaimsIdentity identity = new(claims, SchemeName, ClaimTypes.Name, ClaimTypes.Role);
        ClaimsPrincipal principal = new(identity);
        AuthenticationTicket ticket = new(principal, SchemeName);
        return AuthenticateResult.Success(ticket);
    }

    private async Task TryLogSecurityAsync(
        string eventType,
        Severity severity,
        string? detailsJson = null
    )
    {
        try
        {
            ISecurityEventLogger? logger =
                Context.RequestServices.GetService<ISecurityEventLogger>();
            if (logger is null)
                return;
            await logger.LogAsync(
                source: "shield.apitoken",
                eventType: eventType,
                severity: severity,
                remoteIp: Context.Connection.RemoteIpAddress?.ToString(),
                userAgent: Context.Request.Headers.UserAgent.ToString() is { Length: > 0 } ua
                    ? ua
                    : null,
                path: Context.Request.Path.Value,
                detailsJson: detailsJson
            );
        }
        catch
        {
            // Auth must not fail because the security log failed.
        }
    }
}
