using Microsoft.AspNetCore.Mvc;
using HaulScout.Api.Authorization;
using HaulScout.Api.Services;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Application.Services;
using HaulScout.Infrastructure.Marketplace;

namespace HaulScout.Api.Controllers;

[ApiController]
[Route("api/marketplace-lookup")]
[RequireOrganizationPermission(HaulScoutPermissions.ManageInventory)]
public sealed class MarketplaceLookupController(
    IMarketplaceLookupService lookupService,
    MarketplaceLookupPersistenceService persistenceService,
    IEnumerable<IMarketplacePricingProvider> pricingProviders,
    IResaleEstimator resaleEstimator,
    IVisionIdentificationService visionService) : HaulScoutControllerBase
{
    /// <summary>
    /// Lookup barcode: runs product identification, pricing providers, and saves reusable comp observations.
    /// </summary>
    [HttpGet("barcode/{barcode}")]
    public async Task<ActionResult<MarketplaceLookupResult>> LookupBarcode(string barcode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(barcode))
        {
            return Invalid("Barcode is required.");
        }

        MarketplaceLookupResult marketplace = await lookupService.SearchByBarcodeAsync(barcode, cancellationToken);
        return await persistenceService.PersistBarcodeLookupAsync(barcode, marketplace, cancellationToken);
    }

    [HttpGet("search")]
    public async Task<ActionResult<MarketplaceLookupResult>> Search([FromQuery] string q, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return Invalid("Search query is required.");
        }

        return await lookupService.SearchByKeywordAsync(q, cancellationToken);
    }

    /// <summary>
    /// Returns the configured and available marketplace pricing providers and vision service status.
    /// </summary>
    [HttpGet("status")]
    public ActionResult<MarketplaceProviderStatusDto> Status()
    {
        List<string> configuredProviders = pricingProviders
            .Where(provider => provider.IsConfigured)
            .Select(provider => provider.Name)
            .ToList();

        // The Gemini grounded resale estimator is itself a real pricing source (live comps), so a
        // lookup is worth running whenever it OR a marketplace provider is configured.
        if (resaleEstimator.IsConfigured)
        {
            configuredProviders.Add("AI Comps (Gemini)");
        }

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
