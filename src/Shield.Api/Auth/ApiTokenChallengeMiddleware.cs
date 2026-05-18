using Microsoft.AspNetCore.Authentication;
using Shield.Api.Services;

namespace Shield.Api.Auth;

// When the caller presented an `shld_` bearer, the ONLY scheme allowed to authenticate the
// request is ApiToken. Without this gate the default policy keeps walking the scheme list
// and a cookie / SingleUser / JWT principal can still satisfy the request — meaning a
// revoked or expired api-token effectively falls back to the seeded SingleUser admin.
//
// Runs after UseAuthentication, before UseAuthorization. Cheap header sniff — no DB hit;
// the actual lookup already happened in ApiTokenAuthHandler.
public static class ApiTokenChallengeMiddleware
{
    public static IApplicationBuilder UseApiTokenChallengeGate(this IApplicationBuilder app)
    {
        return app.Use(
            async (context, next) =>
            {
                string? header = context.Request.Headers.Authorization;
                if (
                    !string.IsNullOrEmpty(header)
                    && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                )
                {
                    string presented = header.Substring("Bearer ".Length).Trim();
                    if (presented.StartsWith(ApiTokenStore.TokenPrefix, StringComparison.Ordinal))
                    {
                        AuthenticateResult result = await context.AuthenticateAsync(
                            ApiTokenAuthHandler.SchemeName
                        );
                        if (!result.Succeeded)
                        {
                            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                            return;
                        }
                        // Replace User with the api-token principal so downstream auth /
                        // controllers see the correct identity even though the default policy
                        // is multi-scheme. Otherwise SingleUser fallthrough could still win.
                        context.User = result.Principal!;
                    }
                }
                await next(context);
            }
        );
    }
}
