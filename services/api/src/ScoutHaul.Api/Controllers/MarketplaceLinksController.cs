using Microsoft.AspNetCore.Mvc;
using ScoutHaul.Api.Authorization;
using ScoutHaul.Application.Contracts;
using ScoutHaul.Application.Services;

namespace ScoutHaul.Api.Controllers;

[ApiController]
[Route("api/marketplace-links")]
[RequireOrganizationPermission(ScoutHaulPermissions.ManageInventory)]
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
