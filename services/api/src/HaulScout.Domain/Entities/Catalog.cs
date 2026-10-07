using HaulScout.Domain.Common;

namespace HaulScout.Domain.Entities;

public sealed class ProductIdentity : EntityBase, IOrganizationScoped
{
    public Guid OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public required string Title { get; set; }
    public string? Brand { get; set; }
    public string? Model { get; set; }
    public string? Category { get; set; }
    public string? Upc { get; set; }
    public string? Ean { get; set; }
    public string? Isbn { get; set; }
    public string? Sku { get; set; }
    public string? SearchQuery { get; set; }
    public string? Notes { get; set; }

    /// <summary>
    /// Tracks where this product data originated.
    /// "user" = manually entered/corrected by a user (trusted).
    /// "external" = auto-saved from an external barcode API (may be wrong).
    /// </summary>
    public string DataSource { get; set; } = "user";
    public ICollection<InventoryItem> InventoryItems { get; set; } = [];
    public ICollection<MarketObservation> MarketObservations { get; set; } = [];
}

public sealed class SourceLocation : EntityBase, IOrganizationScoped
{
    public Guid OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public required string Name { get; set; }
    public string? LocationType { get; set; }
    public string? AddressLine1 { get; set; }
    public string? City { get; set; }
    public string? Region { get; set; }
    public string? PostalCode { get; set; }
    public string? Notes { get; set; }
}

public sealed class InventoryItem : EntityBase, IOrganizationScoped
{
    public Guid OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public Guid? ProductIdentityId { get; set; }
    public ProductIdentity? ProductIdentity { get; set; }
    public Guid? SourceLocationId { get; set; }
    public SourceLocation? SourceLocation { get; set; }
    public Guid? SourcingTripId { get; set; }
    public SourcingTrip? SourcingTrip { get; set; }
    public Guid? SourcingTripStopId { get; set; }
    public SourcingTripStop? SourcingTripStop { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public string? Category { get; set; }
    public string? Brand { get; set; }
    public string? Model { get; set; }

    /// <summary>Its colors from <see cref="ItemColors"/>, the main one first; at most <see cref="ItemColors.Max"/>.</summary>
    public List<string> Colors { get; set; } = [];

    public string? Condition { get; set; }
    public InventoryOwnershipType OwnershipType { get; set; } = InventoryOwnershipType.Owned;
    public InventoryFunctionalStatus FunctionalStatus { get; set; } = InventoryFunctionalStatus.Unknown;
    public InventoryCompletenessStatus CompletenessStatus { get; set; } = InventoryCompletenessStatus.Unknown;
    public InventoryAuthenticityStatus AuthenticityStatus { get; set; } = InventoryAuthenticityStatus.NotAssessed;
    public InventoryPackagingStatus PackagingStatus { get; set; } = InventoryPackagingStatus.NotApplicable;
    public string? SizeSystem { get; set; }
    public string? SizeValue { get; set; }
    public string? ManufacturerPartNumber { get; set; }
    public string? SerialNumber { get; set; }
    public string? CountryOfOrigin { get; set; }
    public int? ManufactureYear { get; set; }
    public decimal? Length { get; set; }
    public decimal? Width { get; set; }
    public decimal? Height { get; set; }
    public DimensionUnit DimensionUnit { get; set; } = DimensionUnit.Inches;
    public decimal? Weight { get; set; }
    public WeightUnit WeightUnit { get; set; } = WeightUnit.Ounces;
    public string? AttributesJson { get; set; }
    public string? MeasurementsJson { get; set; }
    public const int StorageLocationLimit = 160;

    public string? StorageLocation { get; set; }
    public string? Accessories { get; set; }
    public string? MissingParts { get; set; }
    public string? TestingNotes { get; set; }
    public string? CleaningNotes { get; set; }
    public string? ListingNotes { get; set; }
    public InventoryStatus Status { get; set; } = InventoryStatus.Draft;
    public decimal? SellerAskingPrice { get; set; }
    public decimal? PurchasePrice { get; set; }
    public decimal? EstimatedSalePrice { get; set; }
    public string? Notes { get; set; }
    public string? Barcode { get; set; }
    public string? PhotoLocalReferencesJson { get; set; }

    /// <summary>
    /// Which listing's photos this item is currently showing, as "platform:listing id:key". Only set while the
    /// photos came from a marketplace listing; photos someone uploaded leave it null.
    /// </summary>
    public string? PhotoSourceKey { get; set; }
    public bool IsArchived { get; set; }
    public bool IsBundle { get; set; }
    public DateTimeOffset? PurchasedAt { get; set; }
    public DateTimeOffset? ListedAt { get; set; }

    /// <summary>
    /// Records that this item is now live for sale somewhere.
    ///
    /// Three paths put an item on sale — a manual listing, the storefront, and a marketplace channel —
    /// and they each decided for themselves what that meant for the item's own status. The channel path
    /// decided nothing at all, so an item live on eBay still read as "ready to list" and kept coming back
    /// round the listing queue. This is the one answer all three ask for.
    /// </summary>
    public void MarkListed(DateTimeOffset now)
    {
        if (!InventoryLifecycle.ShouldAdvanceToListed(Status))
        {
            return;
        }

        Status = InventoryStatus.Listed;
        ListedAt ??= now;
    }
    public int Quantity { get; set; } = 1;
    public DateTimeOffset? SoldAt { get; set; }
    public ICollection<MarketObservation> MarketObservations { get; set; } = [];
    public ICollection<InventoryPhoto> Photos { get; set; } = [];
    public ICollection<InventoryIssue> Issues { get; set; } = [];
    public ICollection<Listing> Listings { get; set; } = [];
    public ICollection<Sale> Sales { get; set; } = [];
    public ICollection<AcquisitionLotItem> AcquisitionLotItems { get; set; } = [];
    public ICollection<InventoryBundleComponent> BundleComponents { get; set; } = [];
    public ICollection<InventoryBundleComponent> ComponentOfBundles { get; set; } = [];
    public ICollection<ChannelListing> ChannelListings { get; set; } = [];
    public StoreListing? StoreListing { get; set; }
}

public sealed class AcquisitionLot : EntityBase, IOrganizationScoped
{
    public Guid OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public required string Name { get; set; }
    public string? SupplierName { get; set; }
    public Guid? SourceLocationId { get; set; }
    public SourceLocation? SourceLocation { get; set; }
    public Guid? SourcingTripId { get; set; }
    public SourcingTrip? SourcingTrip { get; set; }
    public Guid? SourcingTripStopId { get; set; }
    public SourcingTripStop? SourcingTripStop { get; set; }
    public DateTimeOffset PurchasedAt { get; set; } = DateTimeOffset.UtcNow;
    public decimal PurchaseSubtotal { get; set; }
    public decimal ShippingCost { get; set; }
    public decimal TaxCost { get; set; }
    public decimal FeeCost { get; set; }
    public decimal OtherCost { get; set; }
    public AcquisitionLotAllocationMethod AllocationMethod { get; set; } = AcquisitionLotAllocationMethod.Equal;
    public string? ExternalReference { get; set; }
    public string? Notes { get; set; }
    public ICollection<AcquisitionLotItem> Items { get; set; } = [];

