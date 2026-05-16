using System.Security.Claims;
using System.Text;
using System.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
    private readonly RoleManager<ShieldRole> _roleManager;
    private readonly IOptions<ShieldOptions> _shieldOptions;

    public AuthController(
        SignInManager<ShieldUser> signInManager,
        UserManager<ShieldUser> userManager,
        RoleManager<ShieldRole> roleManager,
        IOptions<ShieldOptions> shieldOptions
    )
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _roleManager = roleManager;
        _shieldOptions = shieldOptions;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request)
    {
        SignInResult result = await _signInManager.PasswordSignInAsync(
            request.Username,
            request.Password,
            isPersistent: true,
            lockoutOnFailure: true
        );

        if (result.RequiresTwoFactor)
        {
            if (string.IsNullOrWhiteSpace(request.TwoFactorCode))
                return Unauthorized(
                    new LoginResponse(
                        UserId: null,
                        Username: null,
                        Roles: Array.Empty<string>(),
                        RequiresTwoFactor: true
                    )
                );

            SignInResult twoFactor = await _signInManager.TwoFactorAuthenticatorSignInAsync(
                request.TwoFactorCode,
                isPersistent: true,
                rememberClient: false
            );
            if (!twoFactor.Succeeded)
                return Unauthorized(
                    new LoginResponse(
                        UserId: null,
                        Username: null,
                        Roles: Array.Empty<string>(),
                        RequiresTwoFactor: true,
                        Error: "Invalid 2FA code"
                    )
                );
        }
        else if (result.IsLockedOut)
        {
            return Unauthorized(
                new LoginResponse(
                    UserId: null,
                    Username: null,
                    Roles: Array.Empty<string>(),
                    Error: "Account locked"
                )
            );
        }
        else if (!result.Succeeded)
        {
            return Unauthorized(
                new LoginResponse(
                    UserId: null,
                    Username: null,
                    Roles: Array.Empty<string>(),
                    Error: "Invalid credentials"
                )
            );
        }

        ShieldUser user = (await _userManager.FindByNameAsync(request.Username))!;
        IList<string> roles = await _userManager.GetRolesAsync(user);
        return Ok(new LoginResponse(user.Id.ToString(), user.UserName, roles.ToList()));
    }

    // First-user-wins: when the users table is empty the first registration becomes Admin and
    // auto-creates the Admin role if missing. Subsequent registrations land in Viewer.
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult<RegisterResponse>> Register([FromBody] RegisterRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { error = "Username and password are required" });

        bool isFirstUser = !await _userManager.Users.AnyAsync();
        string assignedRole = isFirstUser ? ShieldRoles.Admin : ShieldRoles.Viewer;

        if (!await _roleManager.RoleExistsAsync(assignedRole))
        {
            IdentityResult roleCreate = await _roleManager.CreateAsync(new ShieldRole(assignedRole));
            if (!roleCreate.Succeeded)
                return Problem(
                    title: "Failed to create role",
                    detail: string.Join(", ", roleCreate.Errors.Select(error => error.Description))
                );
        }

        ShieldUser user = new()
        {
            UserName = request.Username,
            Email = request.Email,
            EmailConfirmed = string.IsNullOrWhiteSpace(request.Email),
            CreatedAt = DateTime.UtcNow,
        };

        IdentityResult create = await _userManager.CreateAsync(user, request.Password);
        if (!create.Succeeded)
            return BadRequest(
                new { error = string.Join(", ", create.Errors.Select(error => error.Description)) }
            );

        IdentityResult assign = await _userManager.AddToRoleAsync(user, assignedRole);
        if (!assign.Succeeded)
            return Problem(
                title: "Failed to assign role",
                detail: string.Join(", ", assign.Errors.Select(error => error.Description))
            );

        IList<string> roles = await _userManager.GetRolesAsync(user);
        return CreatedAtAction(
            nameof(Me),
            new RegisterResponse(user.Id.ToString(), user.UserName!, roles.ToList())
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
        ShieldUser? user = await _userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        IList<string> roles = await _userManager.GetRolesAsync(user);
        return Ok(
            new MeResponse(
                user.Id.ToString(),
                user.UserName,
                roles.ToList(),
                _shieldOptions.Value.SingleUser
            )
        );
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
    public async Task<ActionResult<TwoFactorVerifyResponse>> VerifyTwoFactor(
        [FromBody] TwoFactorVerifyRequest request
    )
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

        IEnumerable<string>? recovery = await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
        IReadOnlyList<string> codes = (recovery ?? Enumerable.Empty<string>()).ToList();
        return Ok(new TwoFactorVerifyResponse(codes));
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
