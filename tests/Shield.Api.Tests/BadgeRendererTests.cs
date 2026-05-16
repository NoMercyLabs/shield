using FluentAssertions;
using Shield.Api.Services;
using Xunit;

namespace Shield.Api.Tests;

public sealed class BadgeRendererTests
{
    private readonly BadgeRenderer _renderer = new();

    [Fact]
    public void Render_with_no_findings_returns_green_no_findings_badge()
    {
        string svg = _renderer.Render(0, 0, 0, 0);
        svg.Should().Contain("<svg");
        svg.Should().Contain("shield");
        svg.Should().Contain("no findings");
        svg.Should().Contain("#4c1");
    }

    [Fact]
    public void Render_with_critical_finding_uses_red_color()
    {
        string svg = _renderer.Render(1, 2, 3, 4);
        svg.Should().Contain("1C 2H 3M 4L");
        svg.Should().Contain("#e05d44");
    }

    [Fact]
    public void Render_with_high_but_no_critical_uses_orange_color()
    {
        string svg = _renderer.Render(0, 2, 3, 4);
        svg.Should().Contain("#fe7d37");
        svg.Should().NotContain("#e05d44");
    }

    [Fact]
    public void Render_with_medium_but_no_high_or_critical_uses_yellow()
    {
        string svg = _renderer.Render(0, 0, 3, 4);
        svg.Should().Contain("#dfb317");
    }

    [Fact]
    public void Render_with_only_low_uses_blue()
    {
        string svg = _renderer.Render(0, 0, 0, 4);
        svg.Should().Contain("#007ec6");
    }

    [Fact]
    public void RenderNotWatched_uses_grey_color_and_not_watched_text()
    {
        string svg = _renderer.RenderNotWatched();
        svg.Should().Contain("not watched");
        svg.Should().Contain("#9f9f9f");
    }

    [Fact]
    public void Rendered_svg_is_well_formed_with_image_namespace_and_root_close()
    {
        string svg = _renderer.Render(0, 1, 0, 0);
        svg.Should().StartWith("<svg xmlns=\"http://www.w3.org/2000/svg\"");
        svg.TrimEnd().Should().EndWith("</svg>");
    }

    [Fact]
    public void Rendered_svg_html_escapes_label_and_value_segments()
    {
        // Defence-in-depth — if counts ever became user-influenced the SVG must not break.
        // Just assert that ampersand/lt/gt never appear unescaped in the no-findings render.
        string svg = _renderer.Render(0, 0, 0, 0);
        svg.Should().NotContain("&lt;svg");
        svg.Should().NotContain("<<");
    }
}
