using Microsoft.AspNetCore.Mvc.Filters;

namespace Shield.Api.Auth;

// Action filter that succeeds when:
//   - the principal isn't an api-token (cookie/JWT principals already passed the role
//     policy), OR
//   - the api-token's scope set contains the required value (or a wildcard `*`).
// Layer on top of [Authorize] — this filter doesn't authenticate, it just narrows what an
// api-token is allowed to do once authenticated.
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
public sealed class RequireApiScopeAttribute : Attribute, IAuthorizationFilter
{
    public string Scope { get; }

    public RequireApiScopeAttribute(string scope)
    {
        Scope = scope;
    }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        bool isApiToken = context.HttpContext.User.HasClaim(claim =>
            claim.Type == ApiTokenAuthHandler.TokenIdClaim
        );
        if (!isApiToken)
            return;

        bool granted = context.HttpContext.User.Claims.Any(claim =>
            claim.Type == ApiTokenAuthHandler.ScopeClaim
            && (
                string.Equals(claim.Value, Scope, StringComparison.OrdinalIgnoreCase)
                || claim.Value == "*"
            )
        );
        if (!granted)
            context.Result = new ForbidResult();
    }
}
