using Microsoft.AspNetCore.Identity;

namespace Shield.Data.Identity;

public class ShieldRole : IdentityRole<Guid>
{
    public ShieldRole() { }

    public ShieldRole(string roleName) : base(roleName) { }
}
