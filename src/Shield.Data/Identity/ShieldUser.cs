using Microsoft.AspNetCore.Identity;

namespace Shield.Data.Identity;

public class ShieldUser : IdentityUser<Guid>
{
    public DateTime CreatedAt { get; set; }
    public string? TotpSecret { get; set; }
}
