using Microsoft.AspNetCore.Mvc;
using ScoutHaul.Application.Contracts;
using ScoutHaul.Application.Services;

namespace ScoutHaul.Api.Controllers;

[ApiController]
[Route("api/marketplace-lookup")]
public sealed class MarketplaceLookupController(
    IMarketplaceLookupService lookupService,
    IBarcodeLookupService barcodeLookupService) : ControllerBase
{
    /// <summary>
    /// Lookup barcode: returns product identity (UPCitemdb) + marketplace pricing (eBay) combined.
    /// </summary>
    [HttpGet("barcode/{barcode}")]
    public async Task<ActionResult<MarketplaceLookupResult>> LookupBarcode(string barcode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(barcode))
        {
            return BadRequest("Barcode is required.");
        }

        // Run product identification and marketplace pricing in parallel
        var productTask = barcodeLookupService.LookupAsync(barcode, cancellationToken);
        var marketplaceTask = lookupService.SearchByBarcodeAsync(barcode, cancellationToken);

        await Task.WhenAll(productTask, marketplaceTask);

        var product = await productTask;
        var marketplace = await marketplaceTask;

        // If marketplace has no results but product lookup succeeded, enrich with product data
        if (product is not null)
        {
            marketplace = marketplace with
            {
                ProductTitle = product.Title,
                ProductBrand = product.Brand,
                ProductImageUrl = product.ImageUrl,
                ProductCategory = product.Category,
            };
        }

        return marketplace;
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
    /// Product identification only (no pricing). Uses UPCitemdb.
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
