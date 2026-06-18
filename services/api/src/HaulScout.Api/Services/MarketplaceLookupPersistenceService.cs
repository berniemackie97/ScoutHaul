using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using HaulScout.Application.Contracts;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Api.Services;

public sealed class MarketplaceLookupPersistenceService(
    HaulScoutDbContext db,
    ICurrentUserContext currentUser,
    IConfiguration configuration,
    ILogger<MarketplaceLookupPersistenceService> logger)
{
    public async Task<MarketplaceLookupResult> PersistBarcodeLookupAsync(
        string barcode,
        MarketplaceLookupResult result,
        CancellationToken cancellationToken)
    {
        if (!configuration.GetValue("MarketplaceLookup:PersistLookupResults", true)
            || (result.Listings.Count == 0 && string.IsNullOrWhiteSpace(result.ProductTitle)))
        {
            return result;
        }

        try
        {
            ProductIdentity product = await ResolveProductIdentityAsync(barcode, result, cancellationToken);
            int savedObservationCount = await SaveObservationsAsync(product.Id, result.Listings, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);

            return result with
            {
                ProductIdentityId = product.Id,
                SavedObservationCount = savedObservationCount
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Marketplace lookup persistence failed for barcode {Barcode}", barcode);
            return result with
            {
                ProviderWarnings = AppendWarning(result.ProviderWarnings, "Lookup results were not saved to comps.")
            };
        }
    }

    private async Task<ProductIdentity> ResolveProductIdentityAsync(
        string barcode,
        MarketplaceLookupResult result,
        CancellationToken cancellationToken)
    {
        Guid organizationId = currentUser.OrganizationId;
        string? normalizedBarcode = NormalizeBarcode(barcode);
        string? upc = normalizedBarcode is { Length: 8 or 12 } ? normalizedBarcode : null;
        string? ean = normalizedBarcode is { Length: 13 or 14 } ? normalizedBarcode : null;
        string? isbn = normalizedBarcode is { Length: 10 } || (normalizedBarcode is { Length: 13 } && IsLikelyBook(result))
            ? normalizedBarcode
            : null;

        ProductIdentity? product = null;
        if (upc is not null || ean is not null || isbn is not null)
        {
            product = await db.ProductIdentities.FirstOrDefaultAsync(item =>
                item.OrganizationId == organizationId
                && ((upc != null && item.Upc == upc)
                    || (ean != null && item.Ean == ean)
                    || (isbn != null && item.Isbn == isbn)),
                cancellationToken);
        }

        string title = TrimTo(
            FirstNonEmpty(result.ProductTitle, result.Listings.FirstOrDefault()?.Title, result.Query, barcode) ?? barcode,
            300);

        product ??= await db.ProductIdentities.FirstOrDefaultAsync(item =>
            item.OrganizationId == organizationId && item.Title == title,
            cancellationToken);

        if (product is null)
        {
            product = new ProductIdentity
            {
                OrganizationId = organizationId,
                Title = title,
                Brand = TrimToNull(result.ProductBrand, 160),
                Category = TrimToNull(result.ProductCategory, 160),
                Upc = upc,
                Ean = ean,
                Isbn = isbn,
                SearchQuery = TrimToNull(result.Query, 500),
                DataSource = "external"
            };

            db.ProductIdentities.Add(product);
            return product;
        }

        // Never overwrite user-verified product data with external API results.
        if (product.DataSource == "user")
        {
            return product;
        }

        product.Brand ??= TrimToNull(result.ProductBrand, 160);
        product.Category ??= TrimToNull(result.ProductCategory, 160);
        product.Upc ??= upc;
        product.Ean ??= ean;
        product.Isbn ??= isbn;
        product.SearchQuery = TrimToNull(result.Query, 500) ?? product.SearchQuery;
        return product;
    }

    private async Task<int> SaveObservationsAsync(
        Guid productIdentityId,
        IReadOnlyList<MarketplaceListingDto> listings,
        CancellationToken cancellationToken)
    {
        int maxListings = Math.Clamp(configuration.GetValue("MarketplaceLookup:MaxListingsToPersist", 25), 0, 100);
        if (maxListings == 0)
        {
            return 0;
        }

        int saved = 0;
        Guid organizationId = currentUser.OrganizationId;
        DateTimeOffset now = DateTimeOffset.UtcNow;

        foreach (MarketplaceListingDto listing in listings.Where(item => item.Price > 0).Take(maxListings))
        {
            string title = TrimTo(listing.Title, 300);
            string source = TrimTo(listing.Source, 80);
            string? sourceUrl = TrimToNull(listing.ItemUrl, 1500);
            string? condition = TrimToNull(listing.Condition, 120);
            decimal shippingPrice = listing.ShippingCost ?? 0;
            decimal totalPrice = listing.Price + shippingPrice;
            MarketObservationType observationType = ClassifyObservation(listing);

            MarketObservation? existing = await FindExistingObservationAsync(
                organizationId,
                productIdentityId,
                source,
                sourceUrl,
                title,
                condition,
                totalPrice,
                cancellationToken);

            if (existing is null)
            {
                db.MarketObservations.Add(new MarketObservation
                {
                    OrganizationId = organizationId,
                    ProductIdentityId = productIdentityId,
                    ObservationType = observationType,
                    Source = source,
                    SourceUrl = sourceUrl,
                    Title = title,
                    Price = listing.Price,
                    ShippingPrice = listing.ShippingCost,
                    TotalPrice = totalPrice,
                    Currency = "USD",
                    Condition = condition,
                    ObservedAt = now,
                    SoldAt = listing.IsSold ? listing.SoldDate ?? now : null
                });
                saved++;
                continue;
            }

            existing.ObservationType = observationType;
            existing.Title = title;
            existing.Price = listing.Price;
            existing.ShippingPrice = listing.ShippingCost;
            existing.TotalPrice = totalPrice;
            existing.Condition = condition;
            existing.ObservedAt = now;
            existing.SoldAt = listing.IsSold ? listing.SoldDate ?? now : null;
            saved++;
        }

        return saved;
    }

    private async Task<MarketObservation?> FindExistingObservationAsync(
        Guid organizationId,
        Guid productIdentityId,
        string source,
        string? sourceUrl,
        string title,
        string? condition,
        decimal totalPrice,
        CancellationToken cancellationToken)
    {
        IQueryable<MarketObservation> query = db.MarketObservations.Where(item =>
            item.OrganizationId == organizationId
            && item.ProductIdentityId == productIdentityId
            && item.Source == source
            && !item.IsArchived);

        if (!string.IsNullOrWhiteSpace(sourceUrl))
        {
            return await query.FirstOrDefaultAsync(item => item.SourceUrl == sourceUrl, cancellationToken);
        }

        DateTimeOffset cutoff = DateTimeOffset.UtcNow.AddDays(-1);
        return await query.FirstOrDefaultAsync(item =>
            item.Title == title
            && item.Condition == condition
            && item.TotalPrice == totalPrice
            && item.ObservedAt >= cutoff,
            cancellationToken);
    }

    private static MarketObservationType ClassifyObservation(MarketplaceListingDto listing)
    {
        if (listing.IsSold)
        {
            return MarketObservationType.Sold;
        }

        return listing.Source.StartsWith("PriceCharting", StringComparison.OrdinalIgnoreCase)
            || listing.Source.StartsWith("Keepa", StringComparison.OrdinalIgnoreCase)
                ? MarketObservationType.Estimate
                : MarketObservationType.ActiveListing;
    }

    private static string? NormalizeBarcode(string value)
    {
        string normalized = new(value.Where(char.IsDigit).ToArray());
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static bool IsLikelyBook(MarketplaceLookupResult result)
    {
        string text = string.Join(' ', result.ProductCategory, result.ProductTitle, result.Query);
        return text.Contains("book", StringComparison.OrdinalIgnoreCase)
            || text.Contains("isbn", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> AppendWarning(IReadOnlyList<string>? warnings, string warning)
    {
        List<string> next = warnings?.ToList() ?? [];
        if (!next.Contains(warning, StringComparer.OrdinalIgnoreCase))
        {
            next.Add(warning);
        }

        return next;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
    }

    private static string? TrimToNull(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return TrimTo(value, maxLength);
    }

    private static string TrimTo(string value, int maxLength)
    {
        value = value.Trim();
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
