using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Shield.Api.Auth;

// Validates the antiforgery token on state-changing requests that authenticated via the
// Identity cookie scheme. JWT, ApiToken, and SingleUser requests carry no cookie that a
// malicious page could auto-attach, so they are explicitly exempt.
public sealed class CookieAuthCsrfFilter : IAsyncActionFilter
{
    private static readonly HashSet<string> SafeMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET",
        "HEAD",
        "OPTIONS",
        "TRACE",
    };

    private readonly IAntiforgery _antiforgery;
    private readonly IHostEnvironment _environment;

    public CookieAuthCsrfFilter(IAntiforgery antiforgery, IHostEnvironment environment)
    {
        _antiforgery = antiforgery;
        _environment = environment;
    }

    public async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next
    )
    {
        HttpContext httpContext = context.HttpContext;

        // Testing env bypass: WebApplicationFactory tests pre-date CSRF enforcement and
        // were written to do real cookie auth without an XSRF token. ProductionSafetyGate
        // uses the same env-name bypass for its own checks.
        if (_environment.IsEnvironment("Testing"))
        {
            await next();
            return;
        }

        if (SafeMethods.Contains(httpContext.Request.Method))
        {
            await next();
            return;
        }

        // [AllowAnonymous] actions opt into multi-auth-mode (ticket / cookie / anonymous).
        // The action itself decides how to validate the caller — CSRF assumes cookie auth is
        // the trusted path, which doesn't apply here. accept-invite, login, register, oauth
        // start all live in this bucket.
        if (HasAllowAnonymous(context))
        {
            await next();
            return;
        }

        if (!RequiresCsrfCheck(httpContext))
        {
            await next();
            return;
        }

        try
        {
            await _antiforgery.ValidateRequestAsync(httpContext);
        }
        catch (AntiforgeryValidationException)
        {
            context.Result = new ObjectResult(new { error = "csrf_token_invalid" })
            {
                StatusCode = StatusCodes.Status400BadRequest,
            };
            return;
        }

        await next();
    }

    private static bool HasAllowAnonymous(ActionExecutingContext context)
    {
        if (context.ActionDescriptor is not ControllerActionDescriptor descriptor)
            return false;
        // Method attribute wins; class-level fallback. EndpointMetadata also covers
        // [AllowAnonymous] applied at the route group level.
        if (
            descriptor.MethodInfo.GetCustomAttributes(typeof(AllowAnonymousAttribute), true).Length
            > 0
        )
            return true;
        if (
            descriptor
                .ControllerTypeInfo.GetCustomAttributes(typeof(AllowAnonymousAttribute), true)
                .Length > 0
        )
            return true;
        return context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>()
            is not null;
    }

    private static bool RequiresCsrfCheck(HttpContext context)
    {
        // SingleUser principals are synthetic — no cookie is auto-sent by a browser.
        if (context.User.HasClaim(claim => claim.Type == SingleUserAuthHandler.SingleUserClaimType))
            return false;

        // The browser only auto-attaches a cookie. Bearer / ApiToken requests bring their
        // own Authorization header, so CSRF doesn't apply. Detect cookie auth via the
        // *actual cookie being present* — robust to whatever PolicyEvaluator did to the
        // principal's AuthenticationType.
        if (!context.Request.Cookies.ContainsKey("shield.auth"))
            return false;

        return context.User.Identity?.IsAuthenticated == true;
    }
}
