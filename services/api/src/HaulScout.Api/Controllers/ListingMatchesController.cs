using Microsoft.AspNetCore.Mvc;
using HaulScout.Api.Authorization;
using HaulScout.Application.Contracts;
using HaulScout.Application.Inventory;
using HaulScout.Application.Security;

namespace HaulScout.Api.Controllers;

/// <summary>Which inventory item an imported marketplace listing is, ranked with reasons.</summary>
[ApiController]
[Route("api/platform-listings")]
[RequireOrganizationPermission(HaulScoutPermissions.ManageListings)]
public sealed class ListingMatchesController(ListingMatchService matches) : HaulScoutControllerBase
{
    [HttpGet("{id:guid}/match-candidates")]
    public async Task<ActionResult<ListingMatchDto>> MatchCandidates(Guid id, [FromQuery] string? search, CancellationToken cancellationToken) =>
        ToActionResult(await matches.RankAsync(id, search, cancellationToken));
}
