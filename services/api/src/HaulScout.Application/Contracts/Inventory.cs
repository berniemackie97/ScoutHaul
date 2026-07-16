using HaulScout.Domain;

namespace HaulScout.Application.Contracts;

// Inventory items, product identities, reference menus, acquisition lots, bundles, photos, issues.

public sealed record UpsertProductIdentityRequest(
    string Title,
    string? Brand,
    string? Model,
    string? Category,
    string? Upc,
    string? Ean,
    string? Isbn,
    string? Sku,
    string? SearchQuery,
    string? Notes);

public sealed record ProductIdentityDto(
    Guid Id,
    string Title,
    string? Brand,
    string? Model,
    string? Category,
    string? Upc,
    string? Ean,
    string? Isbn,
    string? Sku,
    string? SearchQuery,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record UpsertInventoryItemRequest(
    Guid? ProductIdentityId,
    Guid? SourcingTripId,
    Guid? SourcingTripStopId,
    Guid? SourceLocationId,
    string Title,
    string? Description,
    string? Category,
    string? Brand,
    string? Model,
    string? Color,
    string? Condition,
    string? SizeSystem,
    string? SizeValue,
    string? AttributesJson,
    string? MeasurementsJson,
    string? StorageLocation,
    string? Accessories,
    string? MissingParts,
    string? TestingNotes,
    string? CleaningNotes,
    string? ListingNotes,
    decimal? SellerAskingPrice,
    decimal? PurchasePrice,
    decimal? EstimatedSalePrice,
    string? Barcode,
    string? Notes,
    InventoryOwnershipType? OwnershipType = null,
    InventoryFunctionalStatus? FunctionalStatus = null,
    InventoryCompletenessStatus? CompletenessStatus = null,
    InventoryAuthenticityStatus? AuthenticityStatus = null,
    InventoryPackagingStatus? PackagingStatus = null,
    string? ManufacturerPartNumber = null,
    string? SerialNumber = null,
    string? CountryOfOrigin = null,
    int? ManufactureYear = null,
    decimal? Length = null,
    decimal? Width = null,
    decimal? Height = null,
    DimensionUnit? DimensionUnit = null,
    decimal? Weight = null,
    WeightUnit? WeightUnit = null,
    InventoryStatus? Status = null);

public sealed record ChangeInventoryStatusRequest(InventoryStatus Status);

public sealed record InventoryReferenceOptionDto(
    string Value,
    string Label,
    string? Group = null,
    string? ParentValue = null,
    bool IsLegacy = false);

public sealed record InventoryReferenceDataDto(
    IReadOnlyList<InventoryReferenceOptionDto> Categories,
    IReadOnlyList<InventoryReferenceOptionDto> Brands,
    IReadOnlyList<InventoryReferenceOptionDto> Models,
    IReadOnlyList<InventoryReferenceOptionDto> Conditions,
    IReadOnlyList<InventoryReferenceOptionDto> Colors,
    IReadOnlyList<InventoryReferenceOptionDto> SizeSystems,
    IReadOnlyList<InventoryReferenceOptionDto> Sizes,
    IReadOnlyList<InventoryReferenceOptionDto> StorageLocations,
    IReadOnlyList<InventoryReferenceOptionDto> Countries);

public sealed record InventoryItemDto(
    Guid Id,
    Guid? ProductIdentityId,
    Guid? SourcingTripId,
    Guid? SourcingTripStopId,
    Guid? SourceLocationId,
    string Title,
    string? Description,
    string? Category,
    string? Brand,
    string? Model,
    string? Color,
    string? Condition,
    string? SizeSystem,
    string? SizeValue,
    string? AttributesJson,
    string? MeasurementsJson,
    string? StorageLocation,
    string? Accessories,
    string? MissingParts,
    string? TestingNotes,
    string? CleaningNotes,
    string? ListingNotes,
    InventoryStatus Status,
    decimal? SellerAskingPrice,
    decimal? PurchasePrice,
    decimal? EstimatedSalePrice,
    string? Barcode,
    string? Notes,
    IReadOnlyList<string> PhotoUrls,
    IReadOnlyList<InventoryPhotoDto> Photos,
    string? HeroPhotoThumbnailUrl,
    bool IsArchived,
    bool IsBundle,
    int BundleComponentCount,
    IReadOnlyList<InventoryBundleComponentDto> BundleComponents,
    Guid? AcquisitionLotId,
    string? AcquisitionLotName,
    decimal? AcquisitionAllocatedCost,
    InventoryLifecycleGroup LifecycleGroup,
    bool IsSold,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    InventoryOwnershipType OwnershipType,
    InventoryFunctionalStatus FunctionalStatus,
    InventoryCompletenessStatus CompletenessStatus,
    InventoryAuthenticityStatus AuthenticityStatus,
    InventoryPackagingStatus PackagingStatus,
    string? ManufacturerPartNumber,
    string? SerialNumber,
    string? CountryOfOrigin,
    int? ManufactureYear,
    decimal? Length,
    decimal? Width,
    decimal? Height,
    DimensionUnit DimensionUnit,
    decimal? Weight,
    WeightUnit WeightUnit);

/// <summary>Aggregate over a filtered inventory set (whole result, not just the page) for the inventory KPIs.</summary>
public sealed record InventorySummaryDto(
    int Total,
    int ActiveItems,
    int ReadyOrListed,
    int Sold,
    decimal CapitalDeployed,
    decimal EstimatedResaleValue);

/// <summary>A searchable, sortable, paged page of inventory plus the summary over the whole filtered set.</summary>
public sealed record InventoryListDto(IReadOnlyList<InventoryItemDto> Items, int Total, int Page, int PageSize, int PageCount, InventorySummaryDto Summary);

public sealed record AcquisitionLotItemInputDto(
    Guid InventoryItemId,
    decimal? ManualAllocatedCost = null,
    decimal? AllocationWeight = null,
    string? Notes = null);

public sealed record UpsertAcquisitionLotRequest(
    string Name,
    DateTimeOffset? PurchasedAt,
    decimal PurchaseSubtotal,
    decimal ShippingCost,
    decimal TaxCost,
    decimal FeeCost,
    decimal OtherCost,
    AcquisitionLotAllocationMethod AllocationMethod,
    string? SupplierName,
    Guid? SourceLocationId,
    Guid? SourcingTripId,
    Guid? SourcingTripStopId,
    string? ExternalReference,
    string? Notes,
    IReadOnlyList<AcquisitionLotItemInputDto> Items);

public sealed record AcquisitionLotItemDto(
    Guid Id,
    Guid InventoryItemId,
    string InventoryItemTitle,
    InventoryStatus InventoryItemStatus,
    decimal AllocatedCost,
    decimal? AllocationWeight,
    string? Notes);

/// <summary>A new inventory item to create directly inside an acquisition lot ("itemize the lot").</summary>
public sealed record NewLotItemDto(
    string Title,
    string? Category = null,
    string? Brand = null,
    string? Condition = null,
    decimal? EstimatedSalePrice = null,
    decimal? ManualAllocatedCost = null,
    decimal? AllocationWeight = null,
    string? StorageLocation = null,
    string? Notes = null);

/// <summary>Creates draft inventory items inside an existing lot, then re-allocates landed cost across all items.</summary>
public sealed record AddLotItemsRequest(IReadOnlyList<NewLotItemDto> Items);

public sealed record AcquisitionLotDto(
    Guid Id,
    string Name,
    string? SupplierName,
    Guid? SourceLocationId,
    Guid? SourcingTripId,
    Guid? SourcingTripStopId,
    DateTimeOffset PurchasedAt,
    decimal PurchaseSubtotal,
    decimal ShippingCost,
    decimal TaxCost,
    decimal FeeCost,
    decimal OtherCost,
    decimal TotalLandedCost,
    decimal TotalAllocatedCost,
    AcquisitionLotAllocationMethod AllocationMethod,
    string? ExternalReference,
    string? Notes,
    IReadOnlyList<AcquisitionLotItemDto> Items,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateInventoryBundleRequest(
    string Title,
    string? Description,
    string? Category,
    decimal? EstimatedSalePrice,
    InventoryStatus? Status,
    IReadOnlyList<Guid> ComponentInventoryItemIds,
    string? Notes);

public sealed record InventoryBundleComponentDto(
    Guid Id,
    Guid BundleInventoryItemId,
    Guid ComponentInventoryItemId,
    string ComponentTitle,
    InventoryStatus ComponentStatus,
    int Quantity,
    decimal CostSnapshot,
    string? Notes);

public sealed record InventoryPhotoDto(
    Guid Id,
    Guid InventoryItemId,
    bool IsHero,
    int SortOrder,
    string OriginalUrl,
    string ThumbnailUrl,
    string ContentType,
    long OriginalByteSize,
    long ThumbnailByteSize,
    int? Width,
    int? Height,
    DateTimeOffset CreatedAt);

public sealed record InventoryPhotoUploadResultDto(
    Guid InventoryItemId,
    IReadOnlyList<string> PhotoUrls,
    string? UploadedUrl,
    IReadOnlyList<InventoryPhotoDto> Photos);

public sealed record UpsertInventoryIssueRequest(
    string IssueType,
    string Severity,
    string? Note,
    Guid? InventoryPhotoId,
    int? SortOrder,
    bool? IsResolved);

public sealed record InventoryIssueDto(
    Guid Id,
    Guid InventoryItemId,
    Guid? InventoryPhotoId,
    string IssueType,
    string Severity,
    string? Note,
    int SortOrder,
    bool IsResolved,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
