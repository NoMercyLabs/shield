using System.Reflection;
using SkiaSharp;

namespace Shield.Api.Services.Rendering;

// Renders the 1200×630 PNG that Discord/Slack/Twitter/LinkedIn pull when a Shield URL is
// unfurled. Pure code (no template PNG) so variants stay one switch away. Font is shipped
// embedded so a fontconfig-less container renders identically to a Windows dev box.
//
// Privacy: nothing on this canvas may leak data behind auth. Only brand text + (opt-in)
// the public Sources count.
public interface IOgImageRenderer
{
    byte[] Render(OgImageVariant variant, OgImageContext context);
    byte[] RenderIcon(int size);
}

public enum OgImageVariant
{
    Default,
    Instance,
}

public readonly record struct OgImageContext(int? InstanceSourceCount);

public sealed class OgImageRenderer : IOgImageRenderer
{
    private const int Width = 1200;
    private const int Height = 630;

    private static readonly SKColor BackgroundTop = new(0x0f, 0x17, 0x2a);
    private static readonly SKColor BackgroundBottom = new(0x08, 0x0d, 0x1a);
    private static readonly SKColor ShieldFill = new(0x0e, 0xa5, 0xe9);
    private static readonly SKColor ShieldGlow = new(0x0e, 0xa5, 0xe9, 0x40);
    private static readonly SKColor TextPrimary = SKColors.White;
    private static readonly SKColor TextSecondary = new(0xcb, 0xd5, 0xe1);
    private static readonly SKColor TextMuted = new(0x64, 0x74, 0x8b);

    private readonly Lazy<SKTypeface> _typeface;

    public OgImageRenderer()
    {
        _typeface = new(LoadEmbeddedTypeface);
    }

    public byte[] Render(OgImageVariant variant, OgImageContext context)
    {
        using SKBitmap bitmap = new(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using SKCanvas canvas = new(bitmap);

        DrawBackground(canvas);
        DrawShieldMark(canvas);
        DrawWordmarkAndTagline(canvas);
        DrawBottomStrip(canvas, variant, context);

        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, quality: 100);
        return data.ToArray();
    }

    // App / apple-touch icon — square shield mark on the brand-slate background. Used for
    // favicon-png + PWA manifest entries via the /api/og/icon-{size}.png endpoint.
    public byte[] RenderIcon(int size)
    {
        if (size < 16 || size > 1024)
            throw new ArgumentOutOfRangeException(nameof(size), "Icon size must be 16..1024.");

        using SKBitmap bitmap = new(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using SKCanvas canvas = new(bitmap);

        // Background: rounded-square brand slate so the icon reads on light + dark home screens.
        canvas.Clear(SKColors.Transparent);
        using SKPaint backgroundPaint = new()
        {
            Color = BackgroundTop,
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };
        float radius = size * 0.22f;
        canvas.DrawRoundRect(0, 0, size, size, radius, radius, backgroundPaint);

        // Shield mark — Lucide path mapped from a 24×24 viewBox into the icon bounds with
        // 12% horizontal padding so the silhouette doesn't kiss the rounded-square edges.
        float pad = size * 0.18f;
        float drawSize = size - pad * 2f;
        float scale = drawSize / 24f;
        float offsetX = pad;
        float offsetY = pad;

        using SKPath shield = new();
        shield.MoveTo(offsetX + 12 * scale, offsetY + 22 * scale);
        shield.CubicTo(
            offsetX + 12 * scale,
            offsetY + 22 * scale,
            offsetX + 20 * scale,
            offsetY + 18 * scale,
            offsetX + 20 * scale,
            offsetY + 12 * scale
        );
        shield.LineTo(offsetX + 20 * scale, offsetY + 5 * scale);
        shield.LineTo(offsetX + 12 * scale, offsetY + 2 * scale);
        shield.LineTo(offsetX + 4 * scale, offsetY + 5 * scale);
        shield.LineTo(offsetX + 4 * scale, offsetY + 12 * scale);
        shield.CubicTo(
            offsetX + 4 * scale,
            offsetY + 18 * scale,
            offsetX + 12 * scale,
            offsetY + 22 * scale,
            offsetX + 12 * scale,
            offsetY + 22 * scale
        );
        shield.Close();

        using SKPaint shieldPaint = new()
        {
            Color = ShieldFill,
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };
        canvas.DrawPath(shield, shieldPaint);

        using SKPath check = new();
        check.MoveTo(offsetX + 9 * scale, offsetY + 12 * scale);
        check.LineTo(offsetX + 11 * scale, offsetY + 14 * scale);
        check.LineTo(offsetX + 15 * scale, offsetY + 10 * scale);
        using SKPaint checkPaint = new()
        {
            Color = SKColors.White,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = scale * 1.6f,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
        };
        canvas.DrawPath(check, checkPaint);

        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, quality: 100);
        return data.ToArray();
    }

