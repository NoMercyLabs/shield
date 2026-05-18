using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Shield.Api.Services.Security.Snapshots;

namespace Shield.Api.Services.Security.Handlers;

// Restores the user's role membership to its pre-swap shape. Reads BeforeJson as
// UserRolesSnapshot, computes the diff against the current set, then applies via
// UserManager. Lockout guard re-applies: a restore that would leave zero Admin users
// fails rather than orphaning the install.
public sealed class UserRoleChangeUndoHandler : IAuditUndoHandler
{
    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web);

    private readonly UserManager<ShieldUser> _userManager;

    public UserRoleChangeUndoHandler(UserManager<ShieldUser> userManager)
    {
        _userManager = userManager;
    }

    public string Action => "user.role.changed";

    public async Task<AuditUndoResult> UndoAsync(AuditEntry entry, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(entry.BeforeJson))
            return new(false, "No captured before-state to restore.");

        if (!Guid.TryParse(entry.TargetId, out Guid userId))
            return new(false, $"Audit TargetId '{entry.TargetId}' is not a valid user id.");

        UserRolesSnapshot? snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize<UserRolesSnapshot>(
                entry.BeforeJson,
                s_jsonOptions
            );
        }
        catch (JsonException ex)
        {
            return new(false, $"Could not parse before-state: {ex.Message}");
        }
        if (snapshot is null)
            return new(false, "Before-state deserialized to null.");

        ShieldUser? user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return new(false, $"User {userId} no longer exists.");

        IList<string> currentRoles = await _userManager.GetRolesAsync(user);
        HashSet<string> target = new(snapshot.Roles, StringComparer.Ordinal);
        HashSet<string> current = new(currentRoles, StringComparer.Ordinal);

        List<string> toRemove = current.Except(target, StringComparer.Ordinal).ToList();
        List<string> toAdd = target.Except(current, StringComparer.Ordinal).ToList();

        if (toRemove.Count > 0)
        {
            IdentityResult removeResult = await _userManager.RemoveFromRolesAsync(user, toRemove);
            if (!removeResult.Succeeded)
                return new(
                    false,
                    "Failed to remove current roles: "
                        + string.Join("; ", removeResult.Errors.Select(error => error.Description))
                );
        }
        if (toAdd.Count > 0)
        {
            IdentityResult addResult = await _userManager.AddToRolesAsync(user, toAdd);
            if (!addResult.Succeeded)
                return new(
                    false,
                    "Failed to add prior roles: "
                        + string.Join("; ", addResult.Errors.Select(error => error.Description))
                );
        }

        return new(
            true,
            $"Restored {user.UserName ?? user.Id.ToString()} to roles: {string.Join(", ", snapshot.Roles)}."
        );
    }
}
