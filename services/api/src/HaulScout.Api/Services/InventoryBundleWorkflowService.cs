using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using static HaulScout.Api.Helpers.StringHelpers;
using HaulScout.Api.Mapping;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Application.Services;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;
using HaulScout.Application.Inventory;

namespace HaulScout.Api.Services;

/// <summary>
/// Owns inventory bundle creation and its component lifecycle side effects.
/// Controllers keep HTTP shape; this service keeps bundle write rules in one place.
/// </summary>
public sealed class InventoryBundleWorkflowService(HaulScoutDbContext db, ICurrentUserContext currentUser)
{
    private static readonly SaleStatus[] ReleasedSaleStatuses = [SaleStatus.Returned, SaleStatus.Refunded, SaleStatus.Cancelled];

    public async Task<AppResult<InventoryItemDto>> CreateAsync(
        CreateInventoryBundleRequest request,
        CancellationToken cancellationToken)
    {
        string? validation = Validate(request);
        if (validation is not null)
        {
            return AppResult<InventoryItemDto>.Validation(validation);
        }

        Guid[] componentIds = request.ComponentInventoryItemIds.Distinct().ToArray();
        List<InventoryItem> components = await db.InventoryItems
            .Include(item => item.Sales)
            .Where(item => componentIds.Contains(item.Id) && item.OrganizationId == currentUser.OrganizationId)
            .ToListAsync(cancellationToken);

        if (components.Count != componentIds.Length)
        {
            return AppResult<InventoryItemDto>.NotFound("One or more bundle components were not found.");
        }

        string[] blocked = components
            .Where(IsUnavailableForBundle)
            .Select(item => item.Title)
            .ToArray();
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
            OrganizationId = currentUser.OrganizationId,
            Title = request.Title.Trim(),
            Description = TrimToNull(request.Description),
            Category = TrimToNull(request.Category) ?? components.Select(item => item.Category).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
            EstimatedSalePrice = request.EstimatedSalePrice,
            PurchasePrice = components.Sum(item => item.PurchasePrice ?? 0),
            Status = bundleStatus,
            IsBundle = true,
            PurchasedAt = components
                .Where(item => item.PurchasedAt.HasValue)
                .Select(item => item.PurchasedAt)
                .OrderBy(item => item)
                .FirstOrDefault(),
            Notes = TrimToNull(request.Notes)
        };

        db.InventoryItems.Add(bundle);
        foreach (InventoryItem component in components.OrderBy(item => item.Title))
        {
            component.Status = InventoryStatus.Bundled;
            db.InventoryBundleComponents.Add(new InventoryBundleComponent
            {
                OrganizationId = currentUser.OrganizationId,
                BundleInventoryItem = bundle,
                ComponentInventoryItemId = component.Id,
                Quantity = Math.Max(1, component.Quantity),
                CostSnapshot = component.PurchasePrice ?? 0,
                TitleSnapshot = component.Title,
            });
        }

        db.AuditLogs.Add(new AuditLog
        {
            OrganizationId = currentUser.OrganizationId,
            ActorUserId = currentUser.UserId,
            EntityName = nameof(InventoryItem),
            Action = "BundleCreated",
            DetailsJson = JsonSerializer.Serialize(new
            {
                BundleTitle = bundle.Title,
                ComponentInventoryItemIds = componentIds,
                ComponentCount = componentIds.Length,
            })
        });

        await db.SaveChangesAsync(cancellationToken);
        await db.Entry(bundle).Collection(item => item.BundleComponents).LoadAsync(cancellationToken);
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

    private static bool IsUnavailableForBundle(InventoryItem item) =>
        item.IsArchived
        || item.IsBundle
        || item.Status is InventoryStatus.Bundled
            or InventoryStatus.Sold
            or InventoryStatus.Packed
            or InventoryStatus.Shipped
            or InventoryStatus.Delivered
            or InventoryStatus.Returned
            or InventoryStatus.Donated
            or InventoryStatus.Disposed
        || item.Sales.Any(sale => !ReleasedSaleStatuses.Contains(sale.Status));

    private static bool IsInvalidNewBundleStatus(InventoryStatus status) =>
        status is InventoryStatus.Bundled
            or InventoryStatus.Sold
            or InventoryStatus.Packed
            or InventoryStatus.Shipped
            or InventoryStatus.Delivered
            or InventoryStatus.Returned
            or InventoryStatus.Donated
            or InventoryStatus.Disposed;
}
