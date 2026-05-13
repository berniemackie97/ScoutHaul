using Microsoft.AspNetCore.Mvc;
using ScoutHaul.Api.Authorization;
using ScoutHaul.Api.Services;
using ScoutHaul.Application.Contracts;
using ScoutHaul.Application.Services;

namespace ScoutHaul.Api.Controllers;

[ApiController]
[Route("api/marketplace-lookup")]
[RequireOrganizationPermission(ScoutHaulPermissions.ManageInventory)]
public sealed class MarketplaceLookupController(
    IMarketplaceLookupService lookupService,
    IBarcodeLookupService barcodeLookupService,
    MarketplaceLookupPersistenceService persistenceService) : ControllerBase
{
    /// <summary>
    /// Lookup barcode: runs product identification, pricing providers, and saves reusable comp observations.
    /// </summary>
    [HttpGet("barcode/{barcode}")]
    public async Task<ActionResult<MarketplaceLookupResult>> LookupBarcode(string barcode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(barcode))
        {
            return BadRequest("Barcode is required.");
        }

        MarketplaceLookupResult marketplace = await lookupService.SearchByBarcodeAsync(barcode, cancellationToken);
        return await persistenceService.PersistBarcodeLookupAsync(barcode, marketplace, cancellationToken);
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

    /// <summary>
    /// Product identification only (no pricing). Uses the configured barcode provider chain.
    /// </summary>
    [HttpGet("product/{barcode}")]
    public async Task<ActionResult<BarcodeLookupResult>> LookupProduct(string barcode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(barcode))
        {
            return BadRequest("Barcode is required.");
        }

        var result = await barcodeLookupService.LookupAsync(barcode, cancellationToken);
        if (result is null)
        {
            return NotFound("No product found for this barcode.");
        }

        return result;
    }
}
