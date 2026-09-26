using System.Text.Json;
using HaulScout.Application.Contracts;
using HaulScout.Domain;
using HaulScout.Domain.Entities;

namespace HaulScout.Application.Inventory;

/// <summary>
/// Inventory's slice of the DTO mapping, carved out of the Api-layer DtoMapping god-file so the inventory use cases
/// can live in the Application layer without reaching back into the web assembly. The mapping never touched EF — it
/// was only ever in the wrong project.
/// </summary>
public static class InventoryMapping
{
    public static InventoryItemDto ToDto(this InventoryItem item) => new(
        item.Id,
        item.ProductIdentityId,
        item.SourcingTripId,
        item.SourcingTripStopId,
            item.SourceLocationId,
            item.Title,
            item.Description,
            item.Category,
            item.Brand,
            item.Model,
            item.Color,
            item.Condition,
            item.SizeSystem,
            item.SizeValue,
            item.AttributesJson,
            item.MeasurementsJson,
            item.StorageLocation,
            item.Accessories,
            item.MissingParts,
            item.TestingNotes,
            item.CleaningNotes,
            item.ListingNotes,
            item.Status,
            item.SellerAskingPrice,
            item.PurchasePrice,
            item.EstimatedSalePrice,
            item.Barcode,
            item.Notes,
            ParsePhotoUrls(item.PhotoLocalReferencesJson),
            item.Photos
                .OrderByDescending(photo => photo.IsHero)
                .ThenBy(photo => photo.SortOrder)
                .ThenBy(photo => photo.CreatedAt)
                .Select(photo => photo.ToDto())
                .ToArray(),
            item.Photos
                .OrderByDescending(photo => photo.IsHero)
                .ThenBy(photo => photo.SortOrder)
                .ThenBy(photo => photo.CreatedAt)
                .Select(photo => $"/api/inventory/{item.Id}/photos/{photo.Id}/thumbnail")
                .FirstOrDefault(),
            item.IsArchived,
            item.IsBundle,
            item.BundleComponents.Count,
            item.BundleComponents
                .OrderBy(component => component.ComponentInventoryItem != null ? component.ComponentInventoryItem.Title : component.TitleSnapshot)
                .Select(component => component.ToDto())
                .ToArray(),
            item.AcquisitionLotItems
                .OrderByDescending(lotItem => lotItem.UpdatedAt)
                .Select(lotItem => (Guid?)lotItem.AcquisitionLotId)
                .FirstOrDefault(),
            item.AcquisitionLotItems
                .OrderByDescending(lotItem => lotItem.UpdatedAt)
                .Select(lotItem => lotItem.AcquisitionLot != null ? lotItem.AcquisitionLot.Name : null)
                .FirstOrDefault(),
            item.AcquisitionLotItems
                .OrderByDescending(lotItem => lotItem.UpdatedAt)
                .Select(lotItem => (decimal?)lotItem.AllocatedCost)
                .FirstOrDefault(),
            InventoryLifecycle.Group(item.Status),
            InventoryLifecycle.IsSold(item.Status),
            item.CreatedAt,
            item.UpdatedAt,
            item.OwnershipType,
            item.FunctionalStatus,
            item.CompletenessStatus,
            item.AuthenticityStatus,
            item.PackagingStatus,
            item.ManufacturerPartNumber,
            item.SerialNumber,
            item.CountryOfOrigin,
            item.ManufactureYear,
            item.Length,
            item.Width,
            item.Height,
            item.DimensionUnit,
            item.Weight,
            item.WeightUnit);

    public static AcquisitionLotDto ToDto(this AcquisitionLot item) => new(
        item.Id,
        item.Name,
        item.SupplierName,
        item.SourceLocationId,
        item.SourcingTripId,
        item.SourcingTripStopId,
        item.PurchasedAt,
        item.PurchaseSubtotal,
        item.ShippingCost,
        item.TaxCost,
        item.FeeCost,
        item.OtherCost,
        item.TotalLandedCost,
        item.Items.Sum(lotItem => lotItem.AllocatedCost),
        item.AllocationMethod,
        item.ExternalReference,
        item.Notes,
        item.Items
            .OrderBy(lotItem => lotItem.InventoryItem!.Title)
            .Select(lotItem => lotItem.ToDto())
            .ToArray(),
        item.CreatedAt,
        item.UpdatedAt,
        item.Version);

    public static AcquisitionLotItemDto ToDto(this AcquisitionLotItem item) => new(
        item.Id,
        item.InventoryItemId,
        item.InventoryItem?.Title ?? string.Empty,
        item.InventoryItem?.Status ?? InventoryStatus.Draft,
        item.AllocatedCost,
        item.AllocationWeight,
        item.Notes);

    public static InventoryBundleComponentDto ToDto(this InventoryBundleComponent item) => new(
        item.Id,
        item.BundleInventoryItemId,
        item.ComponentInventoryItemId,
        item.ComponentInventoryItem?.Title ?? item.TitleSnapshot,
        item.ComponentInventoryItem?.Status ?? InventoryStatus.Draft,
        item.Quantity,
        item.CostSnapshot,
        item.Notes);

    public static InventoryPhotoDto ToDto(this InventoryPhoto item) => new(
        item.Id,
        item.InventoryItemId,
        item.IsHero,
        item.SortOrder,
        $"/api/inventory/{item.InventoryItemId}/photos/{item.Id}/original",
        $"/api/inventory/{item.InventoryItemId}/photos/{item.Id}/thumbnail",
        item.ContentType,
        item.OriginalByteSize,
        item.ThumbnailByteSize,
        item.Width,
        item.Height,
        item.CreatedAt);

    public static InventoryIssueDto ToDto(this InventoryIssue item) => new(
        item.Id,
        item.InventoryItemId,
        item.InventoryPhotoId,
        item.IssueType,
        item.Severity,
        item.Note,
        item.SortOrder,
        item.IsResolved,
        item.CreatedAt,
        item.UpdatedAt);

    private static IReadOnlyList<string> ParsePhotoUrls(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(value) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
