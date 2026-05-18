using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shield.Api.Contracts;
using Shield.Core.Domain;
using Shield.Data;
using Shield.Data.Identity;
using Xunit;

namespace Shield.Api.Tests;

public sealed class AccessTests
{
    [Fact]
    public async Task Maintainer_sees_only_granted_sources_in_list()
    {
        using MultiUserAccessFactory factory = new();
        (int sourceA, int sourceB, int sourceC, Guid maintainerId, _) = await SeedAclScenarioAsync(
            factory
        );

        HttpClient maintainer = factory.CreateClient();
        await maintainer.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest("maintainer-list", "Maintainer1!")
        );

        HttpResponseMessage response = await maintainer.GetAsync("/api/sources");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        List<SourceResponse>? sources = await response.Content.ReadFromJsonAsync<
            List<SourceResponse>
        >();
        sources.Should().NotBeNull();
        sources!.Select(source => source.Id).Should().BeEquivalentTo([sourceA, sourceB]);
        sources.Should().NotContain(source => source.Id == sourceC);
        _ = maintainerId;
    }

    [Fact]
    public async Task Maintainer_cannot_get_unauthorized_source_returns_404()
    {
        using MultiUserAccessFactory factory = new();
        (_, _, int sourceC, _, _) = await SeedAclScenarioAsync(factory);

        HttpClient maintainer = factory.CreateClient();
        await maintainer.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest("maintainer-list", "Maintainer1!")
        );

        HttpResponseMessage response = await maintainer.GetAsync($"/api/sources/{sourceC}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Maintainer_with_triage_can_ack_finding()
    {
        using MultiUserAccessFactory factory = new();
        (_, int sourceB, _, _, _) = await SeedAclScenarioAsync(factory);
        Guid findingB = await SeedFindingOnSourceAsync(factory, sourceB);

        HttpClient maintainer = factory.CreateClient();
        await maintainer.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest("maintainer-list", "Maintainer1!")
        );

        HttpResponseMessage ack = await maintainer.PostAsync(
            $"/api/findings/{findingB}/ack",
            content: null
        );
        ack.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Maintainer_with_read_cannot_ack_finding_returns_403()
    {
        using MultiUserAccessFactory factory = new();
        (int sourceA, _, _, _, _) = await SeedAclScenarioAsync(factory);
        Guid findingA = await SeedFindingOnSourceAsync(factory, sourceA);

        HttpClient maintainer = factory.CreateClient();
        await maintainer.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest("maintainer-list", "Maintainer1!")
        );

        HttpResponseMessage ack = await maintainer.PostAsync(
            $"/api/findings/{findingA}/ack",
            content: null
        );
        ack.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Group_grant_propagates_to_member()
    {
        using MultiUserAccessFactory factory = new();
        await SeedAdminAsync(factory);

        // Seed a source that the maintainer doesn't get directly — only through a group.
        int sourceGroupOnly;
        Guid memberUserId;
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
            Source source = new()
            {
                Type = SourceType.LocalFolder,
                Name = "group-only-source",
                ConfigJson = "{\"path\":\"/tmp/group-only\"}",
                ScanInterval = TimeSpan.FromHours(1),
                Enabled = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };
            db.Sources.Add(source);
            await db.SaveChangesAsync();
            sourceGroupOnly = source.Id;

            Microsoft.AspNetCore.Identity.UserManager<ShieldUser> userManager =
                scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ShieldUser>>();
            ShieldUser member = new()
            {
                UserName = "group-member",
                Email = "group-member@test",
                EmailConfirmed = true,
                CreatedAt = DateTime.UtcNow,
            };
            await userManager.CreateAsync(member, "Member1Pass!");
            await userManager.AddToRoleAsync(member, "Maintainer");
            memberUserId = member.Id;

            SourceGroup group = new() { Name = "mobile-team", CreatedAt = DateTime.UtcNow };
            db.SourceGroups.Add(group);
            await db.SaveChangesAsync();

            db.GroupMemberships.Add(
                new()
                {
                    GroupId = group.Id,
                    UserId = memberUserId,
                    AddedAt = DateTime.UtcNow,
                }
            );
            db.SourceAccesses.Add(
                new()
                {
                    SourceId = sourceGroupOnly,
                    GroupId = group.Id,
                    UserId = null,
                    Level = SourceAccessLevel.Read,
                    GrantedAt = DateTime.UtcNow,
                }
            );
            await db.SaveChangesAsync();
        }

        HttpClient memberClient = factory.CreateClient();
        await memberClient.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest("group-member", "Member1Pass!")
        );

        HttpResponseMessage list = await memberClient.GetAsync("/api/sources");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        List<SourceResponse>? sources = await list.Content.ReadFromJsonAsync<
            List<SourceResponse>
        >();
        sources.Should().NotBeNull();
        sources!.Should().Contain(source => source.Id == sourceGroupOnly);
    }

    [Fact]
    public async Task Admin_sees_all_sources_regardless_of_grants()
    {
        using MultiUserAccessFactory factory = new();
        (int sourceA, int sourceB, int sourceC, _, _) = await SeedAclScenarioAsync(factory);

        HttpClient admin = factory.CreateClient();
        await admin.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest("admin-acl", "Admin1Pass!")
        );

        HttpResponseMessage list = await admin.GetAsync("/api/sources");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        List<SourceResponse>? sources = await list.Content.ReadFromJsonAsync<
            List<SourceResponse>
        >();
        sources.Should().NotBeNull();
        sources!.Select(source => source.Id).Should().Contain([sourceA, sourceB, sourceC]);
    }

    // ---------- helpers ----------

    private static async Task<Guid> SeedFindingOnSourceAsync(
        MultiUserAccessFactory factory,
        int sourceId
    )
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
        Finding finding = new()
        {
            Id = Guid.NewGuid(),
            SourceId = sourceId,
            InventoryItemId = 1,
            AdvisoryRefId = Guid.NewGuid(),
            Severity = Severity.High,
            FirstSeenAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow,
            State = FindingState.Open,
            DedupKey = "acl-" + Guid.NewGuid().ToString("n"),
        };
        db.Findings.Add(finding);
        await db.SaveChangesAsync();
        return finding.Id;
    }

    private static async Task SeedAdminAsync(MultiUserAccessFactory factory)
    {
        HttpClient bootstrap = factory.CreateClient();
        await bootstrap.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest("admin-acl", "Admin1Pass!")
        );
    }

    private static async Task<(
        int SourceA,
        int SourceB,
        int SourceC,
        Guid MaintainerId,
        Guid AdminId
    )> SeedAclScenarioAsync(MultiUserAccessFactory factory)
    {
        await SeedAdminAsync(factory);

        int sourceA;
        int sourceB;
        int sourceC;
        Guid maintainerId;
        Guid adminId;
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
            Source a = new()
            {
                Type = SourceType.LocalFolder,
                Name = "source-a",
                ConfigJson = "{\"path\":\"/tmp/a\"}",
                ScanInterval = TimeSpan.FromHours(1),
                Enabled = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };
            Source b = new()
            {
                Type = SourceType.LocalFolder,
                Name = "source-b",
                ConfigJson = "{\"path\":\"/tmp/b\"}",
                ScanInterval = TimeSpan.FromHours(1),
                Enabled = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };
            Source c = new()
            {
                Type = SourceType.LocalFolder,
                Name = "source-c",
                ConfigJson = "{\"path\":\"/tmp/c\"}",
                ScanInterval = TimeSpan.FromHours(1),
                Enabled = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };
            db.Sources.AddRange(a, b, c);
            await db.SaveChangesAsync();
            sourceA = a.Id;
            sourceB = b.Id;
            sourceC = c.Id;

            Microsoft.AspNetCore.Identity.UserManager<ShieldUser> userManager =
                scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ShieldUser>>();
            ShieldUser maintainer = new()
            {
                UserName = "maintainer-list",
                Email = "m@test",
                EmailConfirmed = true,
                CreatedAt = DateTime.UtcNow,
            };
            await userManager.CreateAsync(maintainer, "Maintainer1!");
            await userManager.AddToRoleAsync(maintainer, "Maintainer");
            maintainerId = maintainer.Id;

            ShieldUser? adminUser = await userManager.FindByNameAsync("admin-acl");
            adminId = adminUser?.Id ?? Guid.Empty;

            db.SourceAccesses.AddRange(
                new SourceAccess
                {
                    SourceId = sourceA,
                    UserId = maintainerId,
                    Level = SourceAccessLevel.Read,
                    GrantedAt = DateTime.UtcNow,
                },
                new SourceAccess
                {
                    SourceId = sourceB,
                    UserId = maintainerId,
                    Level = SourceAccessLevel.Triage,
                    GrantedAt = DateTime.UtcNow,
                }
            );
            await db.SaveChangesAsync();
        }

        return (sourceA, sourceB, sourceC, maintainerId, adminId);
    }

    // SingleUserMode=false so role gates exercise real Identity principals.
    private sealed class MultiUserAccessFactory : ShieldWebAppFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration(
                (_, config) =>
                {
                    config.AddInMemoryCollection(
                        new Dictionary<string, string?> { ["Shield:SingleUser"] = "false" }
                    );
                }
            );
        }
    }
}
