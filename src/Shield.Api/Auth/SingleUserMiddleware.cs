using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Shield.Api.Auth;

// SECURITY: SingleUser mode bypasses all auth and treats every request as Admin. Only safe for solo,
// network-isolated deployments where the operator owns the box. Never enable behind public ingress.
public sealed class SingleUserMiddleware
{
    public const string SyntheticUserId = "00000000-0000-0000-0000-000000000001";
    public const string SyntheticUsername = "single-user";

    private readonly RequestDelegate _next;

    public SingleUserMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            ClaimsIdentity identity = new(CookieAuthenticationDefaults.AuthenticationScheme);
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, SyntheticUserId));
            identity.AddClaim(new Claim(ClaimTypes.Name, SyntheticUsername));
            identity.AddClaim(new Claim(ClaimTypes.Role, ShieldRoles.Admin));
            context.User = new ClaimsPrincipal(identity);
        }
        return _next(context);
    }
}

public static class ShieldRoles
{
    public const string Admin = "Admin";
    public const string Viewer = "Viewer";
}
