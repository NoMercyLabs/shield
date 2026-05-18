using FluentAssertions;
using Shield.Api.Services.ManifestEditors;
using Shield.Core.Domain;
using Xunit;

namespace Shield.Api.Tests;

// Tests for NugetManifestEditor, specifically the CPM (Central Package Management)
// Directory.Packages.props path and the ManifestPath-targeted editing.
public sealed class NugetManifestEditorTests
{
    private static string TempDir()
    {
        string dir = Path.Combine(
            Path.GetTempPath(),
            "shield-nuget-editor-" + Guid.NewGuid().ToString("n")
        );
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Cleanup(string dir)
    {
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch
        { /* best-effort */
        }
    }

    [Fact]
    public void ApplyUpdatesPackageVersionInDirectoryPackagesPropsWhenManifestPathIsSet()
    {
        string dir = TempDir();
        try
        {
            string propsContent =
                "<Project>\n"
                + "  <ItemGroup>\n"
                + "    <PackageVersion Include=\"Newtonsoft.Json\" Version=\"12.0.3\" />\n"
                + "    <PackageVersion Include=\"Serilog\" Version=\"3.0.0\" />\n"
                + "  </ItemGroup>\n"
                + "</Project>\n";
            string propsPath = Path.Combine(dir, "Directory.Packages.props");
            File.WriteAllText(propsPath, propsContent);

            NugetManifestEditor editor = new();
            InventoryItem item = new()
            {
                Ecosystem = Ecosystem.Nuget,
                Name = "Newtonsoft.Json",
                Version = "12.0.3",
                IsDirect = true,
                ManifestPath = "Directory.Packages.props",
            };

            ManifestEditOutcome outcome = editor.Apply(dir, item, "13.0.3");

            outcome.UnsupportedReason.Should().BeNull();
            outcome
                .ChangedFiles.Should()
                .ContainSingle(file => file.EndsWith("Directory.Packages.props"));
            outcome.FollowUpCommand.Should().Be("dotnet restore");

            string written = File.ReadAllText(propsPath);
            written.Should().Contain("Version=\"13.0.3\"");
            written.Should().Contain("Version=\"3.0.0\"");
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void ApplyWithManifestPathDoesNotTouchOtherCsprojFiles()
    {
        string dir = TempDir();
        try
        {
            string propsContent =
                "<Project>\n"
                + "  <ItemGroup>\n"
                + "    <PackageVersion Include=\"Newtonsoft.Json\" Version=\"12.0.3\" />\n"
                + "  </ItemGroup>\n"
                + "</Project>\n";
            File.WriteAllText(Path.Combine(dir, "Directory.Packages.props"), propsContent);

            string csprojContent =
                "<Project Sdk=\"Microsoft.NET.Sdk\">\n"
                + "  <ItemGroup>\n"
                + "    <PackageReference Include=\"Newtonsoft.Json\" Version=\"12.0.3\" />\n"
                + "  </ItemGroup>\n"
                + "</Project>\n";
            string csprojPath = Path.Combine(dir, "MyApp.csproj");
            File.WriteAllText(csprojPath, csprojContent);

            NugetManifestEditor editor = new();
            InventoryItem item = new()
            {
                Ecosystem = Ecosystem.Nuget,
                Name = "Newtonsoft.Json",
                Version = "12.0.3",
                IsDirect = true,
                ManifestPath = "Directory.Packages.props",
            };

            ManifestEditOutcome outcome = editor.Apply(dir, item, "13.0.3");

            outcome.UnsupportedReason.Should().BeNull();
            outcome.ChangedFiles.Should().ContainSingle();
            outcome.ChangedFiles[0].Should().EndWith("Directory.Packages.props");

            // The csproj must be untouched.
            string csprojAfter = File.ReadAllText(csprojPath);
            csprojAfter.Should().Contain("Version=\"12.0.3\"");
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void ApplyWithoutManifestPathScansAllManifestsUnderRoot()
    {
        string dir = TempDir();
        try
        {
            string csprojContent =
                "<Project Sdk=\"Microsoft.NET.Sdk\">\n"
                + "  <ItemGroup>\n"
                + "    <PackageReference Include=\"Newtonsoft.Json\" Version=\"12.0.3\" />\n"
                + "  </ItemGroup>\n"
                + "</Project>\n";
            File.WriteAllText(Path.Combine(dir, "MyApp.csproj"), csprojContent);

            NugetManifestEditor editor = new();
            InventoryItem item = new()
            {
                Ecosystem = Ecosystem.Nuget,
                Name = "Newtonsoft.Json",
                Version = "12.0.3",
                IsDirect = true,
                ManifestPath = null,
            };

            ManifestEditOutcome outcome = editor.Apply(dir, item, "13.0.3");

            outcome.UnsupportedReason.Should().BeNull();
            outcome.ChangedFiles.Should().ContainSingle(file => file.EndsWith("MyApp.csproj"));
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void ApplyWithCsprojManifestPathEditsOnlyThatFile()
    {
        string dir = TempDir();
        try
        {
            string subDir = Path.Combine(dir, "src", "Foo");
            Directory.CreateDirectory(subDir);

            string fooCsprojContent =
                "<Project Sdk=\"Microsoft.NET.Sdk\">\n"
                + "  <ItemGroup>\n"
                + "    <PackageReference Include=\"Serilog\" Version=\"3.0.0\" />\n"
                + "  </ItemGroup>\n"
                + "</Project>\n";
            string fooCsprojPath = Path.Combine(subDir, "Foo.csproj");
            File.WriteAllText(fooCsprojPath, fooCsprojContent);

            string barCsprojContent =
                "<Project Sdk=\"Microsoft.NET.Sdk\">\n"
                + "  <ItemGroup>\n"
                + "    <PackageReference Include=\"Serilog\" Version=\"3.0.0\" />\n"
                + "  </ItemGroup>\n"
                + "</Project>\n";
            string barCsprojPath = Path.Combine(dir, "Bar.csproj");
            File.WriteAllText(barCsprojPath, barCsprojContent);

            NugetManifestEditor editor = new();
            InventoryItem item = new()
            {
                Ecosystem = Ecosystem.Nuget,
                Name = "Serilog",
                Version = "3.0.0",
                IsDirect = true,
                ManifestPath = "src/Foo/Foo.csproj",
            };

            ManifestEditOutcome outcome = editor.Apply(dir, item, "4.0.0");

            outcome.UnsupportedReason.Should().BeNull();
            outcome.ChangedFiles.Should().ContainSingle(file => file.EndsWith("Foo.csproj"));

            string fooAfter = File.ReadAllText(fooCsprojPath);
            fooAfter.Should().Contain("Version=\"4.0.0\"");

            string barAfter = File.ReadAllText(barCsprojPath);
            barAfter.Should().Contain("Version=\"3.0.0\"");
        }
        finally
        {
            Cleanup(dir);
        }
    }
}
