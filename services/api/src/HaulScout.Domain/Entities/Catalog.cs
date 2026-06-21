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
    public string? Color { get; set; }
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
    public bool IsArchived { get; set; }
    public DateTimeOffset? PurchasedAt { get; set; }
    public DateTimeOffset? ListedAt { get; set; }
    public int Quantity { get; set; } = 1;
    public DateTimeOffset? SoldAt { get; set; }
    public ICollection<MarketObservation> MarketObservations { get; set; } = [];
    public ICollection<InventoryPhoto> Photos { get; set; } = [];
    public ICollection<InventoryIssue> Issues { get; set; } = [];
    public ICollection<Listing> Listings { get; set; } = [];
    public ICollection<Sale> Sales { get; set; } = [];
    public StoreListing? StoreListing { get; set; }
}

public sealed class InventoryPhoto : EntityBase, IOrganizationScoped
{
    public Guid OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public Guid InventoryItemId { get; set; }
    public InventoryItem? InventoryItem { get; set; }
    public bool IsHero { get; set; }
    public int SortOrder { get; set; }
    public string? OriginalFileName { get; set; }
    public required string ContentType { get; set; }
    public required byte[] OriginalBytes { get; set; }
    public required byte[] ThumbnailBytes { get; set; }
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
