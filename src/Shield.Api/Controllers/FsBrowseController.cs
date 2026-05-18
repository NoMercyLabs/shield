using System.Runtime.InteropServices;
using Microsoft.Extensions.Configuration;
using Shield.Scanners;

namespace Shield.Api.Controllers;

[ApiController]
[Route("api/fs")]
[Authorize(Policy = ShieldPolicies.Admin)]
public sealed class FsBrowseController : ControllerBase
{
    // Cheap probe — avoid pathological directories with thousands of immediate children.
    private const int MaxImmediateChildrenScanned = 200;

    private readonly IConfiguration _configuration;

    public FsBrowseController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [HttpGet("browse")]
    public ActionResult<FsBrowseResponse> Browse(
        [FromQuery] string? path,
        [FromQuery] bool showHidden = false
    )
    {
        string[] roots = GetRoots();

        if (string.IsNullOrWhiteSpace(path))
        {
            return Ok(
                new FsBrowseResponse(
                    Path: string.Empty,
                    Parent: null,
                    Entries: RootsAsEntries(roots),
                    Roots: roots,
                    HasLockfiles: false
                )
            );
        }

        string normalised;
        try
        {
            normalised = Path.GetFullPath(path);
        }
        catch (Exception ex)
            when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return BadRequest(new { error = $"Invalid path: {ex.Message}" });
        }

        if (!Path.IsPathRooted(normalised))
            return BadRequest(new { error = "Path must be rooted." });

        if (!IsAllowed(normalised))
            return BadRequest(new { error = "Path is outside the configured browse roots." });

        if (!Directory.Exists(normalised))
            return BadRequest(new { error = $"Path does not exist: {normalised}" });

        List<FsEntry> entries = [];
        bool dirHasLockfilesDirectly = false;

        IEnumerable<string> subdirs;
        IEnumerable<string> files;
        try
        {
            subdirs = Directory.EnumerateDirectories(normalised);
            files = Directory.EnumerateFiles(normalised);
        }
        catch (UnauthorizedAccessException)
        {
            return Ok(
                new FsBrowseResponse(
                    Path: normalised,
                    Parent: GetParent(normalised),
                    Entries: [],
                    Roots: roots,
                    HasLockfiles: false
                )
            );
        }
        catch (IOException ex)
        {
            return BadRequest(new { error = $"Could not enumerate: {ex.Message}" });
        }

        foreach (string dir in subdirs)
        {
            string name = SafeName(dir);
            if (
                !showHidden
                && name.StartsWith('.')
                && !string.Equals(name, ".git", StringComparison.Ordinal)
            )
                continue;

            (bool hasLockfiles, int? lockfileCount) = ProbeLockfiles(dir);
            bool hasGitRepo = SafeDirExists(Path.Combine(dir, ".git"));

            entries.Add(
                new(
                    Name: name,
                    Path: dir,
                    IsDirectory: true,
                    HasLockfiles: hasLockfiles,
                    HasGitRepo: hasGitRepo,
                    LockfileCount: lockfileCount,
                    Size: null
                )
            );
        }

        foreach (string file in files)
        {
            string name = SafeName(file);
            if (!ParserFilenames.IsLockfile(name) && !ParserFilenames.IsManifest(name))
                continue;
            if (!showHidden && name.StartsWith('.'))
                continue;

            long? size = null;
            try
            {
                size = new FileInfo(file).Length;
            }
            catch
            {
                // best-effort, ignore.
            }

            if (ParserFilenames.IsLockfile(name))
                dirHasLockfilesDirectly = true;

            entries.Add(
                new(
                    Name: name,
                    Path: file,
                    IsDirectory: false,
                    HasLockfiles: false,
                    HasGitRepo: false,
                    LockfileCount: null,
                    Size: size
                )
            );
        }

        List<FsEntry> sorted = entries
            .OrderByDescending(entry => entry.IsDirectory)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Ok(
            new FsBrowseResponse(
                Path: normalised,
                Parent: GetParent(normalised),
                Entries: sorted,
                Roots: roots,
                HasLockfiles: dirHasLockfilesDirectly
            )
        );
    }

    private (bool HasLockfiles, int? Count) ProbeLockfiles(string dir)
    {
        try
        {
            int count = 0;
            int scanned = 0;
            foreach (string file in Directory.EnumerateFiles(dir))
            {
                if (++scanned > MaxImmediateChildrenScanned)
                    return (count > 0, count);
                if (ParserFilenames.IsLockfile(Path.GetFileName(file)))
                    count++;
            }
            return (count > 0, count);
        }
        catch (UnauthorizedAccessException)
        {
            return (false, null);
        }
        catch (IOException)
        {
            return (false, null);
        }
    }

    private static bool SafeDirExists(string path)
    {
        try
        {
            return Directory.Exists(path);
        }
        catch
        {
            return false;
        }
    }

    private static string SafeName(string path)
    {
        string name = Path.GetFileName(path);
        return string.IsNullOrEmpty(name) ? path : name;
    }

    private static string? GetParent(string path)
    {
        try
        {
            DirectoryInfo? parent = Directory.GetParent(path);
            return parent?.FullName;
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<FsEntry> RootsAsEntries(string[] roots) =>
        roots
            .Select(root => new FsEntry(
                Name: root,
                Path: root,
                IsDirectory: true,
                HasLockfiles: false,
                HasGitRepo: false,
                LockfileCount: null,
                Size: null
            ))
            .ToList();

    private static string[] GetRoots()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return DriveInfo
                .GetDrives()
                .Where(drive => drive.IsReady)
                .Select(drive => drive.RootDirectory.FullName)
                .ToArray();
        }
        return ["/"];
    }

    private bool IsAllowed(string fullPath)
    {
        string? allowlist = _configuration["Shield:Scanners:FsBrowseRoots"];
        if (string.IsNullOrWhiteSpace(allowlist))
            return true;

        string[] roots = allowlist.Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
        if (roots.Length == 0)
            return true;

        foreach (string root in roots)
        {
            string normalisedRoot;
            try
            {
                normalisedRoot = Path.GetFullPath(root);
            }
            catch
            {
                continue;
            }
            if (fullPath.StartsWith(normalisedRoot, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
