using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Domain;
using HaulScout.Domain.Entities;

namespace HaulScout.Application.Inventory;

/// <summary>
/// Moving an item by hand between its own stages. The stages an item reaches as a consequence of something else —
/// bundling, selling, fulfilment — are deliberately refused here: those are owned by the workflows that also do the
/// rest of the work, and letting this back door set them would leave the sale or bundle inconsistent with the item.
/// </summary>
public sealed class InventoryStatusWorkflowService(
    IInventoryStore inventory,
    ICurrentUserContext currentUser,
    TimeProvider timeProvider)
{
    /// <summary>Stages that are a side effect of another workflow, and so cannot be set directly.</summary>
    private static readonly InventoryStatus[] WorkflowOwnedStatuses =
    [
        InventoryStatus.Bundled,
        InventoryStatus.Sold,
        InventoryStatus.Packed,
        InventoryStatus.Shipped,
        InventoryStatus.Delivered,
        InventoryStatus.Returned,
    ];

    public async Task<AppResult<InventoryItemDto>> ChangeStatusAsync(
        Guid id,
        ChangeInventoryStatusRequest request,
        CancellationToken cancellationToken)
    {
        Guid organizationId = currentUser.OrganizationId;

        InventoryItem? item = await inventory.FindDetailAsync(organizationId, id, cancellationToken);
        if (item is null)
        {
            return AppResult<InventoryItemDto>.NotFound();
        }

        if (await inventory.HasActiveSaleAsync(organizationId, id, cancellationToken))
        {
            return AppResult<InventoryItemDto>.Conflict("This item has an active sale. Change its stage through the sales workflow.");
        }

        if (WorkflowOwnedStatuses.Contains(request.Status))
        {
            return AppResult<InventoryItemDto>.Conflict("Bundle, sale, and fulfillment stages must be changed through their dedicated workflows.");
        }

        // Listed is public: the moment it is set, the item is on the storefront shelf. An operator flipping the
        // switch by hand must therefore have the item shopper-ready — the same gate the admin publish action uses.
        // (Marketplace imports do not pass through here; an imported photo-less item lands as awaiting-photos.)
        if (request.Status == InventoryStatus.Listed
            && StorePublishing.ReasonsItCannotBeListed(item) is { Count: > 0 } reasons)
        {
            return AppResult<InventoryItemDto>.Validation(
                $"This item is not ready to be listed: {string.Join(" ", reasons)}");
        }

        item.Status = request.Status;
        DateTimeOffset now = timeProvider.GetUtcNow();
        item.PurchasedAt ??= request.Status == InventoryStatus.Purchased ? now : null;
        item.ListedAt ??= request.Status == InventoryStatus.Listed ? now : null;

        await inventory.SaveChangesAsync(cancellationToken);
        return AppResult<InventoryItemDto>.Ok(item.ToDto());
    }

    /// <summary>Sets what the item is expected to sell for, without touching anything else on it.</summary>
    public async Task<AppResult<InventoryItemDto>> SetExpectedPriceAsync(
        Guid id,
        SetExpectedPriceRequest request,
        CancellationToken cancellationToken)
    {
        if (request.EstimatedSalePrice is < 0 or > 1_000_000)
        {
            return AppResult<InventoryItemDto>.Validation("Give a price between $0 and $1,000,000.");
        }

        InventoryItem? item = await inventory.FindDetailAsync(currentUser.OrganizationId, id, cancellationToken);
        if (item is null)
        {
            return AppResult<InventoryItemDto>.NotFound();
        }

        item.EstimatedSalePrice = decimal.Round(request.EstimatedSalePrice, 2, MidpointRounding.AwayFromZero);
        await inventory.SaveChangesAsync(cancellationToken);
        return AppResult<InventoryItemDto>.Ok(item.ToDto());
    }

    /// <summary>Most items moved in one request; a shelf, not the warehouse.</summary>
    public const int MaxBulkItems = 200;

    public async Task<AppResult<BulkInventoryUpdateResultDto>> SetLocationAsync(
        SetInventoryLocationRequest request,
        CancellationToken cancellationToken)
    {
        Guid[] ids = (request.InventoryItemIds ?? []).Distinct().ToArray();
        if (ids.Length == 0)
        {
            return AppResult<BulkInventoryUpdateResultDto>.Validation("Select at least one item.");
        }

        if (ids.Length > MaxBulkItems)
        {
            return AppResult<BulkInventoryUpdateResultDto>.Validation($"Move at most {MaxBulkItems} items at a time.");
        }

        string? location = string.IsNullOrWhiteSpace(request.StorageLocation) ? null : request.StorageLocation.Trim();
        if (location?.Length > InventoryItem.StorageLocationLimit)
        {
            return AppResult<BulkInventoryUpdateResultDto>.Validation($"Keep the location under {InventoryItem.StorageLocationLimit} characters.");
        }

        IReadOnlyList<InventoryItem> items = await inventory.FindManyAsync(currentUser.OrganizationId, ids, cancellationToken);
        foreach (InventoryItem item in items)
        {
            item.StorageLocation = location;
        }

        await inventory.SaveChangesAsync(cancellationToken);
        return AppResult<BulkInventoryUpdateResultDto>.Ok(new BulkInventoryUpdateResultDto(items.Count, ids.Length - items.Count));
    }
}
