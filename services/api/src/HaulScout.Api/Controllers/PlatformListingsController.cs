using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using HaulScout.Api.Authorization;
using static HaulScout.Api.Helpers.StringHelpers;
using HaulScout.Api.Mapping;
using HaulScout.Api.Services;
using HaulScout.Application.Contracts;
using HaulScout.Application.Imports;
using HaulScout.Application.Security;
using HaulScout.Application.Services;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Api.Controllers;

/// <summary>
/// The landing zone for marketplace listings captured from the page the operator is viewing or imported from a
/// platform export — active, draft, scheduled, ended, sold, unsold. Ingest is idempotent (keyed on platform +
/// external listing id); reconciling a listing seeds or links a ScoutHaul <see cref="InventoryItem"/> and its
/// <see cref="ChannelListing"/> so existing listings populate inventory without re-entry. No marketplace API or
/// unattended scraping is used.
/// </summary>
[ApiController]
[Route("api/platform-listings")]
[RequireOrganizationPermission(HaulScoutPermissions.ManageListings)]
public sealed class PlatformListingsController(
    HaulScoutDbContext db,
    ICurrentUserContext currentUser,
    PlatformListingImportService listingImports) : HaulScoutControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PlatformListingDto>>> List(
        [FromQuery] PlatformListingReconcileStatus? reconcileStatus,
        [FromQuery] PlatformListingStatus? status,
        CancellationToken cancellationToken)
    {
        IQueryable<PlatformListing> query = db.PlatformListings
            .Include(listing => listing.ReconciledInventoryItem)
            .Where(listing => listing.OrganizationId == currentUser.OrganizationId);

        if (reconcileStatus.HasValue)
        {
            query = query.Where(listing => listing.ReconcileStatus == reconcileStatus.Value);
        }
        if (status.HasValue)
        {
            query = query.Where(listing => listing.Status == status.Value);
        }

        List<PlatformListing> listings = await query
            .OrderByDescending(listing => listing.ScrapedAt)
            .Take(500)
            .ToListAsync(cancellationToken);

        Dictionary<Guid, (Guid Id, string Title, bool AlreadySold)> suggestions = await listingImports.BuildSuggestions(listings, cancellationToken);
        return listings.Select(listing => listing.ToDto(PlatformListingImportService.Suggestion(suggestions, listing))).ToList();
    }

    [HttpGet("quality-snapshots")]
    public async Task<ActionResult<IReadOnlyList<PlatformListingQualitySnapshotDto>>> ListQualitySnapshots(
        [FromQuery] int take = 200,
        CancellationToken cancellationToken = default)
    {
        int safeTake = Math.Clamp(take, 1, 500);
        List<PlatformListingQualitySnapshot> snapshots = await db.PlatformListingQualitySnapshots
            .Where(snapshot => snapshot.OrganizationId == currentUser.OrganizationId)
            .OrderByDescending(snapshot => snapshot.ReportGeneratedAt)
            .ThenByDescending(snapshot => snapshot.DailyImpressions ?? 0)
            .Take(safeTake)
            .ToListAsync(cancellationToken);

        return snapshots.Select(snapshot => snapshot.ToDto()).ToList();
    }

    /// <summary>Idempotent batch upsert of imported/captured listings. Re-ingesting a listing that is not yet linked
    /// refreshes it; a listing already linked to inventory keeps its link but still refreshes its imported fields.</summary>
    [HttpPost("ingest")]
    public async Task<ActionResult<IngestPlatformListingsResultDto>> Ingest(IngestPlatformListingsRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await listingImports.IngestAsync(request, cancellationToken));

    /// <summary>Import eBay's Listing Quality workbook (.xlsx). eBay stores item ids in title hyperlinks, so rows
    /// are matched back to imported active listings by durable item id and stored as historical quality snapshots.</summary>
    [HttpPost("import/ebay-listing-quality-workbook")]
    public async Task<ActionResult<ImportPlatformListingQualityResultDto>> ImportEbayListingQualityWorkbook(
        ImportPlatformListingQualityWorkbookRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ContentBase64))
        {
            return Invalid("Choose the eBay Listing Quality workbook (.xlsx).");
        }

        byte[] workbookBytes;
        try
        {
            workbookBytes = Convert.FromBase64String(request.ContentBase64);
        }
        catch (FormatException)
        {
            return Invalid("The Listing Quality workbook payload was not valid base64.");
        }

        return ToActionResult(await listingImports.ImportQualityWorkbookAsync(workbookBytes, request.Platform, cancellationToken));
    }

    /// <summary>Import eBay's own Active Listings export (Seller Hub → Listings → Download) — parsed server-side
    /// into the same idempotent ingest, so legacy listings seed/link inventory without re-entry.</summary>
    [HttpPost("import/ebay-csv")]
    public async Task<ActionResult<IngestPlatformListingsResultDto>> ImportEbayCsv(ImportPlatformListingsCsvRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Csv))
        {
            return Invalid("Paste or upload the eBay Active Listings CSV.");
        }

        EbayActiveListingsParser.ParseResult parsed = EbayActiveListingsParser.Parse(request.Csv);
        if (parsed.Listings.Count == 0)
        {
            return Invalid(parsed.Warning ?? "No active-listing rows were found in the report.");
        }

        return ToActionResult(await listingImports.IngestAsync(
            new IngestPlatformListingsRequest(string.IsNullOrWhiteSpace(request.Platform) ? "eBay" : request.Platform, parsed.Listings, null),
            cancellationToken));
    }

    /// <summary>Seed or link inventory from an imported listing. Provide an <c>InventoryItemId</c> to link an existing
    /// item, or set <c>CreateNewItem</c> to create a draft item from the listing. A <see cref="ChannelListing"/> is
    /// created/updated either way so the item is tracked on the channel.</summary>
    [HttpPost("{id:guid}/reconcile")]
    public async Task<ActionResult<PlatformListingReconcileResultDto>> Reconcile(Guid id, ReconcilePlatformListingRequest request, CancellationToken cancellationToken)
    {
        PlatformListing? listing = await db.PlatformListings
            .SingleOrDefaultAsync(item => item.Id == id && item.OrganizationId == currentUser.OrganizationId, cancellationToken);
        if (listing is null)
        {
            return Missing();
        }

        if (request.InventoryItemId is null && !request.CreateNewItem)
        {
            return Invalid("Choose an inventory item to link, or create a new one.");
        }

        bool createdItem = false;
        InventoryItem? item;
        if (request.InventoryItemId is Guid targetId)
        {
            item = await db.InventoryItems.SingleOrDefaultAsync(candidate => candidate.Id == targetId && candidate.OrganizationId == currentUser.OrganizationId, cancellationToken);
            if (item is null)
            {
                return Missing("The inventory item to link was not found.");
            }
        }
        else
        {
            item = new InventoryItem
            {
                OrganizationId = currentUser.OrganizationId,
                Title = listing.Title,
                Status = listing.Status == PlatformListingStatus.Active ? InventoryStatus.Listed : InventoryStatus.ReadyToList,
                EstimatedSalePrice = listing.Price,
                SellerAskingPrice = listing.Price,
                Category = TrimToNull(listing.CategoryName),
            };
            db.InventoryItems.Add(item);
            createdItem = true;
        }

        Guid channelAccountId = await ResolveChannelAccountId(listing, cancellationToken);

        // A ChannelListing records one item's presence on one channel account — which is what the
        // unique index (OrganizationId, InventoryItemId, ChannelAccountId) enforces. Two questions have
        // to be asked, because two different things can already exist:
        //   byItem       — does this item already have a presence on this account? (relisting an item
        //                  gives it a NEW platform item number, so an external-id lookup misses, and
        //                  inserting a second row for the same item violates the index)
        //   byExternalId — does another item currently claim this platform listing? (the operator is
        //                  re-linking the listing to a different item)
        ChannelListing? byItem = await db.ChannelListings.SingleOrDefaultAsync(
            existing => existing.OrganizationId == currentUser.OrganizationId
                && existing.ChannelAccountId == channelAccountId
                && existing.InventoryItemId == item.Id, cancellationToken);

        List<ChannelListing> byExternalId = listing.ExternalListingId is { Length: > 0 }
            ? await db.ChannelListings
                .Where(existing => existing.OrganizationId == currentUser.OrganizationId
                    && existing.ChannelAccountId == channelAccountId
                    && existing.ExternalListingId == listing.ExternalListingId)
                .ToListAsync(cancellationToken)
            : [];

        ChannelListing? channelListing = byItem;
        if (channelListing is null && byExternalId.Count > 0)
        {
            // Re-link: the listing moves to this item. Moving the existing row keeps one presence
            // rather than stranding the old one.
            channelListing = byExternalId[0];
            channelListing.InventoryItemId = item.Id;
        }

        // Any other row still claiming this platform listing is stale — a listing identifies one
        // presence, so release the claim instead of leaving two rows pointing at it.
        foreach (ChannelListing stale in byExternalId.Where(existing => existing != channelListing))
        {
            stale.ExternalListingId = null;
        }

        bool createdChannelListing = false;
        if (channelListing is null)
        {
            channelListing = new ChannelListing
            {
                OrganizationId = currentUser.OrganizationId,
                InventoryItemId = item.Id,
                ChannelAccountId = channelAccountId,
                ExternalListingId = listing.ExternalListingId,
                InventoryItem = item,
            };
            db.ChannelListings.Add(channelListing);
            createdChannelListing = true;
        }
        else
        {
            channelListing.InventoryItemId = item.Id;
        }

        channelListing.ExternalListingId = listing.ExternalListingId;
        channelListing.Status = MapChannelStatus(listing.Status);
        channelListing.ExternalUrl = listing.Url;
        channelListing.ListedPrice = listing.Price;
        channelListing.Quantity = listing.Quantity;
        channelListing.ListedAt ??= listing.ListedAt;
        channelListing.LastSyncedAt = DateTimeOffset.UtcNow;

        listing.ReconcileStatus = PlatformListingReconcileStatus.Linked;
        listing.ReconciledInventoryItemId = item.Id;
        listing.ReconciledChannelListingId = channelListing.Id;
        listing.CreatedChannelListingOnReconcile = listing.CreatedChannelListingOnReconcile || createdChannelListing;
        listing.ReconciledAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        listing.ReconciledInventoryItem = item;
        return new PlatformListingReconcileResultDto(listing.Id, item.Id, channelListing.Id, createdItem, listing.ToDto());
    }

    [HttpPost("{id:guid}/unlink")]
    public async Task<ActionResult<PlatformListingDto>> Unlink(Guid id, CancellationToken cancellationToken)
    {
        PlatformListing? listing = await db.PlatformListings
            .Include(item => item.ReconciledInventoryItem)
            .SingleOrDefaultAsync(item => item.Id == id && item.OrganizationId == currentUser.OrganizationId, cancellationToken);
        if (listing is null)
        {
            return Missing();
        }

        if (listing.CreatedChannelListingOnReconcile && listing.ReconciledChannelListingId is Guid channelListingId)
        {
            ChannelListing? channelListing = await db.ChannelListings.SingleOrDefaultAsync(
                item => item.Id == channelListingId && item.OrganizationId == currentUser.OrganizationId,
                cancellationToken);
            if (channelListing is not null)
            {
                db.ChannelListings.Remove(channelListing);
            }
        }

        listing.ReconcileStatus = PlatformListingReconcileStatus.Pending;
        listing.ReconciledInventoryItemId = null;
        listing.ReconciledInventoryItem = null;
        listing.ReconciledChannelListingId = null;
        listing.CreatedChannelListingOnReconcile = false;
        listing.ReconciledAt = null;

        await db.SaveChangesAsync(cancellationToken);
        return listing.ToDto();
    }

    [HttpPost("{id:guid}/ignore")]
    public async Task<ActionResult<PlatformListingDto>> Ignore(Guid id, CancellationToken cancellationToken)
    {
        PlatformListing? listing = await db.PlatformListings
            .Include(item => item.ReconciledInventoryItem)
            .SingleOrDefaultAsync(item => item.Id == id && item.OrganizationId == currentUser.OrganizationId, cancellationToken);
        if (listing is null)
        {
            return Missing();
        }

        listing.ReconcileStatus = PlatformListingReconcileStatus.Ignored;
        await db.SaveChangesAsync(cancellationToken);
        return listing.ToDto();
    }

    private static ChannelListingStatus MapChannelStatus(PlatformListingStatus status) => status switch
    {
        PlatformListingStatus.Active => ChannelListingStatus.Active,
        PlatformListingStatus.Draft or PlatformListingStatus.Scheduled => ChannelListingStatus.Draft,
        PlatformListingStatus.Sold => ChannelListingStatus.Sold,
        _ => ChannelListingStatus.Delisted, // Ended / Unsold / OutOfStock
    };

    private async Task<Guid> ResolveChannelAccountId(PlatformListing listing, CancellationToken cancellationToken)
    {
        if (listing.ChannelAccountId is Guid existingId
            && await db.ChannelAccounts.AnyAsync(account => account.Id == existingId && account.OrganizationId == currentUser.OrganizationId, cancellationToken))
        {
            return existingId;
        }

        ChannelAccount? account = await db.ChannelAccounts.FirstOrDefaultAsync(
            candidate => candidate.OrganizationId == currentUser.OrganizationId && candidate.Channel == listing.Platform, cancellationToken);
        if (account is null)
        {
            account = new ChannelAccount
            {
                OrganizationId = currentUser.OrganizationId,
                Channel = listing.Platform,
                DisplayName = listing.Platform,
                ConnectedAt = DateTimeOffset.UtcNow,
            };
            db.ChannelAccounts.Add(account);
            await db.SaveChangesAsync(cancellationToken);
        }
        return account.Id;
    }


}
