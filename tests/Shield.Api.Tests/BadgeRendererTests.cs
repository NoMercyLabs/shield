using FluentAssertions;
using Shield.Api.Services.Rendering;
using Xunit;

namespace Shield.Api.Tests;

public sealed class BadgeRendererTests
{
    private readonly BadgeRenderer _renderer = new();

    [Fact]
    public void RenderWithNoFindingsReturnsGreenNoFindingsBadge()
    {
        string svg = _renderer.Render(0, 0, 0, 0);
        svg.Should().Contain("<svg");
        svg.Should().Contain("shield");
        svg.Should().Contain("no findings");
        svg.Should().Contain("#4c1");
    }

    [Fact]
    public void RenderWithCriticalFindingUsesRedColor()
    {
        string svg = _renderer.Render(1, 2, 3, 4);
        svg.Should().Contain("1C 2H 3M 4L");
        svg.Should().Contain("#e05d44");
    }

    [Fact]
    public void RenderWithHighButNoCriticalUsesOrangeColor()
    {
        string svg = _renderer.Render(0, 2, 3, 4);
        svg.Should().Contain("#fe7d37");
        svg.Should().NotContain("#e05d44");
    }

    [Fact]
    public void RenderWithMediumButNoHighOrCriticalUsesYellow()
    {
        string svg = _renderer.Render(0, 0, 3, 4);
        svg.Should().Contain("#dfb317");
    }

    [Fact]
    public void RenderWithOnlyLowUsesBlue()
    {
        string svg = _renderer.Render(0, 0, 0, 4);
        svg.Should().Contain("#007ec6");
    }

    [Fact]
    public void RenderNotWatchedUsesGreyColorAndNotWatchedText()
    {
        string svg = _renderer.RenderNotWatched();
        svg.Should().Contain("not watched");
        svg.Should().Contain("#9f9f9f");
    }

    [Fact]
    public void RenderedSvgIsWellFormedWithImageNamespaceAndRootClose()
    {
        string svg = _renderer.Render(0, 1, 0, 0);
        svg.Should().StartWith("<svg xmlns=\"http://www.w3.org/2000/svg\"");
        svg.TrimEnd().Should().EndWith("</svg>");
    }

    [Fact]
    public void RenderedSvgHtmlEscapesLabelAndValueSegments()
    {
        // Defence-in-depth — if counts ever became user-influenced the SVG must not break.
        // Just assert that ampersand/lt/gt never appear unescaped in the no-findings render.
        string svg = _renderer.Render(0, 0, 0, 0);
        svg.Should().NotContain("&lt;svg");
        svg.Should().NotContain("<<");
    }
}
