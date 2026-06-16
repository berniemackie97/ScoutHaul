using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScoutHaul.Application.Common;
using ScoutHaul.Application.Contracts;
using ScoutHaul.Domain;
using ScoutHaul.Infrastructure.Persistence;

namespace ScoutHaul.Api.Controllers;

/// <summary>
/// Public, anonymous storefront catalog read API consumed by the Next.js storefront.
/// The store (organization) is resolved from the X-ScoutHaul-Store header, which the
/// storefront sets from its host→org mapping. Only published (Active) listings and a
/// safe subset of fields are exposed — never cost, supplier, or internal data.
/// </summary>
[ApiController]
[Route("api/storefront")]
[AllowAnonymous]
public sealed class StorefrontController(ScoutHaulDbContext db, ITenantContext tenant) : ControllerBase
{
    private const string StoreHeader = "X-ScoutHaul-Store";

    private async Task<bool> TryResolveStoreAsync(CancellationToken cancellationToken)
    {
        var raw = Request.Headers[StoreHeader].FirstOrDefault();
        if (!Guid.TryParse(raw, out var organizationId))
        {
            return false;
        }

        // Organizations is not org-scoped, so this lookup is allowed anonymously.
        if (!await db.Organizations.AnyAsync(org => org.Id == organizationId, cancellationToken))
        {
            return false;
        }

        // Scopes every subsequent org-scoped query in this request to the resolved store.
        tenant.SetOrganization(organizationId);
        return true;
    }

    [HttpGet("listings")]
    public async Task<ActionResult<StorefrontPageDto<StorefrontListingDto>>> Browse(
        [FromQuery] string? category,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 24,
        CancellationToken cancellationToken = default)
    {
        if (!await TryResolveStoreAsync(cancellationToken))
        {
            return BadRequest("Unknown store.");
        }

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 60);

        var query = db.StoreListings.Where(listing => listing.Status == StoreListingStatus.Active);

        if (!string.IsNullOrWhiteSpace(category))
        {
            var categorySlug = category.Trim();
            query = query.Where(listing => listing.StoreCategory != null && listing.StoreCategory.Slug == categorySlug);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            query = query.Where(listing => EF.Functions.ILike(listing.Title, term));
        }

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(listing => listing.IsFeatured)
            .ThenByDescending(listing => listing.PublishedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(listing => new StorefrontListingDto(
                listing.Id,
                listing.Slug,
                listing.Title,
                listing.Subtitle,
                listing.ListPrice,
                listing.CompareAtPrice,
                listing.IsFeatured,
                listing.StoreCategoryId))
            .ToListAsync(cancellationToken);

        return new StorefrontPageDto<StorefrontListingDto>(items, page, pageSize, total);
    }

    [HttpGet("listings/{slug}")]
    public async Task<ActionResult<StorefrontListingDetailDto>> Detail(string slug, CancellationToken cancellationToken)
    {
        if (!await TryResolveStoreAsync(cancellationToken))
        {
            return BadRequest("Unknown store.");
        }

        var detail = await db.StoreListings
            .Where(listing => listing.Slug == slug && listing.Status == StoreListingStatus.Active)
            .Select(listing => new StorefrontListingDetailDto(
                listing.Id,
                listing.Slug,
                listing.Title,
                listing.Subtitle,
                listing.Description,
                listing.ListPrice,
                listing.CompareAtPrice,
                listing.InventoryItem!.Brand,
                listing.InventoryItem!.Condition,
                listing.MetaTitle,
                listing.MetaDescription))
            .FirstOrDefaultAsync(cancellationToken);

        return detail is null ? NotFound() : detail;
    }

    [HttpGet("categories")]
    public async Task<ActionResult<IReadOnlyList<StoreCategoryDto>>> Categories(CancellationToken cancellationToken)
    {
        if (!await TryResolveStoreAsync(cancellationToken))
        {
            return BadRequest("Unknown store.");
        }

        var categories = await db.StoreCategories
            .Where(category => category.IsActive)
            .OrderBy(category => category.SortOrder)
            .ThenBy(category => category.Name)
            .Select(category => new StoreCategoryDto(
                category.Id,
                category.Name,
                category.Slug,
                category.Description,
                category.ParentId,
                category.SortOrder,
                category.IsActive))
            .ToListAsync(cancellationToken);

        return categories.ToList();
    }
}
