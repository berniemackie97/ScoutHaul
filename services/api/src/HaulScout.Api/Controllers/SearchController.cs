using Microsoft.AspNetCore.Mvc;
using HaulScout.Api.Authorization;
using HaulScout.Application.Contracts;
using HaulScout.Application.Search;

namespace HaulScout.Api.Controllers;

/// <summary>
/// One question — "where is this thing?" — across everything the operator can see.
///
/// Gated on membership rather than one permission: the search reads across inventory, orders,
/// customers and purchases, and a caller who may see only one of those should get that one rather
/// than all four or a 403. <see cref="SearchService"/> checks each section on its own and leaves out
/// what is not allowed.
/// </summary>
[ApiController]
[Route("api/search")]
[RequireOrganizationMember]
public sealed class SearchController(SearchService search) : HaulScoutControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SearchResultsDto>> Search(
        [FromQuery] string? q,
        CancellationToken cancellationToken) =>
        await search.SearchAsync(q, cancellationToken);
}
