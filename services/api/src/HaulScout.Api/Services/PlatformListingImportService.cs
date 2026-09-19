using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using static HaulScout.Api.Helpers.StringHelpers;
using HaulScout.Api.Mapping;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Imports;
using HaulScout.Application.Security;
using HaulScout.Application.Services;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;
using HaulScout.Application.Sync.Agents;

namespace HaulScout.Api.Services;

/// <summary>
/// Listing ingest and the listing-quality workbook, as services rather than controller actions.
///
/// Both lived inside PlatformListingsController, so the only way to run them was an HTTP request to
/// that controller. The import pipeline — which records every file as a sync run — has to call them
/// too, so they moved here unchanged apart from one fix: an identical re-import now counts as
/// unchanged instead of updated.
/// </summary>
public sealed class PlatformListingImportService(HaulScoutDbContext db, ICurrentUserContext currentUser) : IPlatformListingIngestion
{
    /// <summary>Idempotent batch upsert of imported/captured listings. Re-ingesting a listing that is not yet linked
    /// refreshes it; a listing already linked to inventory keeps its link but still refreshes its imported fields.</summary>
    public async Task<AppResult<IngestPlatformListingsResultDto>> IngestAsync(IngestPlatformListingsRequest request, CancellationToken cancellationToken)
    {
        if (request.Listings is null || request.Listings.Count == 0)
        {
            return AppResult<IngestPlatformListingsResultDto>.Validation("No listings were supplied.");
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
                return AppResult<IngestPlatformListingsResultDto>.NotFound("Channel account was not found.");
            }
            channelAccountId = request.ChannelAccountId;
        }

        List<(IngestPlatformListingInput Input, string Channel, string ExternalId)> normalized = [];
        foreach (IngestPlatformListingInput input in request.Listings)
        {
            string externalId = (input.ExternalListingId ?? string.Empty).Trim();
            if (externalId.Length == 0)
            {
                return AppResult<IngestPlatformListingsResultDto>.Validation("Every listing needs an external listing id.");
            }
            if (string.IsNullOrWhiteSpace(input.Title))
            {
                return AppResult<IngestPlatformListingsResultDto>.Validation($"Listing {externalId} has no title.");
            }
            string? channel = SalesChannelCatalog.CanonicalValue(input.Platform, existingChannels) ?? defaultChannel;
            if (channel is null)
            {
                return AppResult<IngestPlatformListingsResultDto>.Validation($"Listing {externalId} has no recognized sales channel.");
            }
            if (input.Price < 0 || input.Quantity < 0)
            {
                return AppResult<IngestPlatformListingsResultDto>.Validation($"Listing {externalId} has negative price or quantity.");
            }
            normalized.Add((input, channel, externalId));
        }

        HashSet<string> keys = normalized.Select(entry => entry.ExternalId).ToHashSet();
        List<PlatformListing> existing = await db.PlatformListings
            .Where(listing => listing.OrganizationId == currentUser.OrganizationId && keys.Contains(listing.ExternalListingId))
            .ToListAsync(cancellationToken);

        int created = 0, updated = 0, skipped = 0, unchanged = 0;
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
                // Counted as updated only when a fact actually changed: an identical re-import used to
                // report every listing as updated, which made the import history say nothing useful.
                string before = ImportedFingerprint(listing);
                ApplyImportedFields(listing, input, channelAccountId);
                if (ImportedFingerprint(listing) == before)
                {
                    unchanged++;
                }
                else
                {
                    updated++;
                }
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
        return AppResult<IngestPlatformListingsResultDto>.Ok(new IngestPlatformListingsResultDto(created, updated, skipped, dtos, unchanged));
    }

    /// <summary>Import eBay's Listing Quality workbook (.xlsx). eBay stores item ids in title hyperlinks, so rows
    /// are matched back to imported active listings by durable item id and stored as historical quality snapshots.</summary>
    public async Task<AppResult<ImportPlatformListingQualityResultDto>> ImportQualityWorkbookAsync(
        byte[] workbookBytes,
        string? requestedPlatform,
        CancellationToken cancellationToken)
    {
        EbayListingQualityWorkbookParser.ParseResult parsed = EbayListingQualityWorkbookParser.Parse(workbookBytes, DateTimeOffset.UtcNow);
        if (parsed.Rows.Count == 0)
        {
            return AppResult<ImportPlatformListingQualityResultDto>.Validation(parsed.Warnings.FirstOrDefault() ?? "No listing-level recommendations were found in the eBay Listing Quality workbook.");
        }

        List<string> existingChannels = await ExistingChannels(cancellationToken);
        string platform = SalesChannelCatalog.CanonicalValue(requestedPlatform, existingChannels) ?? "eBay";
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
        return AppResult<ImportPlatformListingQualityResultDto>.Ok(new ImportPlatformListingQualityResultDto(
            imported,
            updated,
            matched,
            unmatched,
            parsed.Warnings,
            touched.OrderByDescending(snapshot => snapshot.DailyImpressions ?? 0).Take(100).Select(snapshot => snapshot.ToDto()).ToList()));
    }

    /// <summary>Best-effort auto-match of unlinked listings to inventory — by the platform's own id (via an existing
    /// <see cref="ChannelListing.ExternalListingId"/>), then by <see cref="TitleMatch"/>. Suggestions only.</summary>
    public async Task<Dictionary<Guid, (Guid Id, string Title, bool AlreadySold)>> BuildSuggestions(IReadOnlyList<PlatformListing> listings, CancellationToken cancellationToken)
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

    public static (Guid Id, string Title, bool AlreadySold)? Suggestion(
        IReadOnlyDictionary<Guid, (Guid Id, string Title, bool AlreadySold)> suggestions, PlatformListing listing) =>
        suggestions.TryGetValue(listing.Id, out (Guid Id, string Title, bool AlreadySold) match) ? match : null;

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

    /// <summary>Imported listings not yet linked to inventory or set aside — the listings match queue.</summary>
    public Task<int> CountPendingAsync(CancellationToken cancellationToken) =>
        db.PlatformListings.CountAsync(
            listing => listing.OrganizationId == currentUser.OrganizationId && listing.ReconcileStatus == PlatformListingReconcileStatus.Pending,
            cancellationToken);

    /// <summary>The imported facts of a listing, for telling a real refresh from an identical one.</summary>
    private static string ImportedFingerprint(PlatformListing listing) => ImportFingerprint.Of(
        listing.ChannelAccountId, listing.Title, listing.Price, listing.Quantity, listing.QuantitySold, listing.Currency,
        listing.Status, listing.Format, listing.Sku, listing.CategoryName, listing.Condition, listing.Url,
        listing.PrimaryImageUrl, listing.ListedAt, listing.EndsAt);
}
