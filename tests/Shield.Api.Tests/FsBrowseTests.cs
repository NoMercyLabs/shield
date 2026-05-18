using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Shield.Api.Contracts;
using Xunit;

namespace Shield.Api.Tests;

public sealed class FsBrowseTests : IClassFixture<ShieldWebAppFactory>
{
    private readonly ShieldWebAppFactory _factory;

    public FsBrowseTests(ShieldWebAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task BrowseWithNoPathReturnsRoots()
    {
        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync("/api/fs/browse");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        FsBrowseResponse? body = await response.Content.ReadFromJsonAsync<FsBrowseResponse>();
        body.Should().NotBeNull();
        body!.Roots.Should().NotBeEmpty();
        body.Entries.Should().NotBeEmpty();
    }

    [Fact]
    public async Task BrowseReturnsEntriesAndFlagsLockfileDir()
    {
        string tempRoot = Path.Combine(
            Path.GetTempPath(),
            "shield-fsbrowse-tests",
            Guid.NewGuid().ToString("n")
        );
        Directory.CreateDirectory(tempRoot);
        string sub = Path.Combine(tempRoot, "with-lockfile");
        Directory.CreateDirectory(sub);
        await File.WriteAllTextAsync(Path.Combine(sub, "package-lock.json"), "{}");
        string emptySub = Path.Combine(tempRoot, "empty");
        Directory.CreateDirectory(emptySub);

        try
        {
            HttpClient client = _factory.CreateClient();
            HttpResponseMessage response = await client.GetAsync(
                $"/api/fs/browse?path={Uri.EscapeDataString(tempRoot)}"
            );
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            FsBrowseResponse? body = await response.Content.ReadFromJsonAsync<FsBrowseResponse>();
            body.Should().NotBeNull();
            body!.Path.Should().Be(Path.GetFullPath(tempRoot));
            body.Entries.Should().HaveCountGreaterThanOrEqualTo(2);

            FsEntry? lockDir = body.Entries.FirstOrDefault(entry => entry.Name == "with-lockfile");
            lockDir.Should().NotBeNull();
            lockDir!.IsDirectory.Should().BeTrue();
            lockDir.HasLockfiles.Should().BeTrue();
            lockDir.LockfileCount.Should().Be(1);

            FsEntry? emptyDir = body.Entries.FirstOrDefault(entry => entry.Name == "empty");
            emptyDir.Should().NotBeNull();
            emptyDir!.HasLockfiles.Should().BeFalse();
        }
        finally
        {
            try
            {
                Directory.Delete(tempRoot, recursive: true);
            }
            catch
            {
                // best-effort.
            }
        }
    }

    [Fact]
    public async Task BrowseRejectsUnrootedPath()
    {
        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync(
            "/api/fs/browse?path=relative-not-rooted"
        );
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task BrowseRejectsTraversalOutsideAllowlistWhenConfigured()
    {
        string allowedRoot = Path.Combine(
            Path.GetTempPath(),
            "shield-fsbrowse-allow",
            Guid.NewGuid().ToString("n")
        );
        Directory.CreateDirectory(allowedRoot);
        string deniedRoot = Path.Combine(
            Path.GetTempPath(),
            "shield-fsbrowse-deny",
            Guid.NewGuid().ToString("n")
        );
        Directory.CreateDirectory(deniedRoot);

        WebApplicationFactory<Program> withAllowlist = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration(
                (_, config) =>
                {
                    config.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["Shield:Scanners:FsBrowseRoots"] = allowedRoot,
                        }
                    );
                }
            );
        });

        try
        {
            HttpClient client = withAllowlist.CreateClient();

            HttpResponseMessage allowed = await client.GetAsync(
                $"/api/fs/browse?path={Uri.EscapeDataString(allowedRoot)}"
            );
            allowed.StatusCode.Should().Be(HttpStatusCode.OK);

            HttpResponseMessage denied = await client.GetAsync(
                $"/api/fs/browse?path={Uri.EscapeDataString(deniedRoot)}"
            );
            denied.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
        finally
        {
            try
            {
                Directory.Delete(allowedRoot, recursive: true);
            }
            catch { }
            try
            {
                Directory.Delete(deniedRoot, recursive: true);
            }
            catch { }
        }
    }
}