    private static void DrawBackground(SKCanvas canvas)
    {
        using SKPaint background = new() { IsAntialias = false };
        using SKShader gradient = SKShader.CreateLinearGradient(
            new(0, 0),
            new(0, Height),
            [BackgroundTop, BackgroundBottom],
            [0f, 1f],
            SKShaderTileMode.Clamp
        );
        background.Shader = gradient;
        canvas.DrawRect(0, 0, Width, Height, background);

        // Subtle radial glow behind the shield mark so the dark canvas reads as branded
        // rather than flat. Centered on the shield, low opacity, generous radius.
        using SKPaint glow = new() { IsAntialias = true };
        using SKShader glowShader = SKShader.CreateRadialGradient(
            new(Width / 2f, Height / 2f - 60),
            420,
            [ShieldGlow, new(0x0e, 0xa5, 0xe9, 0x00)],
            [0f, 1f],
            SKShaderTileMode.Clamp
        );
        glow.Shader = glowShader;
        canvas.DrawRect(0, 0, Width, Height, glow);
    }

    private static void DrawShieldMark(SKCanvas canvas)
    {
        // Lucide-style shield path normalised to a 24×24 viewBox, then scaled + translated
        // so the centre of mass lands at (Width/2, 230). 240px tall — leaves room for the
        // wordmark + tagline below.
        const float scale = 10f;
        const float offsetX = Width / 2f - 12 * scale;
        const float offsetY = 120f;

        using SKPath shield = new();
        shield.MoveTo(offsetX + 12 * scale, offsetY + 22 * scale);
        // path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z" — quadratic shortcut
        // expressed via cubic for SkiaSharp (no built-in 's' command shortcut).
        shield.CubicTo(
            offsetX + 12 * scale,
            offsetY + 22 * scale,
            offsetX + 20 * scale,
            offsetY + 18 * scale,
            offsetX + 20 * scale,
            offsetY + 12 * scale
        );
        shield.LineTo(offsetX + 20 * scale, offsetY + 5 * scale);
        shield.LineTo(offsetX + 12 * scale, offsetY + 2 * scale);
        shield.LineTo(offsetX + 4 * scale, offsetY + 5 * scale);
        shield.LineTo(offsetX + 4 * scale, offsetY + 12 * scale);
        shield.CubicTo(
            offsetX + 4 * scale,
            offsetY + 18 * scale,
            offsetX + 12 * scale,
            offsetY + 22 * scale,
            offsetX + 12 * scale,
            offsetY + 22 * scale
        );
        shield.Close();

        using SKPaint fill = new()
        {
            Color = ShieldFill,
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };
        canvas.DrawPath(shield, fill);

        // Inner checkmark — same Lucide path, white.
        using SKPath check = new();
        check.MoveTo(offsetX + 9 * scale, offsetY + 12 * scale);
        check.LineTo(offsetX + 11 * scale, offsetY + 14 * scale);
        check.LineTo(offsetX + 15 * scale, offsetY + 10 * scale);

        using SKPaint checkPaint = new()
        {
            Color = SKColors.White,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = scale * 1.6f,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
        };
        canvas.DrawPath(check, checkPaint);
    }

    private void DrawWordmarkAndTagline(SKCanvas canvas)
    {
        SKTypeface typeface = _typeface.Value;

        using SKFont wordmarkFont = new(typeface, size: 96);
        wordmarkFont.Embolden = true;
        using SKPaint wordmarkPaint = new() { Color = TextPrimary, IsAntialias = true };
        DrawCenteredText(
            canvas,
            "Shield",
            wordmarkFont,
            wordmarkPaint,
            centerX: Width / 2f,
            baselineY: 460
        );

        using SKFont taglineFont = new(typeface, size: 30);
        using SKPaint taglinePaint = new() { Color = TextSecondary, IsAntialias = true };
        DrawCenteredText(
            canvas,
            "Self-hosted dependency vulnerability watcher",
            taglineFont,
            taglinePaint,
            centerX: Width / 2f,
            baselineY: 510
        );
    }

    private void DrawBottomStrip(SKCanvas canvas, OgImageVariant variant, OgImageContext context)
    {
        if (
            variant != OgImageVariant.Instance
            || context.InstanceSourceCount is not int sourceCount
        )
            return;

        SKTypeface typeface = _typeface.Value;
        using SKFont labelFont = new(typeface, size: 24);
        using SKPaint labelPaint = new() { Color = TextMuted, IsAntialias = true };

        string label =
            sourceCount == 1 ? "Monitoring 1 source" : $"Monitoring {sourceCount} sources";
        DrawCenteredText(canvas, label, labelFont, labelPaint, centerX: Width / 2f, baselineY: 570);
    }

    private static void DrawCenteredText(
        SKCanvas canvas,
        string text,
        SKFont font,
        SKPaint paint,
        float centerX,
        float baselineY
    )
    {
        float width = font.MeasureText(text);
        canvas.DrawText(text, centerX - width / 2f, baselineY, font, paint);
    }

    private static SKTypeface LoadEmbeddedTypeface()
    {
        Assembly assembly = typeof(OgImageRenderer).Assembly;
        // Default ResourceManager namespace + folder for EmbeddedResource items rooted at
        // Services/Assets — MSBuild maps directory separators to dots.
        const string resourceName = "Shield.Api.Services.Assets.InterVariable.ttf";
        using Stream? stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            throw new InvalidOperationException(
                $"Embedded font resource '{resourceName}' not found. Available resources: "
                    + string.Join(", ", assembly.GetManifestResourceNames())
            );
        }
        SKTypeface? typeface = SKTypeface.FromStream(stream);
        if (typeface is null)
            throw new InvalidOperationException("Failed to load embedded Inter typeface.");
        return typeface;
    }
}
