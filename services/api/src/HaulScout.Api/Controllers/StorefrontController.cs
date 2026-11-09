using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HaulScout.Api.Services;
using HaulScout.Application.Contracts;
using HaulScout.Application.Inventory;
using HaulScout.Application.Storefront;

namespace HaulScout.Api.Controllers;

/// <summary>
/// Public, anonymous storefront catalog consumed by the Next.js storefront. Serves ACTIVE StoreListings — the shelf
/// that follows the inventory automatically: marking an item Listed creates/activates its listing, moving it out
/// retires it. The ids and slugs here are LISTING ids, the same id space the cart and checkout consume, so one
/// identifier travels from browse to paid order. Only a safe subset of fields is exposed — never cost, supplier, or
/// internal data.
/// </summary>
[ApiController]
[Route("api/storefront")]
[AllowAnonymous]
public sealed class StorefrontController(StorefrontStoreResolver resolver, StorefrontCatalogService catalog) : HaulScoutControllerBase
{
    [HttpGet("listings")]
    public async Task<ActionResult<StorefrontPageDto<StorefrontListingDto>>> Browse(
        [FromQuery] string? category,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 24,
        CancellationToken cancellationToken = default)
    {
        if (await resolver.ResolveAsync(Request, cancellationToken) is not Guid organizationId)
        {
            return Invalid("Unknown store.");
        }

        return await catalog.BrowseAsync(organizationId, category, search, page, pageSize, cancellationToken);
    }

    [HttpGet("listings/{slug}")]
    public async Task<ActionResult<StorefrontListingDetailDto>> Detail(string slug, CancellationToken cancellationToken)
    {
        if (await resolver.ResolveAsync(Request, cancellationToken) is not Guid organizationId)
        {
            return Invalid("Unknown store.");
        }

        StorefrontListingDetailDto? detail = await catalog.FindDetailAsync(organizationId, slug, cancellationToken);
        return detail is null ? NotFound() : detail;
    }

    [HttpGet("categories")]
    public async Task<ActionResult<IReadOnlyList<StoreCategoryDto>>> Categories(CancellationToken cancellationToken)
    {
        if (await resolver.ResolveAsync(Request, cancellationToken) is not Guid organizationId)
        {
            return Invalid("Unknown store.");
        }

        return Ok(await catalog.ListCategoriesAsync(organizationId, cancellationToken));
    }

    /// <summary>Public image, addressed by unguessable photo id and fetched WITHOUT the store header — Next/Image
    /// and browsers request it directly. Visibility follows the item's listing, decided in the catalog service.</summary>
    [HttpGet("photos/{photoId:guid}")]
    public async Task<IActionResult> Photo(Guid photoId, [FromQuery] string? size, CancellationToken cancellationToken)
    {
        bool full = string.Equals(size, "full", StringComparison.OrdinalIgnoreCase);
        PhotoBytes? photo = await catalog.FindPublicPhotoAsync(photoId, full, cancellationToken);
        if (photo is null)
        {
            return Missing();
        }

        Response.Headers.CacheControl = "no-store";
        return File(photo.Bytes, photo.ContentType);
    }
}
