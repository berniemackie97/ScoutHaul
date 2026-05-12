using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScoutHaul.Api.Mapping;
using ScoutHaul.Api.Services;
using ScoutHaul.Application.Contracts;
using ScoutHaul.Domain;
using ScoutHaul.Domain.Entities;
using ScoutHaul.Infrastructure.Persistence;

namespace ScoutHaul.Api.Controllers;

[ApiController]
[Route("api/listings")]
public sealed class ListingsController(ScoutHaulDbContext db, ICurrentUserContext currentUser) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<ListingDto>> Create(CreateListingRequest request, CancellationToken cancellationToken)
    {
        var item = await db.InventoryItems.SingleOrDefaultAsync(
            inventoryItem => inventoryItem.Id == request.InventoryItemId && inventoryItem.OrganizationId == currentUser.OrganizationId,
            cancellationToken);

        if (item is null)
        {
            return NotFound("Inventory item was not found.");
        }

        if (string.IsNullOrWhiteSpace(request.Platform) || request.ListPrice <= 0)
        {
            return BadRequest("Platform and positive list price are required.");
        }

        var listing = new Listing
        {
            OrganizationId = currentUser.OrganizationId,
            InventoryItemId = request.InventoryItemId,
            Platform = request.Platform.Trim(),
            ListingUrl = TrimToNull(request.ListingUrl),
            ListPrice = request.ListPrice,
            Status = ListingStatus.Draft,
            Notes = TrimToNull(request.Notes)
        };

        db.Listings.Add(listing);
        await db.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = listing.Id }, listing.ToDto());
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ListingDto>> Update(Guid id, UpdateListingRequest request, CancellationToken cancellationToken)
    {
        var listing = await FindListing(id, cancellationToken);
        if (listing is null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.Platform) || request.ListPrice <= 0)
        {
            return BadRequest("Platform and positive list price are required.");
        }

        listing.Platform = request.Platform.Trim();
        listing.ListingUrl = TrimToNull(request.ListingUrl);
        listing.ListPrice = request.ListPrice;
        listing.Status = request.Status;
        listing.Notes = TrimToNull(request.Notes);
        listing.ListedAt ??= request.Status == ListingStatus.Active ? DateTimeOffset.UtcNow : null;
        listing.EndedAt ??= request.Status is ListingStatus.Ended or ListingStatus.Sold or ListingStatus.Cancelled ? DateTimeOffset.UtcNow : null;

        await db.SaveChangesAsync(cancellationToken);
        return listing.ToDto();
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ListingDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var listing = await FindListing(id, cancellationToken);
        return listing is null ? NotFound() : listing.ToDto();
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ListingDto>>> List([FromQuery] ListingStatus? status, CancellationToken cancellationToken)
    {
        var query = db.Listings.Where(item => item.OrganizationId == currentUser.OrganizationId);
        if (status.HasValue)
        {
            query = query.Where(item => item.Status == status.Value);
        }

        var listings = await query
            .OrderByDescending(item => item.UpdatedAt)
            .Take(200)
            .Select(item => item.ToDto())
            .ToListAsync(cancellationToken);

        return listings;
    }

    [HttpPost("{id:guid}/mark-listed")]
    public async Task<ActionResult<ListingDto>> MarkListed(Guid id, CancellationToken cancellationToken)
    {
        var listing = await db.Listings
            .Include(item => item.InventoryItem)
            .SingleOrDefaultAsync(item => item.Id == id && item.OrganizationId == currentUser.OrganizationId, cancellationToken);

        if (listing is null)
        {
            return NotFound();
        }

        var now = DateTimeOffset.UtcNow;
        listing.Status = ListingStatus.Active;
        listing.ListedAt ??= now;
        listing.InventoryItem!.Status = InventoryStatus.Listed;
        listing.InventoryItem.ListedAt ??= now;
        await db.SaveChangesAsync(cancellationToken);

        return listing.ToDto();
    }

    private Task<Listing?> FindListing(Guid id, CancellationToken cancellationToken) =>
        db.Listings.SingleOrDefaultAsync(item => item.Id == id && item.OrganizationId == currentUser.OrganizationId, cancellationToken);

    private static string? TrimToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
