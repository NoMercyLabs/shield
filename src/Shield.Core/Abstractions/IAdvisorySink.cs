using Shield.Core.Domain;

namespace Shield.Core.Abstractions;

public interface IAdvisorySink
{
    ValueTask UpsertAsync(IReadOnlyList<Advisory> advisories, CancellationToken ct);
}

public sealed class InMemoryAdvisorySink : IAdvisorySink
{
    private readonly List<Advisory> _advisories = [];

    public IReadOnlyList<Advisory> Advisories => _advisories;

    public ValueTask UpsertAsync(IReadOnlyList<Advisory> advisories, CancellationToken ct)
    {
        _advisories.AddRange(advisories);
        return ValueTask.CompletedTask;
    }
}
