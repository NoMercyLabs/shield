using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Shield.Api.Auth;

// Hard-blocks any request whose principal carries the `imp.admin` claim. The admin
// authenticated fine, but Shield's policy is "destructive actions require the real seat" —
// an admin viewing as a user cannot revoke that user's invite, delete a group, change
// settings, or rotate auth. The override has to be dropped first (POST /api/impersonation/stop).
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public sealed class RequireOriginalIdentityAttribute : Attribute, IAuthorizationFilter
{
    public const string ImpersonatorClaimType = "imp.admin";

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        bool isImpersonating = context.HttpContext.User.HasClaim(claim =>
            claim.Type == ImpersonatorClaimType
        );
        if (!isImpersonating)
            return;
        string action =
            context.RouteData.Values["controller"] is string controller
            && context.RouteData.Values["action"] is string method
                ? $"{controller}.{method}"
                : context.HttpContext.Request.Path.ToString();
        context.Result = new ObjectResult(new { error = "impersonation_blocked", action })
        {
            StatusCode = StatusCodes.Status403Forbidden,
        };
    }
}
