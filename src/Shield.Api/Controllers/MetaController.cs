using System.Globalization;
using Shield.Core.Domain;

namespace Shield.Api.Controllers;

// Single source of truth for the wire-protocol enums the SPA needs to render. Server
// reflects each enum at startup and serves the lookup tables; the SPA hydrates once on
// boot. New enum values land in one place and propagate without a second copy in
// types/api.ts to forget.
[ApiController]
[Route("api/meta")]
[AllowAnonymous]
public sealed class MetaController : ControllerBase
{
    private static readonly Lazy<EnumCatalogResponse> Catalog = new(BuildCatalog);

    [HttpGet("enums")]
    public ActionResult<EnumCatalogResponse> GetEnums()
    {
        return Ok(Catalog.Value);
    }

    private static EnumCatalogResponse BuildCatalog()
    {
        Type[] enumTypes =
        [
            typeof(Ecosystem),
            typeof(SourceType),
            typeof(Severity),
            typeof(FindingState),
            typeof(Feed),
            typeof(ChannelType),
            typeof(AlertStatus),
            typeof(OAuthProvider),
            typeof(SourceAccessLevel),
            typeof(AutoFixMode),
            typeof(NotificationKind),
        ];

        Dictionary<string, Dictionary<string, int>> catalog = new();
        foreach (Type enumType in enumTypes)
        {
            Dictionary<string, int> entries = new();
            foreach (string name in Enum.GetNames(enumType))
            {
                object? value = Enum.Parse(enumType, name);
                entries[name] = Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
            catalog[enumType.Name] = entries;
        }
        return new(catalog);
    }
}

// Flat shape: { "Feed": { "Osv": 0, "Ghsa": 1, ... }, "SourceType": { ... }, ... }.
// SPA derives both directions from this — name→value AND value→name.
public sealed record EnumCatalogResponse(
    IReadOnlyDictionary<string, Dictionary<string, int>> Enums
);
