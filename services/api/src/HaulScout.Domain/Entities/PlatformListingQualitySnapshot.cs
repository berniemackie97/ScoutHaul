using HaulScout.Domain.Common;

namespace HaulScout.Domain.Entities;

/// <summary>
/// A point-in-time marketplace quality/performance observation for a listing. eBay's Listing Quality report is
/// report-period data, not listing identity, so snapshots are kept historically and linked back to the platform
/// listing when the report exposes a durable item id.
/// </summary>
public sealed class PlatformListingQualitySnapshot : EntityBase, IOrganizationScoped
{
    public Guid OrganizationId { get; set; }
    public Organization? Organization { get; set; }

    public Guid? PlatformListingId { get; set; }
    public PlatformListing? PlatformListing { get; set; }

    public required string Platform { get; set; }
    public required string ExternalListingId { get; set; }
    public required string Title { get; set; }

    public string? CategoryName { get; set; }
    public string? ListingCondition { get; set; }
    public string? PriceRange { get; set; }
    public DateTimeOffset ReportGeneratedAt { get; set; }
    public DateTimeOffset ImportedAt { get; set; } = DateTimeOffset.UtcNow;

    public string? Action { get; set; }
    public decimal? DailyImpressions { get; set; }
    public decimal? ClickThroughRate { get; set; }
    public decimal? SalesConversionRate { get; set; }
    public decimal? PromotedListingsAdRate { get; set; }

    public int? RecommendedItemSpecificsFilled { get; set; }
    public string? RecommendedItemSpecificsToAdd { get; set; }
    public string? Brand { get; set; }
    public string? Mpn { get; set; }
    public string? Upc { get; set; }
    public int? PhotoCount { get; set; }
    public int? TitleKeywordCount { get; set; }

    public bool? FreeShipping { get; set; }
    public int? EstimatedHandlingDays { get; set; }
    public bool? AcceptsReturns { get; set; }
    public bool? SellerPaidReturns { get; set; }
    public bool? PromotedListings { get; set; }
    public bool? BestOffer { get; set; }

    /// <summary>Full normalized row payload for audit and future reprocessing as eBay changes this workbook.</summary>
    public string? RawJson { get; set; }
}
