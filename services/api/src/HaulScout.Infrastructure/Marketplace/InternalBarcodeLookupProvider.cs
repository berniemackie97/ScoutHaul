using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using HaulScout.Application.Contracts;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Infrastructure.Marketplace;

/// <summary>
/// Looks up barcodes from our own ProductIdentities table — data previously
/// saved from external API results or manually entered by users.
/// Runs FIRST in the provider chain: fastest, free, gets smarter over time.
/// Cross-org lookup since barcode→product mapping is universal.
/// </summary>
internal sealed class InternalBarcodeLookupProvider(
    HaulScoutDbContext db,
    ILogger<InternalBarcodeLookupProvider> logger) : IBarcodeLookupProvider
{
    public string Name => "Internal";
    public bool IsConfigured => true;

    public async Task<BarcodeLookupResult?> LookupAsync(string barcode, CancellationToken cancellationToken)
    {
        string clean = barcode.Trim();
        if (string.IsNullOrWhiteSpace(clean))
        {
            return null;
        }

        try
        {
            // Match by UPC or EAN — barcode→product is universal across orgs.
            // Prefer user-verified data over auto-cached external data.
            ProductIdentity? product = await db.ProductIdentities
                .AsNoTracking()
                .Where(p => p.Upc == clean || p.Ean == clean)
                .OrderByDescending(p => p.DataSource == "user" ? 1 : 0)
                .ThenByDescending(p => p.UpdatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (product is null || string.IsNullOrWhiteSpace(product.Title))
            {
                return null;
            }

            logger.LogDebug("Internal DB hit for barcode {Barcode}: {Title}", clean, product.Title);

            return new BarcodeLookupResult(
                Barcode: clean,
                Title: product.Title,
                Brand: product.Brand,
                Description: null,
                Category: product.Category,
                ImageUrl: null,
                Upc: product.Upc,
                Ean: product.Ean);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Internal barcode lookup failed for {Barcode}", clean);
            return null;
        }
    }
}
