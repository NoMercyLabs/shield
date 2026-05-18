namespace Shield.Core.Abstractions;

public sealed record EpssEntry(string CveId, double Score, double Percentile);

public interface IEpssAdvisoryEnricher
{
    // Streaming enrichment — caller pumps batches; returns rows updated for each batch.
    ValueTask<int> ApplyBatchAsync(IReadOnlyList<EpssEntry> batch, CancellationToken ct);
}
