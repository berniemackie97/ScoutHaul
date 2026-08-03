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

    /// <summary>Import eBay's own Active Listings export (Seller Hub → Listings → Download) — parsed server-side
    /// into the same idempotent ingest, so legacy listings seed/link inventory without re-entry.</summary>
    [HttpPost("import/ebay-csv")]
    public async Task<ActionResult<IngestPlatformListingsResultDto>> ImportEbayCsv(ImportPlatformListingsCsvRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Csv))
        {
            return BadRequest("Paste or upload the eBay Active Listings CSV.");
        }

        EbayActiveListingsParser.ParseResult parsed = EbayActiveListingsParser.Parse(request.Csv);
        if (parsed.Listings.Count == 0)
        {
            return BadRequest(parsed.Warning ?? "No active-listing rows were found in the report.");
        }

        return await Ingest(
            new IngestPlatformListingsRequest(string.IsNullOrWhiteSpace(request.Platform) ? "eBay" : request.Platform, parsed.Listings, null),
            cancellationToken);
    }

    /// <summary>Import eBay's Listing Quality workbook (.xlsx). eBay stores item ids in title hyperlinks, so rows
    /// are matched back to imported active listings by durable item id and stored as historical quality snapshots.</summary>
    [HttpPost("import/ebay-listing-quality-workbook")]
    public async Task<ActionResult<ImportPlatformListingQualityResultDto>> ImportEbayListingQualityWorkbook(
        ImportPlatformListingQualityWorkbookRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ContentBase64))
        {
            return BadRequest("Choose the eBay Listing Quality workbook (.xlsx).");
        }

        byte[] workbookBytes;
        try
        {
            workbookBytes = Convert.FromBase64String(request.ContentBase64);
        }
        catch (FormatException)
        {
            return BadRequest("The Listing Quality workbook payload was not valid base64.");
        }

        EbayListingQualityWorkbookParser.ParseResult parsed = EbayListingQualityWorkbookParser.Parse(workbookBytes, DateTimeOffset.UtcNow);
        if (parsed.Rows.Count == 0)
        {
            return BadRequest(parsed.Warnings.FirstOrDefault() ?? "No listing-level recommendations were found in the eBay Listing Quality workbook.");
        }

        List<string> existingChannels = await ExistingChannels(cancellationToken);
        string platform = SalesChannelCatalog.CanonicalValue(request.Platform, existingChannels) ?? "eBay";
        List<string> externalIds = parsed.Rows.Select(row => row.ExternalListingId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        List<PlatformListing> listings = await db.PlatformListings
            .Where(listing => listing.OrganizationId == currentUser.OrganizationId
                && listing.Platform == platform
                && externalIds.Contains(listing.ExternalListingId))
            .ToListAsync(cancellationToken);

        List<PlatformListingQualitySnapshot> existingSnapshots = await db.PlatformListingQualitySnapshots
            .Where(snapshot => snapshot.OrganizationId == currentUser.OrganizationId
                && snapshot.Platform == platform
                && externalIds.Contains(snapshot.ExternalListingId))
            .ToListAsync(cancellationToken);

        int imported = 0, updated = 0, matched = 0, unmatched = 0;
        List<PlatformListingQualitySnapshot> touched = [];
        DateTimeOffset now = DateTimeOffset.UtcNow;

        foreach (EbayListingQualityWorkbookParser.ListingQualityRow row in parsed.Rows)
        {
            PlatformListing? listing = listings.FirstOrDefault(candidate =>
                candidate.ExternalListingId.Equals(row.ExternalListingId, StringComparison.OrdinalIgnoreCase));
            if (listing is null)
            {
                unmatched++;
            }
            else
            {
                matched++;
            }

            PlatformListingQualitySnapshot? snapshot = existingSnapshots.FirstOrDefault(candidate =>
                candidate.ExternalListingId.Equals(row.ExternalListingId, StringComparison.OrdinalIgnoreCase)
                && candidate.ReportGeneratedAt == row.ReportGeneratedAt);
            if (snapshot is null)
            {
                snapshot = new PlatformListingQualitySnapshot
                {
                    OrganizationId = currentUser.OrganizationId,
                    Platform = platform,
                    ExternalListingId = row.ExternalListingId,
                    Title = row.Title,
                    ReportGeneratedAt = row.ReportGeneratedAt,
                };
                db.PlatformListingQualitySnapshots.Add(snapshot);
                existingSnapshots.Add(snapshot);
                imported++;
            }
            else
            {
                updated++;
            }

            ApplyQualitySnapshot(snapshot, row, listing?.Id, now);
            touched.Add(snapshot);
        }

        await db.SaveChangesAsync(cancellationToken);
        return new ImportPlatformListingQualityResultDto(
            imported,
            updated,
            matched,
            unmatched,
            parsed.Warnings,
            touched.OrderByDescending(snapshot => snapshot.DailyImpressions ?? 0).Take(100).Select(snapshot => snapshot.ToDto()).ToList());
    }

    /// <summary>Idempotent batch upsert of imported/captured listings. Re-ingesting a listing that is not yet linked
    /// refreshes it; a listing already linked to inventory keeps its link but still refreshes its imported fields.</summary>
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
                ApplyImportedFields(listing, input, channelAccountId);
                created++;
            }
            else
            {
                // Refresh imported fields in place. Ignored listings stay ignored until an operator restores
                // them, but their imported facts must not get frozen forever after a bad first import.
                ApplyImportedFields(listing, input, channelAccountId);
                updated++;
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
            return NotFound();
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

    private static void ApplyImportedFields(PlatformListing listing, IngestPlatformListingInput input, Guid? channelAccountId)
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

    private static void ApplyQualitySnapshot(
        PlatformListingQualitySnapshot snapshot,
        EbayListingQualityWorkbookParser.ListingQualityRow row,
        Guid? platformListingId,
        DateTimeOffset importedAt)
    {
        snapshot.PlatformListingId = platformListingId;
        snapshot.Title = row.Title;
        snapshot.CategoryName = TrimToNull(row.CategoryName);
        snapshot.ListingCondition = TrimToNull(row.ListingCondition);
        snapshot.PriceRange = TrimToNull(row.PriceRange);
        snapshot.ImportedAt = importedAt;
        snapshot.Action = TrimToNull(row.Action);
        snapshot.DailyImpressions = row.DailyImpressions;
        snapshot.ClickThroughRate = row.ClickThroughRate;
        snapshot.SalesConversionRate = row.SalesConversionRate;
        snapshot.PromotedListingsAdRate = row.PromotedListingsAdRate;
        snapshot.RecommendedItemSpecificsFilled = row.RecommendedItemSpecificsFilled;
        snapshot.RecommendedItemSpecificsToAdd = TrimToNull(row.RecommendedItemSpecificsToAdd);
        snapshot.Brand = TrimToNull(row.Brand);
        snapshot.Mpn = TrimToNull(row.Mpn);
        snapshot.Upc = TrimToNull(row.Upc);
        snapshot.PhotoCount = row.PhotoCount;
        snapshot.TitleKeywordCount = row.TitleKeywordCount;
        snapshot.FreeShipping = row.FreeShipping;
        snapshot.EstimatedHandlingDays = row.EstimatedHandlingDays;
        snapshot.AcceptsReturns = row.AcceptsReturns;
        snapshot.SellerPaidReturns = row.SellerPaidReturns;
        snapshot.PromotedListings = row.PromotedListings;
        snapshot.BestOffer = row.BestOffer;
        snapshot.RawJson = JsonSerializer.Serialize(row.RawFields);
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
