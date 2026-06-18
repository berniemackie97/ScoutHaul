using HaulScout.Domain.Common;

namespace HaulScout.Domain.Entities;

public sealed class MarketObservation : EntityBase, IOrganizationScoped
{
    public Guid OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public Guid? ProductIdentityId { get; set; }
    public ProductIdentity? ProductIdentity { get; set; }
    public Guid? InventoryItemId { get; set; }
    public InventoryItem? InventoryItem { get; set; }
    public MarketObservationType ObservationType { get; set; }
    public required string Source { get; set; }
    public string? SourceUrl { get; set; }
    public required string Title { get; set; }
    public decimal Price { get; set; }
    public decimal? ShippingPrice { get; set; }
    public decimal TotalPrice { get; set; }
    public string? Currency { get; set; } = "USD";
    public string? Condition { get; set; }
    public DateTimeOffset ObservedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SoldAt { get; set; }
    public string? Notes { get; set; }
    public bool IsArchived { get; set; }
}

public sealed class MarketAnalysis : EntityBase, IOrganizationScoped
{
    public Guid OrganizationId { get; set; }
    public Guid? ProductIdentityId { get; set; }
    public Guid? InventoryItemId { get; set; }
    public decimal? EstimatedSalePrice { get; set; }
    public PricingBasisType PricingBasis { get; set; }
    public int ConfidenceScore { get; set; }
    public int SoldCompCount { get; set; }
    public int ActiveListingCount { get; set; }
    public string WarningsJson { get; set; } = "[]";
    public string ReasonsJson { get; set; } = "[]";
}

public sealed class BuyRecommendation : EntityBase, IOrganizationScoped
{
    public Guid OrganizationId { get; set; }
    public Guid? MarketAnalysisId { get; set; }
    public MarketAnalysis? MarketAnalysis { get; set; }
    public Guid? ProductIdentityId { get; set; }
    public Guid? InventoryItemId { get; set; }
    public PricingBasisType PricingBasis { get; set; }
    public RecommendationDecisionType Decision { get; set; }
    public decimal? RecommendedMaxBuyPrice { get; set; }
    public decimal? ExpectedNetProfit { get; set; }
    public decimal? RoiPercent { get; set; }
    public decimal? MarginPercent { get; set; }
    public decimal? BreakEvenPrice { get; set; }
    public string WarningsJson { get; set; } = "[]";
}
