using Microsoft.Extensions.DependencyInjection;
using Shield.Core.Abstractions;

namespace Shield.Parsers.Python.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPythonParser(this IServiceCollection services)
    {
        services.AddKeyedSingleton<IParser, PythonDependencyParser>("python");
        return services;
    }
}
