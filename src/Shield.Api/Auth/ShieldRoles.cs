namespace Shield.Api.Auth;

public static class ShieldRoles
{
    public const string Admin = "Admin";
    public const string Maintainer = "Maintainer";
    public const string Viewer = "Viewer";
}

// Named authorization policies. Always prefer `[Authorize(Policy = ShieldPolicies.Admin)]`
// over `[Authorize(Roles = ShieldRoles.Admin)]` so multi-scheme auth (ApiToken, JWT, cookie)
// keeps working — see Program.cs AddAuthorization for why.
public static class ShieldPolicies
{
    public const string Admin = "Admin";
    public const string MaintainerOrAdmin = "MaintainerOrAdmin";
}
