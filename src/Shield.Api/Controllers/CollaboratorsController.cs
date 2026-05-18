namespace Shield.Api.Controllers;

// Read-only proxy over GitHub's orgs/members/search endpoints, used by AccessView's
// "Invite from GitHub" picker. Admin-only — invitations are an admin-managed boundary,
// and the admin's OAuth token is the credential used to talk to GitHub.
[ApiController]
[Route("api/collaborators")]
[Authorize(Policy = ShieldPolicies.Admin)]
[NoApiToken]
public sealed class CollaboratorsController : ControllerBase
{
    private readonly IGithubCollaboratorDirectory _directory;
    private readonly ILogger<CollaboratorsController> _logger;

    public CollaboratorsController(
        IGithubCollaboratorDirectory directory,
        ILogger<CollaboratorsController> logger
    )
    {
        _directory = directory;
        _logger = logger;
    }

    [HttpGet("github/orgs")]
    public async Task<IActionResult> ListOrgs(CancellationToken ct)
    {
        try
        {
            IReadOnlyList<GithubOrgSummary>? orgs = await _directory.ListOrgsAsync(ct);
            if (orgs is null)
                return BadRequest(new { error = "github_not_connected" });
            return Ok(new GithubOrgListResponse(orgs));
        }
        catch (GithubTokenInvalidException)
        {
            return Conflict(new { error = "github_token_invalid", action = "reconnect" });
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "GitHub orgs listing failed");
            return StatusCode(502, new { error = "github_api_failed" });
        }
    }

    [HttpGet("github/orgs/{org}/members")]
    public async Task<IActionResult> ListMembers(
        string org,
        [FromQuery] int page = 1,
        [FromQuery] int perPage = 100,
        CancellationToken ct = default
    )
    {
        if (string.IsNullOrWhiteSpace(org))
            return BadRequest(new { error = "org_required" });

        try
        {
            GithubMemberListResponse? members = await _directory.ListMembersAsync(
                org,
                page,
                perPage,
                ct
            );
            if (members is null)
                return BadRequest(new { error = "github_not_connected" });
            return Ok(members);
        }
        catch (GithubTokenInvalidException)
        {
            return Conflict(new { error = "github_token_invalid", action = "reconnect" });
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "GitHub members listing failed for {Org}", org);
            return StatusCode(502, new { error = "github_api_failed" });
        }
    }

    [HttpGet("github/users/search")]
    public async Task<IActionResult> SearchUsers([FromQuery] string q, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q))
            return Ok(new GithubUserSearchResponse([]));

        try
        {
            IReadOnlyList<GithubUserSummary>? users = await _directory.SearchUsersAsync(q, ct);
            if (users is null)
                return BadRequest(new { error = "github_not_connected" });
            return Ok(new GithubUserSearchResponse(users));
        }
        catch (GithubTokenInvalidException)
        {
            return Conflict(new { error = "github_token_invalid", action = "reconnect" });
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "GitHub user search failed");
            return StatusCode(502, new { error = "github_api_failed" });
        }
    }
}
