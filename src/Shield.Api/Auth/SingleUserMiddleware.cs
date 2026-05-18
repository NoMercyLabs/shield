namespace Shield.Api.Auth;

// Holds the well-known Guid for the seeded single-user account. The actual auto-sign-in
// lives in SingleUserAuthHandler so it slots into the standard auth pipeline rather than
// stomping context.User from a custom middleware.
public static class SingleUserMiddleware
{
    public const string SyntheticUserId = "00000000-0000-0000-0000-000000000001";
}

public static class ShieldRoles
{
    public const string Admin = "Admin";
    public const string Maintainer = "Maintainer";
    public const string Viewer = "Viewer";
}

// Named authorization policies. Always prefer `[Authorize(Policy = ShieldPolicies.Admin)]`
// over `[Authorize(Roles = ShieldRoles.Admin)]` so multi-scheme auth (SingleUser, ApiToken,
// JWT, cookie) keeps working — see Program.cs AddAuthorization for why.
public static class ShieldPolicies
{
    public const string Admin = "Admin";
    public const string MaintainerOrAdmin = "MaintainerOrAdmin";
}
