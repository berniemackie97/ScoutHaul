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
[Route("api/inventory")]
public sealed class InventoryController(ScoutHaulDbContext db, ICurrentUserContext currentUser) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<InventoryItemDto>> Create(UpsertInventoryItemRequest request, CancellationToken cancellationToken)
    {
        var validation = Validate(request);
        if (validation is not null)
        {
            return BadRequest(validation);
        }

        if (!await ReferencesBelongToOrganization(request, cancellationToken))
        {
            return BadRequest("One or more inventory references do not belong to this organization.");
        }

        var item = new InventoryItem
        {
            OrganizationId = currentUser.OrganizationId,
            Title = request.Title.Trim(),
            Status = request.PurchasePrice.HasValue ? InventoryStatus.Purchased : InventoryStatus.Draft,
            PurchasedAt = request.PurchasePrice.HasValue ? DateTimeOffset.UtcNow : null
        };

        Apply(request, item);
        db.InventoryItems.Add(item);
        await db.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = item.Id }, item.ToDto());
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<InventoryItemDto>> Update(Guid id, UpsertInventoryItemRequest request, CancellationToken cancellationToken)
    {
        var item = await FindItem(id, cancellationToken);
        if (item is null)
        {
            return NotFound();
        }

        var validation = Validate(request);
        if (validation is not null)
        {
            return BadRequest(validation);
        }

        if (!await ReferencesBelongToOrganization(request, cancellationToken))
        {
            return BadRequest("One or more inventory references do not belong to this organization.");
        }

        Apply(request, item);
        await db.SaveChangesAsync(cancellationToken);

        return item.ToDto();
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<InventoryItemDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var item = await FindItem(id, cancellationToken);
        return item is null ? NotFound() : item.ToDto();
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<InventoryItemDto>>> List(
        [FromQuery] InventoryStatus? status,
        [FromQuery] bool includeArchived,
        CancellationToken cancellationToken)
    {
        var query = db.InventoryItems.Where(item => item.OrganizationId == currentUser.OrganizationId);

        if (!includeArchived)
        {
            query = query.Where(item => !item.IsArchived);
        }

        if (status.HasValue)
        {
            query = query.Where(item => item.Status == status.Value);
        }

        var items = await query
            .OrderByDescending(item => item.UpdatedAt)
            .Take(200)
            .Select(item => item.ToDto())
            .ToListAsync(cancellationToken);

        return items;
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<InventoryItemDto>> ChangeStatus(Guid id, ChangeInventoryStatusRequest request, CancellationToken cancellationToken)
    {
        var item = await FindItem(id, cancellationToken);
        if (item is null)
        {
            return NotFound();
        }

        item.Status = request.Status;
        var now = DateTimeOffset.UtcNow;
        item.PurchasedAt ??= request.Status == InventoryStatus.Purchased ? now : null;
        item.ListedAt ??= request.Status == InventoryStatus.Listed ? now : null;
        item.SoldAt ??= request.Status == InventoryStatus.Sold ? now : null;

        await db.SaveChangesAsync(cancellationToken);
        return item.ToDto();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Archive(Guid id, CancellationToken cancellationToken)
    {
        var item = await FindItem(id, cancellationToken);
        if (item is null)
        {
            return NotFound();
        }

        item.IsArchived = true;
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private Task<InventoryItem?> FindItem(Guid id, CancellationToken cancellationToken) =>
        db.InventoryItems.SingleOrDefaultAsync(
            item => item.Id == id && item.OrganizationId == currentUser.OrganizationId,
            cancellationToken);

    private async Task<bool> ReferencesBelongToOrganization(UpsertInventoryItemRequest request, CancellationToken cancellationToken)
    {
        if (request.SourcingTripStopId.HasValue)
        {
            var stop = await db.SourcingTripStops
                .Where(item => item.Id == request.SourcingTripStopId.Value && item.OrganizationId == currentUser.OrganizationId)
                .Select(item => new { item.SourcingTripId })
                .SingleOrDefaultAsync(cancellationToken);

            if (stop is null)
            {
                return false;
            }

            if (request.SourcingTripId.HasValue && stop.SourcingTripId != request.SourcingTripId.Value)
            {
                return false;
            }
        }

        if (request.SourcingTripId.HasValue)
        {
            bool tripExists = await db.SourcingTrips.AnyAsync(
                item => item.Id == request.SourcingTripId.Value && item.OrganizationId == currentUser.OrganizationId,
                cancellationToken);

            if (!tripExists)
            {
                return false;
            }
        }

        return true;
    }

    private static string? Validate(UpsertInventoryItemRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return "Item title is required.";
        }

        if (request.SellerAskingPrice is < 0 || request.PurchasePrice is < 0 || request.EstimatedSalePrice is < 0)
        {
            return "Prices cannot be negative.";
        }

        return null;
    }

    private static void Apply(UpsertInventoryItemRequest request, InventoryItem item)
    {
        item.ProductIdentityId = request.ProductIdentityId;
        item.SourcingTripId = request.SourcingTripId;
        item.SourcingTripStopId = request.SourcingTripStopId;
        item.SourceLocationId = request.SourceLocationId;
        item.Title = request.Title.Trim();
        item.Description = TrimToNull(request.Description);
        item.Condition = TrimToNull(request.Condition);
        item.SellerAskingPrice = request.SellerAskingPrice;
        item.PurchasePrice = request.PurchasePrice;
        item.EstimatedSalePrice = request.EstimatedSalePrice;
        item.Barcode = TrimToNull(request.Barcode);
        item.Notes = TrimToNull(request.Notes);
    }

    private static string? TrimToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
