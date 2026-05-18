using System.Reflection;

namespace Shield.Core.Http;

// Canonical outbound User-Agent. GitHub, npm registry, OSV, EPSS, Discord all log UA on
// rate-limit / abuse reports — one string makes support triangulation trivial.
public static class ShieldUserAgent
{
    public static string Header { get; } = BuildHeader();

    private static string BuildHeader()
    {
        Assembly assembly = Assembly.GetEntryAssembly() ?? typeof(ShieldUserAgent).Assembly;
        string version =
            assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "0.0.0";
        int plus = version.IndexOf('+');
        if (plus > 0)
            version = version[..plus];
        return $"Shield/{version} (+https://github.com/nomercylabs/shield)";
    }
}
