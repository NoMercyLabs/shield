using Shield.Core.Domain;

namespace Shield.Feeds.Osv.Models;

public sealed record OsvQuery(Ecosystem Ecosystem, string PackageName, string Version);
