using FluentAssertions;
using Shield.Core.Domain;
using Shield.Core.Results;
using Xunit;

namespace Shield.Parsers.Swift.Tests;

public class PackageResolvedParserTests
{
    [Fact]
    public async Task ParseAsyncPackageResolvedV2ReturnsPinnedRepositories()
    {
        PackageResolvedParser parser = new();
        await using FileStream stream = File.OpenRead(Path.Combine("Fixtures", "Package.resolved"));

        ParseResult result = await parser.ParseAsync(
            stream,
            "Package.resolved",
            CancellationToken.None
        );

        result.Success.Should().BeTrue(result.Error);
        result.Items.Should().HaveCount(2);
        result.Items.Should().OnlyContain(item => item.Ecosystem == Ecosystem.SwiftPM);
        result
            .Items.Should()
            .ContainSingle(item =>
                item.Name == "https://github.com/Alamofire/Alamofire.git" && item.Version == "5.8.1"
            );
        result
            .Items.Should()
            .ContainSingle(item =>
                item.Name == "https://github.com/apple/swift-log.git" && item.Version == "1.5.3"
            );
    }
}
