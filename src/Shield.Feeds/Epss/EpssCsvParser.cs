using System.Globalization;
using System.IO.Compression;
using Shield.Core.Abstractions;

namespace Shield.Feeds.Epss;

/// Stream-parser for the daily EPSS CSV (gzipped). Yields EpssEntry rows without ever
/// materialising the full ~250k-row file in memory. Caller batches via the EpssFeedSync loop.
public static class EpssCsvParser
{
    /// Skips header lines (the first non-blank line that starts with "#" is metadata, and the
    /// first data line is the column header "cve,epss,percentile").
    public static async IAsyncEnumerable<EpssEntry> ReadAsync(
        Stream gzipStream,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default
    )
    {
        await using GZipStream decompress = new(gzipStream, CompressionMode.Decompress);
        using StreamReader reader = new(decompress);

        bool sawColumnHeader = false;
        string? line;
        while ((line = await reader.ReadLineAsync(ct)) is not null)
        {
            ct.ThrowIfCancellationRequested();

            if (line.Length == 0)
                continue;
            if (line[0] == '#')
                continue;

            if (!sawColumnHeader)
            {
                sawColumnHeader = true;
                // Column header is "cve,epss,percentile" — skip it. If the file already starts
                // with data (no header), we'd lose one row; FIRST.org always emits the header.
                if (
                    line.StartsWith("cve", StringComparison.OrdinalIgnoreCase)
                    && line.Contains("epss", StringComparison.OrdinalIgnoreCase)
                )
                    continue;
            }

            string[] parts = line.Split(',', 3);
            if (parts.Length < 3)
                continue;

            string cveId = parts[0].Trim();
            if (cveId.Length == 0)
                continue;

            if (
                !double.TryParse(
                    parts[1],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double score
                )
                || !double.TryParse(
                    parts[2],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double percentile
                )
            )
                continue;

            yield return new EpssEntry(cveId, score, percentile);
        }
    }
}
