using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HaulScout.Api.Authorization;
using HaulScout.Api.Mapping;
using HaulScout.Api.Services;
using HaulScout.Application.Contracts;
using HaulScout.Application.Services;
using static HaulScout.Api.Helpers.StringHelpers;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Api.Controllers;

/// <summary>
/// The landing zone for marketplace listings the browser extension scrapes from the operator's logged-in session —
/// active, draft, scheduled, ended, sold, unsold. Ingest is idempotent (keyed on platform + external listing id);
/// reconciling a listing seeds or links a ScoutHaul <see cref="InventoryItem"/> and its <see cref="ChannelListing"/>
/// so existing listings populate inventory without re-entry. No marketplace API is used.
/// </summary>
[ApiController]
[Route("api/platform-listings")]
[RequireOrganizationPermission(HaulScoutPermissions.ManageListings)]
public sealed class PlatformListingsController(HaulScoutDbContext db, ICurrentUserContext currentUser) : ControllerBase
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

        Dictionary<Guid, (Guid Id, string Title, bool AlreadySold)> suggestions = await BuildSuggestions(listings, cancellationToken);
        return listings.Select(listing => listing.ToDto(Suggestion(suggestions, listing))).ToList();
    }

    /// <summary>Idempotent batch upsert of scraped listings. Re-ingesting a listing that is not yet linked refreshes
    /// it; a listing already linked to inventory keeps its link but still refreshes its scraped fields.</summary>
    [HttpPost("ingest")]
    public async Task<ActionResult<IngestPlatformListingsResultDto>> Ingest(IngestPlatformListingsRequest request, CancellationToken cancellationToken)
    {
        if (request.Listings is null || request.Listings.Count == 0)
        {
            return BadRequest("No listings were supplied.");
        }

        List<string> existingChannels = await ExistingChannels(cancellationToken);
        string? defaultChannel = SalesChannelCatalog.CanonicalValue(request.Platform, existingChannels);

        Guid? channelAccountId = null;
        if (request.ChannelAccountId.HasValue)
        {
            bool accountExists = await db.ChannelAccounts.AnyAsync(
                account => account.Id == request.ChannelAccountId.Value && account.OrganizationId == currentUser.OrganizationId, cancellationToken);
            if (!accountExists)
            {
                return NotFound("Channel account was not found.");
            }
            channelAccountId = request.ChannelAccountId;
        }

        List<(IngestPlatformListingInput Input, string Channel, string ExternalId)> normalized = [];
        foreach (IngestPlatformListingInput input in request.Listings)
        {
            string externalId = (input.ExternalListingId ?? string.Empty).Trim();
            if (externalId.Length == 0)
            {
                return BadRequest("Every listing needs an external listing id.");
            }
            if (string.IsNullOrWhiteSpace(input.Title))
            {
                return BadRequest($"Listing {externalId} has no title.");
            }
            string? channel = SalesChannelCatalog.CanonicalValue(input.Platform, existingChannels) ?? defaultChannel;
            if (channel is null)
            {
                return BadRequest($"Listing {externalId} has no recognized sales channel.");
            }
            if (input.Price < 0 || input.Quantity < 0)
            {
                return BadRequest($"Listing {externalId} has negative price or quantity.");
            }
            normalized.Add((input, channel, externalId));
        }

        HashSet<string> keys = normalized.Select(entry => entry.ExternalId).ToHashSet();
        List<PlatformListing> existing = await db.PlatformListings
            .Where(listing => listing.OrganizationId == currentUser.OrganizationId && keys.Contains(listing.ExternalListingId))
            .ToListAsync(cancellationToken);

        int created = 0, updated = 0, skipped = 0;
        List<Guid> touched = [];

        foreach ((IngestPlatformListingInput input, string channel, string externalId) in normalized)
        {
            PlatformListing? listing = existing.SingleOrDefault(candidate => candidate.Platform == channel && candidate.ExternalListingId == externalId);
            if (listing is null)
            {
                listing = new PlatformListing
                {
                    OrganizationId = currentUser.OrganizationId,
                    Platform = channel,
                    ExternalListingId = externalId,
                    Title = input.Title.Trim(),
                    ChannelAccountId = channelAccountId,
                };
                db.PlatformListings.Add(listing);
                existing.Add(listing);
                ApplyScrapedFields(listing, input, channelAccountId);
                created++;
            }
            else if (listing.ReconcileStatus != PlatformListingReconcileStatus.Ignored)
            {
                // Refresh scraped fields in place (keeps the inventory link if already reconciled).
                ApplyScrapedFields(listing, input, channelAccountId);
                updated++;
            }
            else
            {
                skipped++; // ignored listings are left alone
                continue;
            }
            touched.Add(listing.Id);
        }

        await db.SaveChangesAsync(cancellationToken);

        List<PlatformListing> refreshed = await db.PlatformListings
            .Include(listing => listing.ReconciledInventoryItem)
            .Where(listing => listing.OrganizationId == currentUser.OrganizationId && touched.Contains(listing.Id))
            .ToListAsync(cancellationToken);
        Dictionary<Guid, (Guid Id, string Title, bool AlreadySold)> suggestions = await BuildSuggestions(refreshed, cancellationToken);
        List<PlatformListingDto> dtos = refreshed.Select(listing => listing.ToDto(Suggestion(suggestions, listing))).ToList();
        return new IngestPlatformListingsResultDto(created, updated, skipped, dtos);
    }

    /// <summary>Seed or link inventory from a scraped listing. Provide an <c>InventoryItemId</c> to link an existing
    /// item, or set <c>CreateNewItem</c> to create a draft item from the listing. A <see cref="ChannelListing"/> is
    /// created/updated either way so the item is tracked on the channel.</summary>
    [HttpPost("{id:guid}/reconcile")]
    public async Task<ActionResult<PlatformListingReconcileResultDto>> Reconcile(Guid id, ReconcilePlatformListingRequest request, CancellationToken cancellationToken)
    {
        PlatformListing? listing = await db.PlatformListings
            .SingleOrDefaultAsync(item => item.Id == id && item.OrganizationId == currentUser.OrganizationId, cancellationToken);
        if (listing is null)
        {
            return NotFound();
        }

        if (request.InventoryItemId is null && !request.CreateNewItem)
        {
            return BadRequest("Choose an inventory item to link, or create a new one.");
        }

        bool createdItem = false;
        InventoryItem? item;
        if (request.InventoryItemId is Guid targetId)
        {
            item = await db.InventoryItems.SingleOrDefaultAsync(candidate => candidate.Id == targetId && candidate.OrganizationId == currentUser.OrganizationId, cancellationToken);
            if (item is null)
            {
                return NotFound("The inventory item to link was not found.");
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

        ChannelListing? channelListing = await db.ChannelListings.SingleOrDefaultAsync(
            existing => existing.OrganizationId == currentUser.OrganizationId
                && existing.ChannelAccountId == channelAccountId
                && existing.ExternalListingId == listing.ExternalListingId, cancellationToken);
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
        }
        else
        {
            channelListing.InventoryItemId = item.Id;
        }

        channelListing.Status = MapChannelStatus(listing.Status);
        channelListing.ExternalUrl = listing.Url;
        channelListing.ListedPrice = listing.Price;
        channelListing.Quantity = listing.Quantity;
        channelListing.ListedAt ??= listing.ListedAt;
        channelListing.LastSyncedAt = DateTimeOffset.UtcNow;

        listing.ReconcileStatus = PlatformListingReconcileStatus.Linked;
        listing.ReconciledInventoryItemId = item.Id;
        listing.ReconciledChannelListingId = channelListing.Id;
        listing.ReconciledAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        listing.ReconciledInventoryItem = item;
        return new PlatformListingReconcileResultDto(listing.Id, item.Id, channelListing.Id, createdItem, listing.ToDto());
    }

    [HttpPost("{id:guid}/ignore")]
    public async Task<ActionResult<PlatformListingDto>> Ignore(Guid id, CancellationToken cancellationToken)
    {
        PlatformListing? listing = await db.PlatformListings
            .Include(item => item.ReconciledInventoryItem)
            .SingleOrDefaultAsync(item => item.Id == id && item.OrganizationId == currentUser.OrganizationId, cancellationToken);
        if (listing is null)
        {
            return NotFound();
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

    private static void ApplyScrapedFields(PlatformListing listing, IngestPlatformListingInput input, Guid? channelAccountId)
    {
        if (channelAccountId.HasValue)
        {
            listing.ChannelAccountId = channelAccountId;
        }
        listing.Title = input.Title.Trim();
        listing.Price = input.Price;
        listing.Quantity = input.Quantity <= 0 ? 1 : input.Quantity;
        listing.QuantitySold = input.QuantitySold;
        listing.Currency = string.IsNullOrWhiteSpace(input.Currency) ? "USD" : input.Currency.Trim().ToUpperInvariant();
        listing.Status = input.Status;
        listing.Format = TrimToNull(input.Format);
        listing.Sku = TrimToNull(input.Sku);
        listing.CategoryName = TrimToNull(input.CategoryName);
        listing.Condition = TrimToNull(input.Condition);
        listing.Url = TrimToNull(input.Url);
        listing.PrimaryImageUrl = TrimToNull(input.PrimaryImageUrl);
        listing.ListedAt = input.ListedAt;
        listing.EndsAt = input.EndsAt;
        listing.RawJson = TrimToNull(input.RawJson);
        listing.ScrapedAt = DateTimeOffset.UtcNow;
    }

    private static (Guid Id, string Title, bool AlreadySold)? Suggestion(
        IReadOnlyDictionary<Guid, (Guid Id, string Title, bool AlreadySold)> suggestions, PlatformListing listing) =>
        suggestions.TryGetValue(listing.Id, out (Guid Id, string Title, bool AlreadySold) match) ? match : null;

    /// <summary>Best-effort auto-match of unlinked listings to inventory — by the platform's own id (via an existing
    /// <see cref="ChannelListing.ExternalListingId"/>), then by <see cref="TitleMatch"/>. Suggestions only.</summary>
    private async Task<Dictionary<Guid, (Guid Id, string Title, bool AlreadySold)>> BuildSuggestions(IReadOnlyList<PlatformListing> listings, CancellationToken cancellationToken)
    {
        Dictionary<Guid, (Guid, string, bool)> suggestions = new();
        List<PlatformListing> openListings = listings.Where(listing => listing.ReconcileStatus == PlatformListingReconcileStatus.Pending).ToList();
        if (openListings.Count == 0)
        {
            return suggestions;
        }

        var inventory = await db.InventoryItems
            .Where(item => item.OrganizationId == currentUser.OrganizationId && !item.IsArchived)
            .OrderByDescending(item => item.UpdatedAt)
            .Select(item => new { item.Id, item.Title, item.Status })
            .Take(2000)
            .ToListAsync(cancellationToken);
        Dictionary<Guid, string> titleById = inventory.ToDictionary(item => item.Id, item => item.Title);
        Dictionary<Guid, bool> soldById = inventory.ToDictionary(item => item.Id, item => InventoryLifecycle.IsSold(item.Status));
        List<(Guid Id, string Title)> titleCandidates = inventory.Select(item => (item.Id, item.Title)).ToList();

        List<string> externalIds = openListings.Select(listing => listing.ExternalListingId).Distinct().ToList();
        var byExternalId = await db.ChannelListings
            .Where(channel => channel.OrganizationId == currentUser.OrganizationId
                && channel.ExternalListingId != null && externalIds.Contains(channel.ExternalListingId))
            .Select(channel => new { channel.ExternalListingId, channel.InventoryItemId })
            .ToListAsync(cancellationToken);

        foreach (PlatformListing listing in openListings)
        {
            Guid? matchId = byExternalId.FirstOrDefault(candidate => candidate.ExternalListingId == listing.ExternalListingId)?.InventoryItemId;
            matchId ??= TitleMatch.Best(listing.Title, titleCandidates);
            if (matchId is Guid resolved && titleById.TryGetValue(resolved, out string? title))
            {
                suggestions[listing.Id] = (resolved, title, soldById.GetValueOrDefault(resolved));
            }
        }

        return suggestions;
    }

    private async Task<List<string>> ExistingChannels(CancellationToken cancellationToken)
    {
        List<string> saleChannels = await db.Sales.AsNoTracking()
            .Where(item => item.OrganizationId == currentUser.OrganizationId).Select(item => item.Platform).Distinct().ToListAsync(cancellationToken);
        List<string> listingChannels = await db.Listings.AsNoTracking()
            .Where(item => item.OrganizationId == currentUser.OrganizationId).Select(item => item.Platform).Distinct().ToListAsync(cancellationToken);
        List<string> channelAccounts = await db.ChannelAccounts.AsNoTracking()
            .Where(item => item.OrganizationId == currentUser.OrganizationId).Select(item => item.Channel).Distinct().ToListAsync(cancellationToken);
        return saleChannels.Concat(listingChannels).Concat(channelAccounts).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToList();
    }
}
