using Microsoft.Extensions.DependencyInjection;
using Shield.Core.Abstractions;

namespace Shield.Parsers.Rust.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRustParser(this IServiceCollection services)
    {
        services.AddKeyedSingleton<IParser, RustDependencyParser>("rust");
        return services;
    }
}
