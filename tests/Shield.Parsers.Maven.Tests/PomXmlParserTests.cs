using FluentAssertions;
using Shield.Core.Domain;
using Shield.Core.Results;
using Xunit;

namespace Shield.Parsers.Maven.Tests;

public class PomXmlParserTests
{
    [Fact]
    public async Task ParseAsync_PomXml_ReturnsDirectDepsAndSkipsDependencyManagement()
    {
        PomXmlParser parser = new();
        await using FileStream stream = File.OpenRead(Path.Combine("Fixtures", "pom.xml"));

        ParseResult result = await parser.ParseAsync(stream, "pom.xml", CancellationToken.None);

        result.Success.Should().BeTrue(result.Error);
        // 3 direct dependencies; junit-bom in dependencyManagement is excluded.
        result.Items.Should().HaveCount(3);
        result.Items.Should().OnlyContain(item => item.Ecosystem == Ecosystem.Maven);
        result
            .Items.Should()
            .ContainSingle(item =>
                item.Name == "org.springframework:spring-core" && item.Version == "6.1.4"
            );
        result
            .Items.Should()
            .ContainSingle(item =>
                item.Name == "com.google.guava:guava" && item.Version == "33.0.0-jre"
            );
        result.Items.Should().NotContain(item => item.Name == "org.junit:junit-bom");
        result.Diagnostics.Should().ContainKey("transitive");
    }
}
