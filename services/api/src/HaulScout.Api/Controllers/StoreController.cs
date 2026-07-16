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

/// <summary>
/// Operator-facing storefront management: publish an InventoryItem onto the store as a
/// StoreListing and manage listings + categories. Public (customer) catalog reads are a
/// separate, anonymous, host-scoped surface built with the storefront.
/// </summary>
[ApiController]
[Route("api/store")]
[RequireOrganizationPermission(HaulScoutPermissions.ManageListings)]
public sealed class StoreController(
    HaulScoutDbContext db,
    ICurrentUserContext currentUser) : ControllerBase
{
    // ── Categories ───────────────────────────────────────────────
    [HttpGet("categories")]
    public async Task<ActionResult<IReadOnlyList<StoreCategoryDto>>> ListCategories(CancellationToken cancellationToken)
    {
        List<StoreCategory> categories = await db.StoreCategories
            .OrderBy(category => category.SortOrder)
            .ThenBy(category => category.Name)
            .ToListAsync(cancellationToken);

        return categories.Select(category => category.ToDto()).ToList();
    }

    [HttpPost("categories")]
    public async Task<ActionResult<StoreCategoryDto>> CreateCategory(UpsertStoreCategoryRequest request, CancellationToken cancellationToken)
    {
        string? name = TrimToNull(request.Name);
        if (name is null)
        {
            return BadRequest("Category name is required.");
        }

        if (request.ParentId is Guid parentId
            && !await db.StoreCategories.AnyAsync(category => category.Id == parentId, cancellationToken))
        {
            return BadRequest("Parent category not found.");
        }

        StoreCategory category = new()
        {
            OrganizationId = currentUser.OrganizationId,
            Name = name,
            Slug = await EnsureUniqueCategorySlugAsync(TrimToNull(request.Slug) ?? Slugify(name), cancellationToken),
            Description = TrimToNull(request.Description),
            ParentId = request.ParentId,
            SortOrder = request.SortOrder,
            IsActive = request.IsActive,
        };

        db.StoreCategories.Add(category);
        await db.SaveChangesAsync(cancellationToken);
        return category.ToDto();
    }

    // ── Listings ─────────────────────────────────────────────────
    [HttpGet("listings")]
    public async Task<ActionResult<IReadOnlyList<StoreListingDto>>> ListListings(
        [FromQuery] StoreListingStatus? status,
        CancellationToken cancellationToken)
    {
        IQueryable<StoreListing> query = db.StoreListings.AsQueryable();
        if (status is not null)
        {
            query = query.Where(listing => listing.Status == status);
        }

        List<StoreListing> listings = await query
            .OrderByDescending(listing => listing.CreatedAt)
            .ToListAsync(cancellationToken);

        return listings.Select(listing => listing.ToDto()).ToList();
    }

    [HttpGet("listings/{id:guid}")]
    public async Task<ActionResult<StoreListingDto>> GetListing(Guid id, CancellationToken cancellationToken)
    {
        StoreListing? listing = await db.StoreListings.FirstOrDefaultAsync(listing => listing.Id == id, cancellationToken);
        return listing is null ? NotFound() : listing.ToDto();
    }

    [HttpPost("listings")]
    public async Task<ActionResult<StoreListingDto>> Publish(PublishListingRequest request, CancellationToken cancellationToken)
    {
        if (request.ListPrice < 0)
        {
            return BadRequest("List price cannot be negative.");
        }

        // Org scoping is enforced by the global query filter, so this only finds items in our org.
        InventoryItem? item = await db.InventoryItems.FirstOrDefaultAsync(item => item.Id == request.InventoryItemId, cancellationToken);
        if (item is null)
        {
            return BadRequest("Inventory item not found.");
        }

        if (await db.StoreListings.AnyAsync(listing => listing.InventoryItemId == item.Id, cancellationToken))
        {
            return Conflict("This inventory item is already published to the storefront.");
        }

        if (request.StoreCategoryId is Guid categoryId
            && !await db.StoreCategories.AnyAsync(category => category.Id == categoryId, cancellationToken))
        {
            return BadRequest("Store category not found.");
        }

        string title = TrimToNull(request.Title) ?? item.Title;

        StoreListing listing = new()
        {
            OrganizationId = currentUser.OrganizationId,
            InventoryItemId = item.Id,
            StoreCategoryId = request.StoreCategoryId,
            Slug = await EnsureUniqueListingSlugAsync(Slugify(title), cancellationToken),
            Title = title,
            Subtitle = TrimToNull(request.Subtitle),
            Description = TrimToNull(request.Description),
            ListPrice = request.ListPrice,
            CompareAtPrice = request.CompareAtPrice,
            IsFeatured = request.IsFeatured,
            MetaTitle = TrimToNull(request.MetaTitle),
            MetaDescription = TrimToNull(request.MetaDescription),
            Status = StoreListingStatus.Active,
            PublishedAt = DateTimeOffset.UtcNow,
        };
        db.StoreListings.Add(listing);

        // Advance the inventory item into the Listed stage of its lifecycle.
        item.Status = InventoryStatus.Listed;
        item.ListedAt ??= DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(GetListing), new { id = listing.Id }, listing.ToDto());
    }

    [HttpPut("listings/{id:guid}")]
    public async Task<ActionResult<StoreListingDto>> UpdateListing(Guid id, UpdateStoreListingRequest request, CancellationToken cancellationToken)
    {
        if (request.ListPrice < 0)
        {
            return BadRequest("List price cannot be negative.");
        }

        StoreListing? listing = await db.StoreListings.FirstOrDefaultAsync(listing => listing.Id == id, cancellationToken);
        if (listing is null)
        {
            return NotFound();
        }

        if (request.StoreCategoryId is Guid categoryId
            && !await db.StoreCategories.AnyAsync(category => category.Id == categoryId, cancellationToken))
        {
            return BadRequest("Store category not found.");
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
            listing.PublishedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
        return listing.ToDto();
    }

    [HttpPost("listings/{id:guid}/unpublish")]
    public async Task<ActionResult<StoreListingDto>> Unpublish(Guid id, CancellationToken cancellationToken)
    {
        StoreListing? listing = await db.StoreListings.FirstOrDefaultAsync(listing => listing.Id == id, cancellationToken);
        if (listing is null)
        {
            return NotFound();
        }

        listing.Status = StoreListingStatus.Draft;
        await db.SaveChangesAsync(cancellationToken);
        return listing.ToDto();
    }

    private async Task<string> EnsureUniqueListingSlugAsync(string baseSlug, CancellationToken cancellationToken)
    {
        string stem = string.IsNullOrEmpty(baseSlug) ? "listing" : baseSlug;
        string candidate = stem;
        int suffix = 2;
        while (await db.StoreListings.AnyAsync(listing => listing.Slug == candidate, cancellationToken))
        {
            candidate = $"{stem}-{suffix++}";
        }

        return candidate;
    }

    private async Task<string> EnsureUniqueCategorySlugAsync(string baseSlug, CancellationToken cancellationToken)
    {
        string stem = string.IsNullOrEmpty(baseSlug) ? "category" : baseSlug;
        string candidate = stem;
        int suffix = 2;
        while (await db.StoreCategories.AnyAsync(category => category.Slug == candidate, cancellationToken))
        {
            candidate = $"{stem}-{suffix++}";
        }

        return candidate;
    }
}
