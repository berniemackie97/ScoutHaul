using Microsoft.AspNetCore.Mvc;
using ScoutHaul.Api.Authorization;
using ScoutHaul.Api.Services;
using ScoutHaul.Application.Contracts;
using ScoutHaul.Application.Services;
using ScoutHaul.Infrastructure.Marketplace;

namespace ScoutHaul.Api.Controllers;

[ApiController]
[Route("api/marketplace-lookup")]
[RequireOrganizationPermission(ScoutHaulPermissions.ManageInventory)]
public sealed class MarketplaceLookupController(
    IMarketplaceLookupService lookupService,
    MarketplaceLookupPersistenceService persistenceService,
    IEnumerable<IMarketplacePricingProvider> pricingProviders,
    IVisionIdentificationService visionService) : ControllerBase
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
    /// Returns the configured and available marketplace pricing providers and vision service status.
    /// </summary>
    [HttpGet("status")]
    public ActionResult<MarketplaceProviderStatusDto> Status()
    {
        var configuredProviders = pricingProviders
            .Where(provider => provider.IsConfigured)
            .Select(provider => provider.Name)
            .ToList();

        return new MarketplaceProviderStatusDto(
            configuredProviders,
            configuredProviders.Count > 0,
            visionService.IsConfigured);
    }
}

public sealed record MarketplaceProviderStatusDto(
    IReadOnlyList<string> ConfiguredProviders,
    bool HasProviders,
    bool HasVisionService);
