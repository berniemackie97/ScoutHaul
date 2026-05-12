using Microsoft.AspNetCore.Mvc;
using ScoutHaul.Application.Contracts;
using ScoutHaul.Application.Services;

namespace ScoutHaul.Api.Controllers;

[ApiController]
[Route("api/marketplace-lookup")]
public sealed class MarketplaceLookupController(IMarketplaceLookupService lookupService) : ControllerBase
{
    [HttpGet("barcode/{barcode}")]
    public async Task<ActionResult<MarketplaceLookupResult>> LookupBarcode(string barcode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(barcode))
        {
            return BadRequest("Barcode is required.");
        }

        return await lookupService.SearchByBarcodeAsync(barcode, cancellationToken);
    }

    [HttpGet("search")]
    public async Task<ActionResult<MarketplaceLookupResult>> Search([FromQuery] string q, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return BadRequest("Search query is required.");
        }

        return await lookupService.SearchByKeywordAsync(q, cancellationToken);
    }
}
