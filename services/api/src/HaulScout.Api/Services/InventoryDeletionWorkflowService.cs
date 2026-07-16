using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using HaulScout.Application.Common;
using HaulScout.Application.Security;
using HaulScout.Application.Services;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Api.Services;

/// <summary>
/// Owns inventory archive/delete rules and cleanup side effects.
/// The controller owns HTTP; this service owns deletion safety and graph mutation.
/// </summary>
public sealed class InventoryDeletionWorkflowService(HaulScoutDbContext db, ICurrentUserContext currentUser)
{
    public async Task<AppResult> DeleteAsync(Guid id, bool hard, CancellationToken cancellationToken)
    {
        InventoryItem? item = await db.InventoryItems
            .Include(inventoryItem => inventoryItem.BundleComponents)
                .ThenInclude(component => component.ComponentInventoryItem)
            .SingleOrDefaultAsync(
                inventoryItem => inventoryItem.Id == id && inventoryItem.OrganizationId == currentUser.OrganizationId,
                cancellationToken);
        if (item is null)
        {
            return AppResult.NotFound();
        }

        string? deleteBlockReason = await ValidateDeleteOrArchiveAsync(item, cancellationToken);
        if (deleteBlockReason is not null)
        {
            return AppResult.Conflict(deleteBlockReason);
        }

        if (hard)
        {
            string? blockReason = await ValidateHardDeleteAsync(item, cancellationToken);
            if (blockReason is not null)
            {
                return AppResult.Conflict(blockReason);
            }

            await RemoveInventoryItemGraphAsync(item, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return AppResult.Ok();
        }

        item.IsArchived = true;
        ReleaseBundleComponents(item);

        db.AuditLogs.Add(new AuditLog
        {
            OrganizationId = currentUser.OrganizationId,
            ActorUserId = currentUser.UserId,
            EntityName = nameof(InventoryItem),
            EntityId = item.Id,
            Action = "InventoryArchived",
            DetailsJson = JsonSerializer.Serialize(new { item.Title, item.Status, item.IsBundle })
        });

        await db.SaveChangesAsync(cancellationToken);
        return AppResult.Ok();
    }

    private async Task<string?> ValidateDeleteOrArchiveAsync(InventoryItem item, CancellationToken cancellationToken)
    {
        if (item.Status == InventoryStatus.Listed)
        {
            return "This inventory item is marked Listed. Delist it from every channel and move it out of the Listed stage before deleting or archiving it.";
        }

        bool hasActiveSale = await db.Sales.AnyAsync(
            sale => sale.InventoryItemId == item.Id
                && sale.OrganizationId == currentUser.OrganizationId
                && !SaleWorkflow.InventoryReleaseStatuses.Contains(sale.Status),
            cancellationToken);
        if (hasActiveSale)
        {
            return "This inventory item has an active sale. Complete, cancel, refund, or return the sale workflow before deleting or archiving it.";
        }

        bool isBundleComponent = await db.InventoryBundleComponents.AnyAsync(
            component => component.ComponentInventoryItemId == item.Id && component.OrganizationId == currentUser.OrganizationId,
            cancellationToken);
        if (isBundleComponent)
        {
            return "This item is inside a bundle. Delete or edit the bundle before deleting or archiving the component item.";
        }

        bool hasLiveChannelListing = await db.ChannelListings.AnyAsync(
            listing => listing.InventoryItemId == item.Id
                && listing.OrganizationId == currentUser.OrganizationId
                && (listing.Status == ChannelListingStatus.Active || listing.Status == ChannelListingStatus.PendingDelist),
            cancellationToken);
        if (hasLiveChannelListing)
        {
            return "This inventory item still has live or pending-delist channel listings. Delist every channel before deleting or archiving it.";
        }

        bool hasActiveListing = await db.Listings.AnyAsync(
            listing => listing.InventoryItemId == item.Id
                && listing.OrganizationId == currentUser.OrganizationId
                && listing.Status == ListingStatus.Active,
            cancellationToken);
        if (hasActiveListing)
        {
            return "This inventory item still has an active marketplace listing. End or delist the listing before deleting or archiving it.";
        }

        bool hasActiveStorefrontListing = await db.StoreListings.AnyAsync(
            listing => listing.InventoryItemId == item.Id
                && listing.OrganizationId == currentUser.OrganizationId
                && (listing.Status == StoreListingStatus.Active || listing.Status == StoreListingStatus.Reserved),
            cancellationToken);
        if (hasActiveStorefrontListing)
        {
            return "This inventory item is still active or reserved on the storefront. Archive the storefront listing before deleting or archiving the item.";
        }

        return null;
    }

    private async Task<string?> ValidateHardDeleteAsync(InventoryItem item, CancellationToken cancellationToken)
    {
        bool hasSaleHistory = await db.Sales.AnyAsync(
            sale => sale.InventoryItemId == item.Id && sale.OrganizationId == currentUser.OrganizationId,
            cancellationToken);
        if (hasSaleHistory)
        {
            return "This inventory item has sale history. Archive it instead so financial reporting and corrections remain intact.";
        }

        bool hasOrderHistory = await db.OrderLines.AnyAsync(
            line => line.InventoryItemId == item.Id && line.OrganizationId == currentUser.OrganizationId,
            cancellationToken);
        if (hasOrderHistory)
        {
            return "This inventory item is attached to a customer order. Archive it instead so customer/order history remains intact.";
        }

        bool isBundleComponent = await db.InventoryBundleComponents.AnyAsync(
            component => component.ComponentInventoryItemId == item.Id && component.OrganizationId == currentUser.OrganizationId,
            cancellationToken);
        if (isBundleComponent)
        {
            return "This item is inside a bundle. Delete or edit the bundle before deleting the component item.";
        }

        return null;
    }

    private async Task RemoveInventoryItemGraphAsync(InventoryItem item, CancellationToken cancellationToken)
    {
        ReleaseBundleComponents(item);

        List<ChannelListing> channelListings = await db.ChannelListings
            .Where(listing => listing.InventoryItemId == item.Id && listing.OrganizationId == currentUser.OrganizationId)
            .ToListAsync(cancellationToken);
        db.ChannelListings.RemoveRange(channelListings);

        List<Expense> expenses = await db.Expenses
            .Where(expense => expense.InventoryItemId == item.Id && expense.OrganizationId == currentUser.OrganizationId)
            .ToListAsync(cancellationToken);
        db.Expenses.RemoveRange(expenses);

        List<MarketObservation> observations = await db.MarketObservations
            .Where(observation => observation.InventoryItemId == item.Id && observation.OrganizationId == currentUser.OrganizationId)
            .ToListAsync(cancellationToken);
        foreach (MarketObservation observation in observations)
        {
            observation.InventoryItemId = null;
        }

        db.InventoryItems.Remove(item);
        db.AuditLogs.Add(new AuditLog
        {
            OrganizationId = currentUser.OrganizationId,
            ActorUserId = currentUser.UserId,
            EntityName = nameof(InventoryItem),
            EntityId = item.Id,
            Action = "InventoryDeleted",
            DetailsJson = JsonSerializer.Serialize(new { item.Title, item.Status, item.IsBundle })
        });
    }

    private static void ReleaseBundleComponents(InventoryItem item)
    {
        if (!item.IsBundle)
        {
            return;
        }

        foreach (InventoryBundleComponent component in item.BundleComponents)
        {
            if (component.ComponentInventoryItem is not null && component.ComponentInventoryItem.Status == InventoryStatus.Bundled)
            {
                component.ComponentInventoryItem.Status = InventoryStatus.ReadyToList;
            }
        }
    }
}
