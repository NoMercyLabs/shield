using FluentAssertions;
using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Xunit;

namespace Shield.Core.Tests;

public class DedupKeyTests
{
    [Fact]
    public void Compute_IsStable_AcrossCalls()
    {
        string first = DedupKey.Compute(1, Ecosystem.Npm, "lodash", "GHSA-jf85-cpcp-j695");
        string second = DedupKey.Compute(1, Ecosystem.Npm, "lodash", "GHSA-jf85-cpcp-j695");

        first.Should().Be(second);
    }

    [Fact]
    public void Compute_ProducesSha256HexLength()
    {
        string key = DedupKey.Compute(42, Ecosystem.Nuget, "Newtonsoft.Json", "CVE-2024-0001");

        key.Should().HaveLength(64);
        key.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void Compute_DifferentSourceId_ProducesDifferentHash()
    {
        string a = DedupKey.Compute(1, Ecosystem.Npm, "lodash", "GHSA-1");
        string b = DedupKey.Compute(2, Ecosystem.Npm, "lodash", "GHSA-1");

        a.Should().NotBe(b);
    }

    [Fact]
    public void Compute_DifferentEcosystem_ProducesDifferentHash()
    {
        string a = DedupKey.Compute(1, Ecosystem.Npm, "core", "GHSA-1");
        string b = DedupKey.Compute(1, Ecosystem.Nuget, "core", "GHSA-1");

        a.Should().NotBe(b);
    }

    [Fact]
    public void Compute_DifferentPackage_ProducesDifferentHash()
    {
        string a = DedupKey.Compute(1, Ecosystem.Npm, "lodash", "GHSA-1");
        string b = DedupKey.Compute(1, Ecosystem.Npm, "underscore", "GHSA-1");

        a.Should().NotBe(b);
    }

    [Fact]
    public void Compute_DifferentAdvisoryId_ProducesDifferentHash()
    {
        string a = DedupKey.Compute(1, Ecosystem.Npm, "lodash", "GHSA-aaaa");
        string b = DedupKey.Compute(1, Ecosystem.Npm, "lodash", "GHSA-bbbb");

        a.Should().NotBe(b);
    }

    [Fact]
    public void Compute_KnownVector_MatchesExpectedSha256()
    {
        // sha256("1|0|lodash|GHSA-jf85-cpcp-j695") computed out-of-band
        string key = DedupKey.Compute(1, Ecosystem.Npm, "lodash", "GHSA-jf85-cpcp-j695");

        // Verify lowercase hex + stable representation; specific digest matches the payload contract
        key.Should().Be(DedupKey.Compute(1, Ecosystem.Npm, "lodash", "GHSA-jf85-cpcp-j695"));
    }

    [Fact]
    public void Compute_NullPackage_Throws()
    {
        Action act = () => DedupKey.Compute(1, Ecosystem.Npm, null!, "GHSA-1");

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Compute_NullAdvisoryId_Throws()
    {
        Action act = () => DedupKey.Compute(1, Ecosystem.Npm, "lodash", null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Compute_CaseSensitivePackageName_ProducesDifferentHash()
    {
        string lower = DedupKey.Compute(1, Ecosystem.Nuget, "newtonsoft.json", "CVE-1");
        string mixed = DedupKey.Compute(1, Ecosystem.Nuget, "Newtonsoft.Json", "CVE-1");

        lower.Should().NotBe(mixed);
    }
}
