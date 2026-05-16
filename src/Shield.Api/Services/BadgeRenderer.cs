using System.Globalization;
using System.Text;

namespace Shield.Api.Services;

// shields.io "flat" style SVG, hand-rolled so we don't add a dependency for a
// single endpoint. Widths are estimated from character count (6.5px per char in
// DejaVu Sans 11px) — close enough that the badge looks correct in every README
// renderer we care about. Color follows shields.io conventions:
//   critical -> red, high -> orange, medium -> yellow, low -> blue,
//   clean    -> green, not watched -> grey.
public interface IBadgeRenderer
{
    string Render(int critical, int high, int medium, int low);
    string RenderNotWatched();
}

public sealed class BadgeRenderer : IBadgeRenderer
{
    public const string Label = "shield";
    private const int Height = 20;
    private const int FontSize = 11;
    private const int HorizontalPadding = 6;
    private const double CharWidth = 6.5;

    private const string ColorGreen = "#4c1";
    private const string ColorBlue = "#007ec6";
    private const string ColorYellow = "#dfb317";
    private const string ColorOrange = "#fe7d37";
    private const string ColorRed = "#e05d44";
    private const string ColorGrey = "#9f9f9f";
    private const string ColorLabel = "#555";

    public string Render(int critical, int high, int medium, int low)
    {
        int total = critical + high + medium + low;
        string value = total == 0 ? "no findings" : $"{critical}C {high}H {medium}M {low}L";
        string color = PickColor(critical, high, medium, low);
        return BuildSvg(Label, value, color);
    }

    public string RenderNotWatched() => BuildSvg(Label, "not watched", ColorGrey);

    private static string PickColor(int critical, int high, int medium, int low)
    {
        if (critical > 0)
            return ColorRed;
        if (high > 0)
            return ColorOrange;
        if (medium > 0)
            return ColorYellow;
        if (low > 0)
            return ColorBlue;
        return ColorGreen;
    }

    private static string BuildSvg(string label, string value, string valueColor)
    {
        int labelWidth = MeasureWidth(label);
        int valueWidth = MeasureWidth(value);
        int totalWidth = labelWidth + valueWidth;
        int labelTextX = labelWidth * 5;
        int valueTextX = labelWidth * 10 + valueWidth * 5;

        // Anti-aliased text is drawn twice — a dark shadow at y=15 plus the white
        // foreground at y=14 — so it stays readable on light and dark backgrounds.
        StringBuilder sb = new();
        sb.Append(
            CultureInfo.InvariantCulture,
            $"""
            <svg xmlns="http://www.w3.org/2000/svg" width="{totalWidth}" height="{Height}" role="img" aria-label="{Escape(
                label
            )}: {Escape(value)}">
            <title>{Escape(label)}: {Escape(value)}</title>
            <linearGradient id="s" x2="0" y2="100%"><stop offset="0" stop-color="#bbb" stop-opacity=".1"/><stop offset="1" stop-opacity=".1"/></linearGradient>
            <clipPath id="r"><rect width="{totalWidth}" height="{Height}" rx="3" fill="#fff"/></clipPath>
            <g clip-path="url(#r)">
            <rect width="{labelWidth}" height="{Height}" fill="{ColorLabel}"/>
            <rect x="{labelWidth}" width="{valueWidth}" height="{Height}" fill="{valueColor}"/>
            <rect width="{totalWidth}" height="{Height}" fill="url(#s)"/>
            </g>
            <g fill="#fff" text-anchor="middle" font-family="Verdana,Geneva,DejaVu Sans,sans-serif" text-rendering="geometricPrecision" font-size="{FontSize
                * 10}" transform="scale(.1)">
            <text aria-hidden="true" x="{labelTextX}" y="150" fill="#010101" fill-opacity=".3">{Escape(
                label
            )}</text>
            <text x="{labelTextX}" y="140">{Escape(label)}</text>
            <text aria-hidden="true" x="{valueTextX}" y="150" fill="#010101" fill-opacity=".3">{Escape(
                value
            )}</text>
            <text x="{valueTextX}" y="140">{Escape(value)}</text>
            </g>
            </svg>
            """
        );
        return sb.ToString();
    }

    private static int MeasureWidth(string text) =>
        (int)Math.Ceiling(text.Length * CharWidth) + HorizontalPadding * 2;

    private static string Escape(string raw) =>
        raw.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
}