    /// <summary>Replaced on every change to the lot or its items, so a write made from an older copy is refused.</summary>
    public Guid Version { get; private set; } = Guid.NewGuid();

    public decimal TotalLandedCost => PurchaseSubtotal + ShippingCost + TaxCost + FeeCost + OtherCost;

    public void MarkChanged() => Version = Guid.NewGuid();
}

public sealed class AcquisitionLotItem : EntityBase, IOrganizationScoped
{
    public Guid OrganizationId { get; set; }
    public Guid AcquisitionLotId { get; set; }
    public AcquisitionLot? AcquisitionLot { get; set; }
    public Guid InventoryItemId { get; set; }
    public InventoryItem? InventoryItem { get; set; }
    public decimal AllocatedCost { get; set; }
    public decimal? AllocationWeight { get; set; }
    public string? Notes { get; set; }
}

public sealed class InventoryBundleComponent : EntityBase, IOrganizationScoped
{
    public Guid OrganizationId { get; set; }
    public Guid BundleInventoryItemId { get; set; }
    public InventoryItem? BundleInventoryItem { get; set; }
    public Guid ComponentInventoryItemId { get; set; }
    public InventoryItem? ComponentInventoryItem { get; set; }
    public int Quantity { get; set; } = 1;
    public decimal CostSnapshot { get; set; }
    public required string TitleSnapshot { get; set; }
    public string? Notes { get; set; }
}

public sealed class InventoryPhoto : EntityBase, IOrganizationScoped
{
    public Guid OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public Guid InventoryItemId { get; set; }
    public InventoryItem? InventoryItem { get; set; }
    public bool IsHero { get; set; }
    public int SortOrder { get; set; }

    /// <summary>
    /// The marketplace address this photo was fetched from, when it came from a listing rather than from someone
    /// uploading it. It is how a photo already held is recognised, so a listing read again keeps the same photo —
    /// and the same address on the storefront — instead of replacing it with a copy.
    /// </summary>
    public string? SourceUrl { get; set; }
    public string? OriginalFileName { get; set; }
    public required string ContentType { get; set; }

    /// <summary>
    /// The image itself, held in the database. It is null once the photo lives in object storage, which is where
    /// photos are kept when one is configured: image bytes are by far the largest thing this schema stores, and
    /// every one of them would otherwise be carried by every backup, restore and write-ahead log record.
    /// </summary>
    public byte[]? OriginalBytes { get; set; }
    public byte[]? ThumbnailBytes { get; set; }

    /// <summary>
    /// Where the image lives in object storage, or null while it is still held in the database. Exactly one of the
    /// two is set for each of the original and the thumbnail.
    /// </summary>
    public string? OriginalObjectKey { get; set; }
    public string? ThumbnailObjectKey { get; set; }
    public long OriginalByteSize { get; set; }
    public long ThumbnailByteSize { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
}

public sealed class InventoryIssue : EntityBase, IOrganizationScoped
{
    public Guid OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public Guid InventoryItemId { get; set; }
    public InventoryItem? InventoryItem { get; set; }
    public Guid? InventoryPhotoId { get; set; }
    public InventoryPhoto? InventoryPhoto { get; set; }
    public required string IssueType { get; set; }
    public required string Severity { get; set; }
    public string? Note { get; set; }
    public int SortOrder { get; set; }
    public bool IsResolved { get; set; }
}
