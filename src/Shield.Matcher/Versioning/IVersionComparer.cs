using Shield.Core.Domain;

namespace Shield.Matcher.Versioning;

public interface IVersionComparer
{
    Ecosystem Ecosystem { get; }
    bool Satisfies(string version, VersionRange range);
}
