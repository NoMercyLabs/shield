using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shield.Core.Options;
using Shield.Data.Identity;

namespace Shield.Api.Auth;

// Auto-authenticates as the seeded single-user@shield.local Admin when Shield:SingleUser=true
// AND no real Identity cookie was supplied. Lives alongside the cookie scheme so the real auth
// pipeline keeps running — this is a convenience layer, not a bypass.
public sealed class SingleUserAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "SingleUser";

    private readonly SignInManager<ShieldUser> _signInManager;
    private readonly UserManager<ShieldUser> _userManager;
    private readonly IOptions<ShieldOptions> _shieldOptions;

    public SingleUserAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        SignInManager<ShieldUser> signInManager,
        UserManager<ShieldUser> userManager,
        IOptions<ShieldOptions> shieldOptions
    ) : base(options, logger, encoder)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _shieldOptions = shieldOptions;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!_shieldOptions.Value.SingleUser)
            return AuthenticateResult.NoResult();

        // If a real cookie session already authenticated, defer to it.
        AuthenticateResult cookie = await Context.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        if (cookie.Succeeded)
            return AuthenticateResult.NoResult();

        ShieldUser? user = await _userManager.FindByNameAsync(IdentitySeeder.SingleUserName);
        if (user is null)
            return AuthenticateResult.NoResult();

        System.Security.Claims.ClaimsPrincipal principal = await _signInManager.CreateUserPrincipalAsync(user);
        AuthenticationTicket ticket = new(principal, SchemeName);
        return AuthenticateResult.Success(ticket);
    }
}
