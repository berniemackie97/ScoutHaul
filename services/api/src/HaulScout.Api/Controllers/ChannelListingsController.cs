using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HaulScout.Api.Authorization;
using static HaulScout.Api.Helpers.StringHelpers;
using HaulScout.Api.Mapping;
using HaulScout.Api.Services;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Api.Controllers;

[ApiController]
[Route("api/inventory/{itemId:guid}/channel-listings")]
[RequireOrganizationPermission(HaulScoutPermissions.ManageListings)]
public sealed class ChannelListingsController(HaulScoutDbContext db, ICurrentUserContext currentUser) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ChannelListingDto>>> List(Guid itemId, CancellationToken cancellationToken)
    {
        if (!await ItemExists(itemId, cancellationToken))
        {
            return NotFound();
        }

        List<ChannelListing> listings = await Query(itemId)
            .OrderBy(item => item.ChannelAccount!.DisplayName)
            .ToListAsync(cancellationToken);

        return listings.Select(item => item.ToDto()).ToList();
    }

    /// <summary>Create or update the item's state on one channel account (keyed on item + account).</summary>
    [HttpPut]
    public async Task<ActionResult<ChannelListingDto>> Upsert(Guid itemId, UpsertChannelListingRequest request, CancellationToken cancellationToken)
    {
        if (!await ItemExists(itemId, cancellationToken))
        {
            return NotFound("Inventory item not found.");
        }

        bool accountValid = await db.ChannelAccounts.AnyAsync(
            account => account.Id == request.ChannelAccountId && account.OrganizationId == currentUser.OrganizationId,
            cancellationToken);
        if (!accountValid)
        {
            return BadRequest("Channel account not found.");
        }

        ChannelListing? listing = await Query(itemId).SingleOrDefaultAsync(item => item.ChannelAccountId == request.ChannelAccountId, cancellationToken);
        if (listing is null)
        {
            listing = new ChannelListing
            {
                OrganizationId = currentUser.OrganizationId,
                InventoryItemId = itemId,
                ChannelAccountId = request.ChannelAccountId,
            };
            db.ChannelListings.Add(listing);
        }

        listing.Status = request.Status;
        listing.ListedPrice = request.ListedPrice;
        listing.Quantity = request.Quantity;
        listing.ExternalListingId = TrimToNull(request.ExternalListingId);
        listing.ExternalUrl = TrimToNull(request.ExternalUrl);
        listing.ImageHash = TrimToNull(request.ImageHash);
        listing.LastSyncedAt = DateTimeOffset.UtcNow;
        listing.LastError = null;
        if (request.Status == ChannelListingStatus.Active)
        {
            listing.ListedAt ??= DateTimeOffset.UtcNow;
        }
        if (request.Status == ChannelListingStatus.Sold)
        {
            listing.SoldAt ??= DateTimeOffset.UtcNow;
            // Sold here → every other channel still live needs manual review/removal. Flag those rows
            // for the admin action queue; nothing is auto-delisted from external marketplaces.
            List<ChannelListing> siblings = await db.ChannelListings
                .Where(item => item.InventoryItemId == itemId
                    && item.OrganizationId == currentUser.OrganizationId
                    && item.ChannelAccountId != request.ChannelAccountId
                    && item.Status == ChannelListingStatus.Active)
                .ToListAsync(cancellationToken);
            foreach (ChannelListing sibling in siblings)
            {
                sibling.Status = ChannelListingStatus.PendingDelist;
                sibling.LastSyncedAt = DateTimeOffset.UtcNow;
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        ChannelListing saved = await Query(itemId).SingleAsync(item => item.Id == listing.Id, cancellationToken);
        return saved.ToDto();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid itemId, Guid id, CancellationToken cancellationToken)
    {
        ChannelListing? listing = await db.ChannelListings.SingleOrDefaultAsync(
            item => item.Id == id && item.InventoryItemId == itemId && item.OrganizationId == currentUser.OrganizationId,
            cancellationToken);
        if (listing is null)
        {
            return NotFound();
        }

        db.ChannelListings.Remove(listing);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>Org-wide channel-listing worklist across all items (for example status=PendingDelist).</summary>
    [HttpGet("~/api/channel-listings")]
    public async Task<ActionResult<IReadOnlyList<ChannelListingDto>>> Worklist([FromQuery] string? status, CancellationToken cancellationToken)
    {
        IQueryable<ChannelListing> query = db.ChannelListings
            .Include(item => item.ChannelAccount)
            .Include(item => item.InventoryItem)
            .Where(item => item.OrganizationId == currentUser.OrganizationId);

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<ChannelListingStatus>(status, ignoreCase: true, out ChannelListingStatus parsed))
        {
            query = query.Where(item => item.Status == parsed);
        }

        List<ChannelListing> listings = await query
            .OrderByDescending(item => item.LastSyncedAt)
            .Take(500)
            .ToListAsync(cancellationToken);

        return listings.Select(item => item.ToDto()).ToList();
    }

    private IQueryable<ChannelListing> Query(Guid itemId) =>
        db.ChannelListings
            .Include(item => item.ChannelAccount)
            .Include(item => item.InventoryItem)
            .Where(item => item.InventoryItemId == itemId && item.OrganizationId == currentUser.OrganizationId);

    private Task<bool> ItemExists(Guid itemId, CancellationToken cancellationToken) =>
        db.InventoryItems.AnyAsync(item => item.Id == itemId && item.OrganizationId == currentUser.OrganizationId, cancellationToken);
}
