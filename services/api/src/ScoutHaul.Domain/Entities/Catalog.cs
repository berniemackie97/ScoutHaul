using ScoutHaul.Domain.Common;

namespace ScoutHaul.Domain.Entities;

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
    public required string Title { get; set; }
    public string? Description { get; set; }
    public string? Condition { get; set; }
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
    public DateTimeOffset? SoldAt { get; set; }
    public ICollection<MarketObservation> MarketObservations { get; set; } = [];
    public ICollection<Listing> Listings { get; set; } = [];
    public ICollection<Sale> Sales { get; set; } = [];
}
