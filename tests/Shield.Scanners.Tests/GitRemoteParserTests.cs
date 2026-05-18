using FluentAssertions;
using Xunit;

namespace Shield.Scanners.Tests;

public class GitRemoteParserTests
{
    [Theory]
    [InlineData("https://github.com/NoMercyLabs/shield.git", "github.com", "NoMercyLabs", "shield")]
    [InlineData("https://github.com/NoMercyLabs/shield", "github.com", "NoMercyLabs", "shield")]
    [InlineData("http://github.com/owner/repo", "github.com", "owner", "repo")]
    [InlineData("git@github.com:NoMercyLabs/shield.git", "github.com", "NoMercyLabs", "shield")]
    [InlineData("git@github.com:NoMercyLabs/shield", "github.com", "NoMercyLabs", "shield")]
    [InlineData(
        "ssh://git@github.com/NoMercyLabs/shield.git",
        "github.com",
        "NoMercyLabs",
        "shield"
    )]
    [InlineData(
        "ssh://git@github.com:22/NoMercyLabs/shield.git",
        "github.com",
        "NoMercyLabs",
        "shield"
    )]
    [InlineData("git://github.com/foo/bar.git", "github.com", "foo", "bar")]
    public void ParsesGithubUrls(string url, string host, string owner, string repo)
    {
        DetectedRemote? parsed = GitRemoteParser.ParseRemoteUrl(url);
        parsed.Should().NotBeNull();
        parsed!.Host.Should().Be(host);
        parsed.Owner.Should().Be(owner);
        parsed.Repo.Should().Be(repo);
        parsed.RemoteUrl.Should().Be(url);
    }

    [Theory]
    [InlineData(
        "https://git.nomercy.tv/Fillz/nomercy-ffmpeg.git",
        "git.nomercy.tv",
        "Fillz",
        "nomercy-ffmpeg"
    )]
    [InlineData("git@gitlab.com:group/sub/repo.git", "gitlab.com", "group/sub", "repo")]
    [InlineData("https://bitbucket.org/team/proj", "bitbucket.org", "team", "proj")]
    public void ParsesNonGithubHosts(string url, string host, string owner, string repo)
    {
        DetectedRemote? parsed = GitRemoteParser.ParseRemoteUrl(url);
        parsed.Should().NotBeNull();
        parsed!.Host.Should().Be(host);
        parsed.Owner.Should().Be(owner);
        parsed.Repo.Should().Be(repo);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-url")]
    [InlineData("https://github.com/")]
    [InlineData("https://github.com/onlyone")]
    [InlineData("git@github.com")]
    [InlineData("ftp://github.com/owner/repo")]
    public void RejectsMalformedUrls(string? url)
    {
        GitRemoteParser.ParseRemoteUrl(url).Should().BeNull();
    }

    [Fact]
    public void DetectFromWorkingTreeReturnsNullWhenNoDotGit()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "shield-git-test-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);
        try
        {
            GitRemoteParser.DetectFromWorkingTree(root).Should().BeNull();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DetectFromWorkingTreeParsesOriginBlock()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "shield-git-test-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        try
        {
            File.WriteAllText(
                Path.Combine(root, ".git", "config"),
                "[core]\n\trepositoryformatversion = 0\n[remote \"origin\"]\n\turl = git@github.com:NoMercyLabs/shield.git\n\tfetch = +refs/heads/*:refs/remotes/origin/*\n"
            );
            File.WriteAllText(Path.Combine(root, ".git", "HEAD"), "ref: refs/heads/master\n");

            DetectedRemote? detected = GitRemoteParser.DetectFromWorkingTree(root);
            detected.Should().NotBeNull();
            detected!.Host.Should().Be("github.com");
            detected.Owner.Should().Be("NoMercyLabs");
            detected.Repo.Should().Be("shield");
            detected.Branch.Should().Be("master");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DetectFromWorkingTreeReturnsNullWhenNoOriginBlock()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "shield-git-test-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        try
        {
            File.WriteAllText(
                Path.Combine(root, ".git", "config"),
                "[core]\n\trepositoryformatversion = 0\n[remote \"upstream\"]\n\turl = git@github.com:foo/bar.git\n"
            );
            GitRemoteParser.DetectFromWorkingTree(root).Should().BeNull();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
