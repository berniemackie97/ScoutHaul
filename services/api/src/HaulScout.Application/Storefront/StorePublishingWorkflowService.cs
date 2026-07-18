using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using static HaulScout.Application.Common.Text;

namespace HaulScout.Application.Storefront;

/// <summary>
/// The operator's side of the shelf. Publishing IS listing: it marks the item Listed (behind the domain's listing
/// gate) and the save-time synchronizer materializes the store listing in the same transaction; unpublishing is the
/// reverse. There is deliberately no way to point the shelf and the inventory in different directions.
/// </summary>
public sealed class StorePublishingWorkflowService(
    IStoreListingStore store,
    ICurrentUserContext currentUser,
    TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<StoreListingDto>> ListListingsAsync(StoreListingStatus? status, CancellationToken cancellationToken) =>
        (await store.ListListingsAsync(currentUser.OrganizationId, status, cancellationToken))
            .Select(listing => listing.ToDto())
            .ToList();

    public async Task<AppResult<StoreListingDto>> GetListingAsync(Guid listingId, CancellationToken cancellationToken)
    {
        StoreListing? listing = await store.FindListingAsync(currentUser.OrganizationId, listingId, cancellationToken);
        return listing is null ? AppResult<StoreListingDto>.NotFound() : AppResult<StoreListingDto>.Ok(listing.ToDto());
    }

    public async Task<AppResult<StoreListingDto>> GetListingByItemAsync(Guid inventoryItemId, CancellationToken cancellationToken)
    {
        StoreListing? listing = await store.FindListingByItemAsync(currentUser.OrganizationId, inventoryItemId, cancellationToken);
        return listing is null ? AppResult<StoreListingDto>.NotFound() : AppResult<StoreListingDto>.Ok(listing.ToDto());
    }

    public async Task<AppResult<StoreListingDto>> PublishAsync(PublishListingRequest request, CancellationToken cancellationToken)
    {
        if (request.ListPrice < 0)
        {
            return AppResult<StoreListingDto>.Validation("List price cannot be negative.");
        }

        Guid organizationId = currentUser.OrganizationId;
        InventoryItem? item = await store.FindItemForPublishingAsync(organizationId, request.InventoryItemId, cancellationToken);
        if (item is null)
        {
            return AppResult<StoreListingDto>.Validation("Inventory item not found.");
        }

        if (request.StoreCategoryId is Guid categoryId
            && !await store.CategoryExistsAsync(organizationId, categoryId, cancellationToken))
        {
            return AppResult<StoreListingDto>.Validation("Store category not found.");
        }

        // An operator explicitly pricing the publish satisfies "it needs a price" — record it as the item's
        // estimate when none exists, so the gate and the seeded listing agree with what they just typed.
        if (StorePublishing.ShelfPrice(item) <= 0 && request.ListPrice > 0)
        {
            item.EstimatedSalePrice = request.ListPrice;
        }

        // The listing gate: manual publishing requires a shopper-ready item, photos included. (Imports bypass this
        // by design — reality arriving is not a request — and land as Draft "awaiting photos" instead.)
        if (StorePublishing.ReasonsItCannotBeListed(item) is { Count: > 0 } reasons)
        {
            return AppResult<StoreListingDto>.Validation($"This item is not ready for the storefront: {string.Join(" ", reasons)}");
        }

        item.Status = InventoryStatus.Listed;
        item.ListedAt ??= timeProvider.GetUtcNow();
        await store.SaveChangesAsync(cancellationToken);   // the synchronizer creates/activates the listing here

        StoreListing? listing = await store.FindListingByItemAsync(organizationId, item.Id, cancellationToken);
        if (listing is null)
        {
            return AppResult<StoreListingDto>.Conflict("The listing did not materialize — run the shelf reconcile and retry.");
        }

        ApplyPresentation(listing, request);
        await store.SaveChangesAsync(cancellationToken);
        return AppResult<StoreListingDto>.Ok(listing.ToDto());
    }

    public async Task<AppResult<StoreListingDto>> UpdateCommerceAsync(Guid listingId, UpdateStoreListingCommerceRequest request, CancellationToken cancellationToken)
    {
        if (ValidateCommerce(request) is string validation)
        {
            return AppResult<StoreListingDto>.Validation(validation);
        }

        StoreListing? listing = await store.FindListingAsync(currentUser.OrganizationId, listingId, cancellationToken);
        if (listing is null)
        {
            return AppResult<StoreListingDto>.NotFound();
        }

        ApplyCommerce(listing, request);
        await store.SaveChangesAsync(cancellationToken);
        return AppResult<StoreListingDto>.Ok(listing.ToDto());
    }

    public async Task<AppResult<StoreListingDto>> UpdateListingAsync(Guid listingId, UpdateStoreListingRequest request, CancellationToken cancellationToken)
    {
        if (request.ListPrice < 0)
        {
            return AppResult<StoreListingDto>.Validation("List price cannot be negative.");
        }

        StoreListing? listing = await store.FindListingAsync(currentUser.OrganizationId, listingId, cancellationToken);
        if (listing is null)
        {
            return AppResult<StoreListingDto>.NotFound();
        }

        if (request.StoreCategoryId is Guid categoryId
            && !await store.CategoryExistsAsync(currentUser.OrganizationId, categoryId, cancellationToken))
        {
            return AppResult<StoreListingDto>.Validation("Store category not found.");
        }

        // Reserved belongs to a live checkout and Sold to a completed one — hand-editing either would falsify
        // commerce state. Draft/Active edits are honored; the synchronizer re-derives on the next item change.
        if (request.Status is StoreListingStatus.Reserved or StoreListingStatus.Sold && request.Status != listing.Status)
        {
            return AppResult<StoreListingDto>.Validation("Reserved and Sold are commerce states — they follow checkout, not edits.");
        }

        listing.ListPrice = request.ListPrice;
        listing.CompareAtPrice = request.CompareAtPrice;
        listing.StoreCategoryId = request.StoreCategoryId;
        listing.Title = TrimToNull(request.Title) ?? listing.Title;
        listing.Subtitle = TrimToNull(request.Subtitle);
        listing.Description = TrimToNull(request.Description);
        listing.IsFeatured = request.IsFeatured;
        listing.MetaTitle = TrimToNull(request.MetaTitle);
        listing.MetaDescription = TrimToNull(request.MetaDescription);
        listing.Status = request.Status;
        if (request.Status == StoreListingStatus.Active && listing.PublishedAt is null)
        {
            listing.PublishedAt = timeProvider.GetUtcNow();
        }

        await store.SaveChangesAsync(cancellationToken);
        return AppResult<StoreListingDto>.Ok(listing.ToDto());
    }

    public async Task<AppResult<StoreListingDto>> UnpublishAsync(Guid listingId, CancellationToken cancellationToken)
    {
        StoreListing? listing = await store.FindListingWithItemAsync(currentUser.OrganizationId, listingId, cancellationToken);
        if (listing is null)
        {
            return AppResult<StoreListingDto>.NotFound();
        }

        if (listing.Status == StoreListingStatus.Reserved)
        {
            return AppResult<StoreListingDto>.Conflict(
                "This item is in a shopper's checkout right now. It will resolve within the session window; try again after.");
        }

        // Unpublishing IS unlisting. Setting only the listing to Draft while the item stayed Listed would be the
        // exact shelf-vs-inventory drift this design removes — the next sync would faithfully re-activate it.
        if (listing.InventoryItem is { Status: InventoryStatus.Listed } item)
        {
            item.Status = InventoryStatus.ReadyToList;
        }

        listing.Status = StoreListingStatus.Draft;
        await store.SaveChangesAsync(cancellationToken);
        return AppResult<StoreListingDto>.Ok(listing.ToDto());
    }

    public async Task<ReconcileStoreCatalogResultDto> ReconcileAsync(CancellationToken cancellationToken)
    {
        ShelfReconciliation result = await store.ReconcileShelfAsync(currentUser.OrganizationId, cancellationToken);
        return new ReconcileStoreCatalogResultDto(
            result.ItemsExamined, result.ListingsCreated, result.NowActive, result.AwaitingPhotos, result.Retired);
    }

    public async Task<IReadOnlyList<StoreCategoryDto>> ListCategoriesAsync(CancellationToken cancellationToken) =>
        (await store.ListCategoriesAsync(currentUser.OrganizationId, cancellationToken))
            .Select(category => category.ToDto())
            .ToList();

    public async Task<AppResult<StoreCategoryDto>> CreateCategoryAsync(UpsertStoreCategoryRequest request, CancellationToken cancellationToken)
    {
        Guid organizationId = currentUser.OrganizationId;
        string? name = TrimToNull(request.Name);
        if (name is null)
        {
            return AppResult<StoreCategoryDto>.Validation("Category name is required.");
        }

        if (request.ParentId is Guid parentId && !await store.CategoryExistsAsync(organizationId, parentId, cancellationToken))
        {
            return AppResult<StoreCategoryDto>.Validation("Parent category not found.");
        }

        StoreCategory category = new()
        {
            OrganizationId = organizationId,
            Name = name,
            Slug = await store.EnsureUniqueCategorySlugAsync(
                organizationId,
                TrimToNull(request.Slug) ?? StorefrontCatalogService.Slugify(name),
                cancellationToken),
            Description = TrimToNull(request.Description),
            ParentId = request.ParentId,
            SortOrder = request.SortOrder,
            IsActive = request.IsActive,
        };

        await store.AddCategoryAsync(category, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return AppResult<StoreCategoryDto>.Ok(category.ToDto());
    }

    /// <summary>The operator's initial presentation, applied on top of the synchronizer's seeded defaults.</summary>
    private static void ApplyPresentation(StoreListing listing, PublishListingRequest request)
    {
        listing.StoreCategoryId = request.StoreCategoryId ?? listing.StoreCategoryId;
        listing.Title = TrimToNull(request.Title) ?? listing.Title;
        listing.Subtitle = TrimToNull(request.Subtitle) ?? listing.Subtitle;
        listing.Description = TrimToNull(request.Description) ?? listing.Description;
        listing.ListPrice = request.ListPrice > 0 ? request.ListPrice : listing.ListPrice;
        listing.CompareAtPrice = request.CompareAtPrice ?? listing.CompareAtPrice;
        listing.IsFeatured = request.IsFeatured;
        listing.MetaTitle = TrimToNull(request.MetaTitle) ?? listing.MetaTitle;
        listing.MetaDescription = TrimToNull(request.MetaDescription) ?? listing.MetaDescription;
    }

    private static string? ValidateCommerce(UpdateStoreListingCommerceRequest request)
    {
        if (request.MinimumOfferAmount is < 0 || request.MaximumOfferAmount is < 0)
        {
            return "Offer limits cannot be negative.";
        }

        if (request.MinimumOfferAmount is decimal min
            && request.MaximumOfferAmount is decimal max
            && min > max)
        {
            return "Minimum offer cannot be greater than maximum offer.";
        }

        if (request.ShippingPolicy == StoreShippingPolicy.FixedAmount)
        {
            if (request.ShippingAmountOverride is null)
            {
                return "Fixed shipping requires a shipping amount.";
            }

            if (request.ShippingAmountOverride < 0)
            {
                return "Shipping amount cannot be negative.";
            }
        }
        else if (request.ShippingAmountOverride is < 0)
        {
            return "Shipping amount cannot be negative.";
        }

        return null;
    }

    private static void ApplyCommerce(StoreListing listing, UpdateStoreListingCommerceRequest request)
    {
        listing.AcceptsOffers = request.AcceptsOffers;
        listing.MinimumOfferAmount = request.AcceptsOffers ? RoundMoney(request.MinimumOfferAmount) : null;
        listing.MaximumOfferAmount = request.AcceptsOffers ? RoundMoney(request.MaximumOfferAmount) : null;
        listing.ShippingPolicy = request.ShippingPolicy;
        listing.ShippingAmountOverride = request.ShippingPolicy == StoreShippingPolicy.FixedAmount
            ? RoundMoney(request.ShippingAmountOverride)
            : null;
    }

    private static decimal? RoundMoney(decimal? amount) =>
        amount is null ? null : decimal.Round(amount.Value, 2, MidpointRounding.AwayFromZero);
}
