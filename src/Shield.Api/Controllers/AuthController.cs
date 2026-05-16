using System.Security.Claims;
using System.Text;
using System.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Shield.Api.Auth;
using Shield.Api.Contracts;
using Shield.Core.Options;
using Shield.Data.Identity;
using SignInResult = Microsoft.AspNetCore.Identity.SignInResult;

namespace Shield.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AuthController : ControllerBase
{
    private readonly SignInManager<ShieldUser> _signInManager;
    private readonly UserManager<ShieldUser> _userManager;
    private readonly IOptions<ShieldOptions> _shieldOptions;

    public AuthController(
        SignInManager<ShieldUser> signInManager,
        UserManager<ShieldUser> userManager,
        IOptions<ShieldOptions> shieldOptions
    )
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _shieldOptions = shieldOptions;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request)
    {
        if (_shieldOptions.Value.SingleUser)
            return Ok(new LoginResponse(Succeeded: true, RequiresTwoFactor: false, Error: null));

        SignInResult result = await _signInManager.PasswordSignInAsync(
            request.Username,
            request.Password,
            isPersistent: request.RememberMe,
            lockoutOnFailure: true
        );

        if (result.RequiresTwoFactor)
        {
            if (string.IsNullOrWhiteSpace(request.TwoFactorCode))
                return Ok(
                    new LoginResponse(Succeeded: false, RequiresTwoFactor: true, Error: null)
                );

            SignInResult twoFactor = await _signInManager.TwoFactorAuthenticatorSignInAsync(
                request.TwoFactorCode,
                isPersistent: request.RememberMe,
                rememberClient: false
            );
            if (twoFactor.Succeeded)
                return Ok(
                    new LoginResponse(Succeeded: true, RequiresTwoFactor: false, Error: null)
                );
            return Unauthorized(
                new LoginResponse(
                    Succeeded: false,
                    RequiresTwoFactor: true,
                    Error: "Invalid 2FA code"
                )
            );
        }

        if (result.Succeeded)
            return Ok(new LoginResponse(Succeeded: true, RequiresTwoFactor: false, Error: null));
        if (result.IsLockedOut)
            return Unauthorized(
                new LoginResponse(
                    Succeeded: false,
                    RequiresTwoFactor: false,
                    Error: "Account locked"
                )
            );
        return Unauthorized(
            new LoginResponse(
                Succeeded: false,
                RequiresTwoFactor: false,
                Error: "Invalid credentials"
            )
        );
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<MeResponse>> Me()
    {
        bool singleUser = _shieldOptions.Value.SingleUser;

        string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        string? username = User.Identity?.Name;
        List<string> roles = User.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToList();

        if (!singleUser && userId is not null && Guid.TryParse(userId, out Guid uid))
        {
            ShieldUser? user = await _userManager.FindByIdAsync(uid.ToString());
            if (user is not null)
            {
                username = user.UserName;
                IList<string> dbRoles = await _userManager.GetRolesAsync(user);
                roles = dbRoles.ToList();
            }
        }

        return Ok(new MeResponse(userId, username, roles, singleUser));
    }

    [HttpPost("2fa/enroll")]
    [Authorize]
    public async Task<ActionResult<TwoFactorEnrollResponse>> EnrollTwoFactor()
    {
        ShieldUser? user = await _userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        string? key = await _userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrWhiteSpace(key))
        {
            await _userManager.ResetAuthenticatorKeyAsync(user);
            key = await _userManager.GetAuthenticatorKeyAsync(user);
        }

        string sharedKey = key ?? string.Empty;
        string uri = BuildAuthenticatorUri(user.UserName ?? user.Email ?? "shield-user", sharedKey);
        return Ok(new TwoFactorEnrollResponse(sharedKey, uri));
    }

    [HttpPost("2fa/verify")]
    [Authorize]
    public async Task<IActionResult> VerifyTwoFactor([FromBody] TwoFactorVerifyRequest request)
    {
        ShieldUser? user = await _userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        bool valid = await _userManager.VerifyTwoFactorTokenAsync(
            user,
            _userManager.Options.Tokens.AuthenticatorTokenProvider,
            request.Code
        );
        if (!valid)
            return BadRequest(new { error = "Invalid code" });

        await _userManager.SetTwoFactorEnabledAsync(user, true);
        return NoContent();
    }

    private static string BuildAuthenticatorUri(string username, string sharedKey)
    {
        StringBuilder builder = new();
        builder.Append("otpauth://totp/Shield:");
        builder.Append(HttpUtility.UrlEncode(username));
        builder.Append("?secret=");
        builder.Append(sharedKey);
        builder.Append("&issuer=Shield&digits=6");
        return builder.ToString();
    }
}
