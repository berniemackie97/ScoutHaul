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
[Route("api/listings")]
[RequireOrganizationPermission(HaulScoutPermissions.ManageListings)]
public sealed class ListingsController(HaulScoutDbContext db, ICurrentUserContext currentUser) : HaulScoutControllerBase
{
    [HttpPost]
    public async Task<ActionResult<ListingDto>> Create(CreateListingRequest request, CancellationToken cancellationToken)
    {
        InventoryItem? item = await db.InventoryItems.SingleOrDefaultAsync(
            inventoryItem => inventoryItem.Id == request.InventoryItemId && inventoryItem.OrganizationId == currentUser.OrganizationId,
            cancellationToken);

        if (item is null)
        {
            return Missing("Inventory item was not found.");
        }

        if (string.IsNullOrWhiteSpace(request.Platform) || request.ListPrice <= 0)
        {
            return Invalid("Platform and positive list price are required.");
        }

        if (ContentTooLong(request.Title, request.Description) is string contentProblem)
        {
            return Invalid(contentProblem);
        }

        Listing listing = new()
        {
            OrganizationId = currentUser.OrganizationId,
            InventoryItemId = request.InventoryItemId,
            InventoryItem = item,
            Platform = request.Platform.Trim(),
            ListingUrl = TrimToNull(request.ListingUrl),
            ListPrice = request.ListPrice,
            Status = ListingStatus.Draft,
            Notes = TrimToNull(request.Notes),
            Title = TrimToNull(request.Title),
            Description = TrimToNull(request.Description)
        };

        db.Listings.Add(listing);
        await db.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = listing.Id }, listing.ToDto());
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ListingDto>> Update(Guid id, UpdateListingRequest request, CancellationToken cancellationToken)
    {
        Listing? listing = await FindListing(id, cancellationToken);
        if (listing is null)
        {
            return Missing();
        }

        if (string.IsNullOrWhiteSpace(request.Platform) || request.ListPrice <= 0)
        {
            return Invalid("Platform and positive list price are required.");
        }

        if (ContentTooLong(request.Title, request.Description) is string contentProblem)
        {
            return Invalid(contentProblem);
        }

        listing.Platform = request.Platform.Trim();
        listing.ListingUrl = TrimToNull(request.ListingUrl);
        listing.ListPrice = request.ListPrice;
        listing.Status = request.Status;
        listing.Notes = TrimToNull(request.Notes);
        listing.Title = TrimToNull(request.Title);
        listing.Description = TrimToNull(request.Description);
        listing.ListedAt ??= request.Status == ListingStatus.Active ? DateTimeOffset.UtcNow : null;
        listing.EndedAt ??= request.Status is ListingStatus.Ended or ListingStatus.Sold or ListingStatus.Cancelled ? DateTimeOffset.UtcNow : null;

        await db.SaveChangesAsync(cancellationToken);
        return listing.ToDto();
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ListingDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        Listing? listing = await FindListing(id, cancellationToken);
        return listing is null ? NotFound() : listing.ToDto();
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ListingDto>>> List([FromQuery] ListingStatus? status, CancellationToken cancellationToken)
    {
        IQueryable<Listing> query = db.Listings.Where(item => item.OrganizationId == currentUser.OrganizationId);
        if (status.HasValue)
        {
            query = query.Where(item => item.Status == status.Value);
        }

        List<Listing> listings = await query
            .Include(item => item.InventoryItem)
            .OrderByDescending(item => item.UpdatedAt)
            .Take(200)
            .ToListAsync(cancellationToken);

        return listings.Select(item => item.ToDto()).ToList();
    }

    [HttpPost("{id:guid}/mark-listed")]
    public async Task<ActionResult<ListingDto>> MarkListed(Guid id, CancellationToken cancellationToken)
    {
        Listing? listing = await db.Listings
            .Include(item => item.InventoryItem)
            .SingleOrDefaultAsync(item => item.Id == id && item.OrganizationId == currentUser.OrganizationId, cancellationToken);

        if (listing is null)
        {
            return Missing();
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        listing.Status = ListingStatus.Active;
        listing.ListedAt ??= now;
        listing.InventoryItem!.MarkListed(now);
        await db.SaveChangesAsync(cancellationToken);

        return listing.ToDto();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        Listing? listing = await FindListing(id, cancellationToken);
        if (listing is null)
        {
            return Missing();
        }

        db.Listings.Remove(listing);
        await db.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    private static string? ContentTooLong(string? title, string? description) => title?.Trim().Length switch
    {
        > ListingLimits.TitleMaxLength => $"Title must be {ListingLimits.TitleMaxLength} characters or fewer.",
        _ => description?.Trim().Length > ListingLimits.DescriptionMaxLength
            ? $"Description must be {ListingLimits.DescriptionMaxLength} characters or fewer."
            : null
    };

    private Task<Listing?> FindListing(Guid id, CancellationToken cancellationToken) =>
        db.Listings
            .Include(item => item.InventoryItem)
            .SingleOrDefaultAsync(item => item.Id == id && item.OrganizationId == currentUser.OrganizationId, cancellationToken);

}
