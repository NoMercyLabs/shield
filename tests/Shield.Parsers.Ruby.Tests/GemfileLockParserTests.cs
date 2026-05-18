using FluentAssertions;
using Shield.Core.Domain;
using Shield.Core.Results;
using Xunit;

namespace Shield.Parsers.Ruby.Tests;

public class GemfileLockParserTests
{
    [Fact]
    public async Task ParseAsyncGemfileLockReturnsGemsAndMarksDirect()
    {
        GemfileLockParser parser = new();
        await using FileStream stream = File.OpenRead(Path.Combine("Fixtures", "Gemfile.lock"));

        ParseResult result = await parser.ParseAsync(
            stream,
            "Gemfile.lock",
            CancellationToken.None
        );

        result.Success.Should().BeTrue(result.Error);
        result.Items.Should().HaveCount(5);
        result.Items.Should().OnlyContain(item => item.Ecosystem == Ecosystem.RubyGems);
        result
            .Items.Should()
            .ContainSingle(item =>
                item.Name == "rails" && item.Version == "7.1.3" && item.IsDirect
            );
        result
            .Items.Should()
            .ContainSingle(item =>
                item.Name == "rspec" && item.Version == "3.12.0" && item.IsDirect
            );
        result
            .Items.Should()
            .ContainSingle(item =>
                item.Name == "actionpack" && item.Version == "7.1.3" && !item.IsDirect
            );
    }
}
