using Microsoft.AspNetCore.Identity;
using Shield.Api.Auth;
using Shield.Core.Abstractions;
using Shield.Data.Identity;

namespace Shield.Api.Services.Access;

// Implements the Channels-layer abstraction so InboxChannel can resolve admin user IDs
// without a hard dependency on ASP.NET Identity.
public sealed class AdminAudienceProvider : IAdminAudienceProvider
{
    private readonly UserManager<ShieldUser> _userManager;

    public AdminAudienceProvider(UserManager<ShieldUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<IReadOnlyList<Guid>> GetAdminUserIdsAsync(CancellationToken ct = default)
    {
        IList<ShieldUser> admins = await _userManager.GetUsersInRoleAsync(ShieldRoles.Admin);
        return admins.Select(user => user.Id).ToList();
    }
}
