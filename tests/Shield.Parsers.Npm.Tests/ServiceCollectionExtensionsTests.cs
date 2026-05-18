using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Shield.Core.Abstractions;
using Shield.Parsers.Npm.Extensions;
using Xunit;

namespace Shield.Parsers.Npm.Tests;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddNpmParserRegistersKeyedSingleton()
    {
        ServiceCollection services = new();
        services.AddNpmParser();

        ServiceProvider provider = services.BuildServiceProvider();
        IParser resolved = provider.GetRequiredKeyedService<IParser>("npm");

        resolved.Should().BeOfType<NpmLockParser>();
    }
}
