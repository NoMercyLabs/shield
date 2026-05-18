using Microsoft.AspNetCore.Mvc.Filters;

namespace Shield.Api.Auth;

// Hard-blocks api-token bearers from sensitive surfaces (settings, channels, access mgmt,
// audit, the api-token endpoints themselves). Returns 403 — the bearer authenticated fine,
// but the principal type is wrong for this surface.
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public sealed class NoApiTokenAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        bool isApiToken = context.HttpContext.User.HasClaim(claim =>
            claim.Type == ApiTokenAuthHandler.TokenIdClaim
        );
        if (isApiToken)
            context.Result = new ForbidResult();
    }
}
