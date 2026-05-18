namespace Shield.Core.Abstractions;

// Provides the set of admin user IDs without coupling the Channels layer to ASP.NET Identity.
// Implemented in Shield.Api as AdminAudienceProvider (uses UserManager<ShieldUser>).
// Shield.Channels takes this abstraction so it carries no hard Identity dependency.
public interface IAdminAudienceProvider
{
    Task<IReadOnlyList<Guid>> GetAdminUserIdsAsync(CancellationToken ct = default);
}
