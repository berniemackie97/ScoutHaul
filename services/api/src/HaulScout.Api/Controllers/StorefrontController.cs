using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Api.Controllers;

/// <summary>
/// Public, anonymous storefront catalog read API consumed by the Next.js storefront.
/// Serves the organisation's REAL sellable inventory directly — one shared inventory,
/// no separate "publish" step. The store (organization) is resolved from the
/// X-HaulScout-Store header. Only a safe subset of fields is exposed — never cost,
/// supplier, or internal data.
/// </summary>
[ApiController]
[Route("api/storefront")]
[AllowAnonymous]
public sealed class StorefrontController(HaulScoutDbContext db, ITenantContext tenant) : ControllerBase
{
    private const string StoreHeader = "X-HaulScout-Store";

    // Inventory a customer may actually buy: priced, not archived, and marked ready/listed.
    // (Quantity is intentionally NOT a gate — operators use 0 for single, untracked items;
    // availability is governed by Status. Sold/Shipped items fall out via their status.)
    private IQueryable<InventoryItem> SellableItems() =>
        db.InventoryItems.Where(i =>
            !i.IsArchived
            && (i.Status == InventoryStatus.Listed || i.Status == InventoryStatus.ReadyToList)
            && (i.EstimatedSalePrice ?? i.SellerAskingPrice) > 0m
            && i.OwnershipType != InventoryOwnershipType.CustomerRepair
            && i.AuthenticityStatus != InventoryAuthenticityStatus.Counterfeit
            && i.AuthenticityStatus != InventoryAuthenticityStatus.NeedsVerification);

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

        IQueryable<InventoryItem> query = SellableItems();

        if (!string.IsNullOrWhiteSpace(category))
        {
            string slug = category.Trim();
            string? name = (await query
                    .Where(i => i.Category != null && i.Category != "")
                    .Select(i => i.Category!)
                    .Distinct()
                    .ToListAsync(cancellationToken))
                .FirstOrDefault(n => Slugify(n) == slug);
            query = name is null ? query.Where(_ => false) : query.Where(i => i.Category == name);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = $"%{search.Trim()}%";
            query = query.Where(i =>
                EF.Functions.ILike(i.Title, term)
                || (i.Brand != null && EF.Functions.ILike(i.Brand, term)));
        }

        int total = await query.CountAsync(cancellationToken);

