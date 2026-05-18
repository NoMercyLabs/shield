using Microsoft.Extensions.DependencyInjection;
using Shield.Parsers.Composer.Extensions;
using Shield.Parsers.Dart.Extensions;
using Shield.Parsers.Elixir.Extensions;
using Shield.Parsers.Go.Extensions;
using Shield.Parsers.Gradle.Extensions;
using Shield.Parsers.Maven.Extensions;
using Shield.Parsers.Npm.Extensions;
using Shield.Parsers.Nuget.Extensions;
using Shield.Parsers.Python.Extensions;
using Shield.Parsers.Ruby.Extensions;
using Shield.Parsers.Rust.Extensions;
using Shield.Parsers.Swift.Extensions;
using Shield.Parsers.Vcpkg.Extensions;

namespace Shield.Parsers.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddShieldParsers(this IServiceCollection services)
    {
        services.AddComposerParser();
        services.AddDartParser();
        services.AddElixirParser();
        services.AddGoParser();
        services.AddGradleParser();
        services.AddMavenParser();
        services.AddNpmParser();
        services.AddNugetParser();
        services.AddPythonParser();
        services.AddRubyParser();
        services.AddRustParser();
        services.AddSwiftParser();
        services.AddVcpkgParser();
        return services;
    }
}
