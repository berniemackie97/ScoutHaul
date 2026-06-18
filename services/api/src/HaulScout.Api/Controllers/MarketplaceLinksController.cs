using Microsoft.AspNetCore.Mvc;
using HaulScout.Api.Authorization;
using HaulScout.Application.Contracts;
using HaulScout.Application.Services;

namespace HaulScout.Api.Controllers;

[ApiController]
[Route("api/marketplace-links")]
[RequireOrganizationPermission(HaulScoutPermissions.ManageInventory)]
public sealed class MarketplaceLinksController(IMarketplaceSearchLinkGenerator linkGenerator) : ControllerBase
{
    [HttpGet]
    public ActionResult<MarketplaceSearchLinksDto> Get([FromQuery] string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return BadRequest("Search query is required.");
        }

        return linkGenerator.Generate(query);
    }
}