        var rows = await query
            .OrderByDescending(i => i.ListedAt ?? i.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(i => new
            {
                i.Id,
                i.Title,
                i.Brand,
                i.Category,
                i.Condition,
                ListPrice = i.EstimatedSalePrice ?? i.SellerAskingPrice ?? 0m,
                HeroPhotoId = i.Photos
                    .OrderByDescending(p => p.IsHero)
                    .ThenBy(p => p.SortOrder)
                    .Select(p => (Guid?)p.Id)
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        List<StorefrontListingDto> items = rows
            .Select(r => new StorefrontListingDto(
                r.Id, BuildSlug(r.Title, r.Id), r.Title, null, r.ListPrice, null,
                false, null, r.Condition, r.Brand, PhotoUrl(r.HeroPhotoId), r.Category))
            .ToList();

        return new StorefrontPageDto<StorefrontListingDto>(items, page, pageSize, total);
    }

    [HttpGet("listings/{slug}")]
    public async Task<ActionResult<StorefrontListingDetailDto>> Detail(string slug, CancellationToken cancellationToken)
    {
        if (!await TryResolveStoreAsync(cancellationToken))
        {
            return BadRequest("Unknown store.");
        }

        if (!TryGetSlugId(slug, out Guid itemId))
        {
            return NotFound();
        }

        var row = await SellableItems()
            .Where(i => i.Id == itemId)
            .Select(i => new
            {
                i.Id,
                i.Title,
                i.Description,
                i.Brand,
                i.Model,
                i.Category,
                i.Color,
                i.Condition,
                i.SizeSystem,
                i.SizeValue,
                i.FunctionalStatus,
                i.CompletenessStatus,
                i.PackagingStatus,
                i.CountryOfOrigin,
                i.ManufactureYear,
                i.Length,
                i.Width,
                i.Height,
                i.DimensionUnit,
                i.Weight,
                i.WeightUnit,
                ListPrice = i.EstimatedSalePrice ?? i.SellerAskingPrice ?? 0m,
                PhotoIds = i.Photos
                    .OrderByDescending(p => p.IsHero)
                    .ThenBy(p => p.SortOrder)
                    .Select(p => p.Id)
                    .ToList(),
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return NotFound();
        }

        List<string> gallery = row.PhotoIds
            .Select(id => PhotoUrl(id))
            .Where(url => url is not null)
            .Select(url => url!)
            .ToList();

        return new StorefrontListingDetailDto(
            row.Id, BuildSlug(row.Title, row.Id), row.Title, null, row.Description,
            row.ListPrice, null, row.Brand, row.Condition, row.Title, row.Description,
            gallery.FirstOrDefault(), row.Category, row.Model, row.Color, row.SizeSystem, row.SizeValue,
            row.FunctionalStatus, row.CompletenessStatus, row.PackagingStatus, row.CountryOfOrigin,
            row.ManufactureYear, row.Length, row.Width, row.Height, row.DimensionUnit, row.Weight, row.WeightUnit,
            gallery);
    }

    [HttpGet("categories")]
    public async Task<ActionResult<IReadOnlyList<StoreCategoryDto>>> Categories(CancellationToken cancellationToken)
    {
        if (!await TryResolveStoreAsync(cancellationToken))
        {
            return BadRequest("Unknown store.");
        }

        var groups = await SellableItems()
            .Where(i => i.Category != null && i.Category != "")
            .GroupBy(i => i.Category!)
            .Select(g => new { Name = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .ToListAsync(cancellationToken);

        List<StoreCategoryDto> categories = groups
            .Select((c, idx) => new StoreCategoryDto(Guid.Empty, c.Name, Slugify(c.Name), null, null, idx, true))
            .ToList();

        return categories;
    }

    [HttpGet("photos/{photoId:guid}")]
    public async Task<IActionResult> Photo(Guid photoId, [FromQuery] string? size, CancellationToken cancellationToken)
    {
        // Public image: addressed by an unguessable photo id and served WITHOUT the store
        // header — Next/Image and browsers fetch it directly. Safe-gated to photos of a
        // sellable item belonging to a store with a public domain; tenant filter bypassed.
        bool full = string.Equals(size, "full", StringComparison.OrdinalIgnoreCase);
        var photo = await db.InventoryPhotos
            .IgnoreQueryFilters()
            .Where(p => p.Id == photoId
                && p.InventoryItem!.Organization!.StoreDomain != null
                && !p.InventoryItem.IsArchived
                && (p.InventoryItem.Status == InventoryStatus.Listed || p.InventoryItem.Status == InventoryStatus.ReadyToList)
                && p.InventoryItem.OwnershipType != InventoryOwnershipType.CustomerRepair
                && p.InventoryItem.AuthenticityStatus != InventoryAuthenticityStatus.Counterfeit
                && p.InventoryItem.AuthenticityStatus != InventoryAuthenticityStatus.NeedsVerification)
            .Select(p => new { Bytes = full ? p.OriginalBytes : p.ThumbnailBytes, p.ContentType })
            .FirstOrDefaultAsync(cancellationToken);

        if (photo is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        return File(photo.Bytes, full ? photo.ContentType : "image/jpeg");
    }

    // ── store resolution + helpers ──────────────────────────────────────────

    private async Task<bool> TryResolveStoreAsync(CancellationToken cancellationToken)
    {
        string? raw = Request.Headers[StoreHeader].FirstOrDefault()?.Trim();
        if (string.IsNullOrEmpty(raw))
        {
            return false;
        }

        // The store is identified by its domain (e.g. "overhaultrading.com") so no org id
        // is hardcoded in the storefront. A raw org id is still accepted for tooling/back-compat.
        // Organizations is not org-scoped, so these lookups are allowed anonymously.
        Guid organizationId;
        if (Guid.TryParse(raw, out Guid id))
        {
            organizationId = await db.Organizations
                .Where(org => org.Id == id)
                .Select(org => org.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }
        else
        {
            string domain = NormalizeDomain(raw);
            organizationId = await db.Organizations
                .Where(org => org.StoreDomain != null && org.StoreDomain.ToLower() == domain)
                .Select(org => org.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (organizationId == Guid.Empty)
        {
            return false;
        }

        // Scopes every subsequent org-scoped query in this request to the resolved store.
        tenant.SetOrganization(organizationId);
        return true;
    }

    private static string NormalizeDomain(string value)
    {
        string domain = value.ToLowerInvariant();
        if (domain.StartsWith("www.", StringComparison.Ordinal))
        {
            domain = domain[4..];
        }

        int colon = domain.IndexOf(':');
        return colon >= 0 ? domain[..colon] : domain;
    }

    private static string? PhotoUrl(Guid? photoId) =>
        photoId is null ? null : $"/api/storefront/photos/{photoId}";

    private static string Slugify(string value)
    {
        StringBuilder sb = new(value.Length);
        foreach (char ch in value.ToLowerInvariant())
        {
            sb.Append(char.IsLetterOrDigit(ch) ? ch : '-');
        }

        string slug = sb.ToString();
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-");
        }

        return slug.Trim('-');
    }

    // Stable, unique slug derived from the item: "{title-slug}-{id:N}".
    private static string BuildSlug(string title, Guid id) => $"{Slugify(title)}-{id:N}";

    private static bool TryGetSlugId(string slug, out Guid id)
    {
        id = Guid.Empty;
        return !string.IsNullOrEmpty(slug)
            && slug.Length >= 32
            && Guid.TryParseExact(slug[^32..], "N", out id);
    }
}
