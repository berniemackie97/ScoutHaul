using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using static HaulScout.Application.Common.Text;

namespace HaulScout.Application.Inventory;

/// <summary>
/// Combining several items into one sellable bundle. The components do not disappear — they become Bundled, keeping
/// their own rows and cost, and the bundle takes their combined cost as its basis so profit stays attributable.
/// </summary>
public sealed class InventoryBundleWorkflowService(
    IInventoryStore inventory,
    IAuditTrail audit,
    ICurrentUserContext currentUser)
{
    private static readonly SaleStatus[] ReleasedSaleStatuses = [SaleStatus.Returned, SaleStatus.Refunded, SaleStatus.Cancelled];

    /// <summary>Stages in which an item is spoken for and cannot be folded into a bundle.</summary>
    private static readonly InventoryStatus[] UnavailableStatuses =
    [
        InventoryStatus.Bundled,
        InventoryStatus.Sold,
        InventoryStatus.Packed,
        InventoryStatus.Shipped,
        InventoryStatus.Delivered,
        InventoryStatus.Returned,
        InventoryStatus.Donated,
        InventoryStatus.Disposed,
    ];

    public async Task<AppResult<InventoryItemDto>> CreateAsync(
        CreateInventoryBundleRequest request,
        CancellationToken cancellationToken)
    {
        if (Validate(request) is string validation)
        {
            return AppResult<InventoryItemDto>.Validation(validation);
        }

        Guid organizationId = currentUser.OrganizationId;
        Guid[] componentIds = request.ComponentInventoryItemIds.Distinct().ToArray();
        IReadOnlyList<InventoryItem> components = await inventory.ListBundleCandidatesAsync(
            organizationId, componentIds, cancellationToken);

        if (components.Count != componentIds.Length)
        {
            return AppResult<InventoryItemDto>.NotFound("One or more bundle components were not found.");
        }

        string[] blocked = components.Where(IsUnavailableForBundle).Select(item => item.Title).ToArray();
        if (blocked.Length > 0)
        {
            return AppResult<InventoryItemDto>.Conflict($"These items cannot be bundled because they are unavailable, sold, archived, or already bundled: {string.Join(", ", blocked)}.");
        }

        InventoryStatus bundleStatus = request.Status ?? InventoryStatus.ReadyToList;
        if (IsInvalidNewBundleStatus(bundleStatus))
        {
            return AppResult<InventoryItemDto>.Validation("A new bundle must start in an unsold listing-prep stage.");
        }

        InventoryItem bundle = new()
        {
            OrganizationId = organizationId,
            Title = request.Title.Trim(),
            Description = TrimToNull(request.Description),
            Category = TrimToNull(request.Category) ?? components.Select(item => item.Category).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
            EstimatedSalePrice = request.EstimatedSalePrice,
            PurchasePrice = components.Sum(item => item.PurchasePrice ?? 0),
            Status = bundleStatus,
            IsBundle = true,
            // The bundle is as old as its oldest component — it was not acquired today.
            PurchasedAt = components
                .Where(item => item.PurchasedAt.HasValue)
                .Select(item => item.PurchasedAt)
                .OrderBy(item => item)
                .FirstOrDefault(),
            Notes = TrimToNull(request.Notes),
        };

        await inventory.AddAsync(bundle, cancellationToken);
        foreach (InventoryItem component in components.OrderBy(item => item.Title))
        {
            component.Status = InventoryStatus.Bundled;
            // Cost and title are snapshotted: the bundle's basis must not drift if a component is later edited.
            await inventory.AddBundleComponentAsync(
                new InventoryBundleComponent
                {
                    OrganizationId = organizationId,
                    BundleInventoryItem = bundle,
                    ComponentInventoryItemId = component.Id,
                    Quantity = Math.Max(1, component.Quantity),
                    CostSnapshot = component.PurchasePrice ?? 0,
                    TitleSnapshot = component.Title,
                },
                cancellationToken);
        }

        await audit.RecordAsync(
            nameof(InventoryItem),
            "BundleCreated",
            new
            {
                BundleTitle = bundle.Title,
                ComponentInventoryItemIds = componentIds,
                ComponentCount = componentIds.Length,
            },
            cancellationToken);

        await inventory.SaveChangesAsync(cancellationToken);
        await inventory.LoadBundleComponentsAsync(bundle, cancellationToken);
        return AppResult<InventoryItemDto>.Ok(bundle.ToDto());
    }

    private static string? Validate(CreateInventoryBundleRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return "Bundle title is required.";
        }

        if (request.EstimatedSalePrice is < 0)
        {
            return "Estimated sale price cannot be negative.";
        }

        if (request.ComponentInventoryItemIds is null || request.ComponentInventoryItemIds.Distinct().Count() < 2)
        {
            return "Select at least two distinct inventory items to build a bundle.";
        }

        return null;
    }

    /// <summary>An item is unavailable if it is gone, already in a bundle, or still held by a live sale.</summary>
    private static bool IsUnavailableForBundle(InventoryItem item) =>
        item.IsArchived
        || item.IsBundle
        || UnavailableStatuses.Contains(item.Status)
        || item.Sales.Any(sale => !ReleasedSaleStatuses.Contains(sale.Status));

    /// <summary>A bundle cannot be born already sold or disposed of — those stages are reached by doing the work.</summary>
    private static bool IsInvalidNewBundleStatus(InventoryStatus status) => UnavailableStatuses.Contains(status);
}
