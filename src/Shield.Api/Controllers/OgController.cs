using Microsoft.Net.Http.Headers;
using Shield.Api.Services;

namespace Shield.Api.Controllers;

// Public, unauthenticated endpoint that serves the Open Graph preview image. Crawlers
// (Discordbot, Slackbot, Twitterbot, …) call this when a Shield URL is unfurled. Privacy:
// nothing on the canvas may leak data behind auth — see OgImageRenderer for the allow-list.
[ApiController]
[Route("api/og")]
[AllowAnonymous]
public sealed class OgController : ControllerBase
{
    private const string PngContentType = "image/png";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

    private readonly IOgImageRenderer _renderer;
    private readonly ShieldDbContext _db;
    private readonly IConfiguration _configuration;

    public OgController(IOgImageRenderer renderer, ShieldDbContext db, IConfiguration configuration)
    {
        _renderer = renderer;
        _db = db;
        _configuration = configuration;
    }

    [HttpGet("default.png")]
    public IActionResult Default()
    {
        byte[] png = _renderer.Render(OgImageVariant.Default, new(null));
        return PngResult(png);
    }

    [HttpGet("instance.png")]
    public async Task<IActionResult> Instance(CancellationToken ct)
    {
        // Opt-in disclosure: the instance source count is a small but non-zero signal. We
        // hide it unless the operator explicitly turns it on.
        bool showCount = _configuration.GetValue("Shield:Og:ShowInstanceCount", false);
        int? sourceCount = showCount ? await _db.Sources.CountAsync(ct) : null;

        OgImageVariant variant = sourceCount is not null
            ? OgImageVariant.Instance
            : OgImageVariant.Default;
        byte[] png = _renderer.Render(variant, new(sourceCount));
        return PngResult(png);
    }

    [HttpGet("icon-{size:int}.png")]
    public IActionResult Icon(int size)
    {
        // Whitelist sizes the SPA + manifest reference. Arbitrary sizes from random callers
        // would let a bot pin the renderer's CPU; the controller stays predictable.
        int[] allowed = [64, 192, 256, 512];
        if (Array.IndexOf(allowed, size) < 0)
            return NotFound();
        byte[] png = _renderer.RenderIcon(size);
        return PngResult(png);
    }

    private FileContentResult PngResult(byte[] png)
    {
        Response.Headers[HeaderNames.CacheControl] =
            $"public, max-age={(int)CacheTtl.TotalSeconds}";
        return File(png, PngContentType);
    }
}
