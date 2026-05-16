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
