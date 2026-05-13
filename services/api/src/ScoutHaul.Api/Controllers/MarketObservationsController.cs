using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScoutHaul.Api.Authorization;
using ScoutHaul.Api.Mapping;
using ScoutHaul.Api.Services;
using ScoutHaul.Application.Contracts;
using ScoutHaul.Domain;
using ScoutHaul.Domain.Entities;
using ScoutHaul.Infrastructure.Persistence;

namespace ScoutHaul.Api.Controllers;

[ApiController]
[Route("api/market-observations")]
[RequireOrganizationPermission(ScoutHaulPermissions.ManageInventory)]
public sealed class MarketObservationsController(ScoutHaulDbContext db, ICurrentUserContext currentUser) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<MarketObservationDto>> Add(UpsertMarketObservationRequest request, CancellationToken cancellationToken)
    {
        var validation = Validate(request);
        if (validation is not null)
        {
            return BadRequest(validation);
        }

        if (!await ReferencesBelongToOrganization(request, cancellationToken))
        {
            return BadRequest("Referenced product identity or inventory item was not found for the current organization.");
        }

        var observation = new MarketObservation
        {
            OrganizationId = currentUser.OrganizationId,
            ObservationType = request.ObservationType,
            Source = request.Source.Trim(),
            Title = request.Title.Trim(),
            Price = request.Price,
            ShippingPrice = request.ShippingPrice,
            TotalPrice = request.Price + (request.ShippingPrice ?? 0),
            ObservedAt = request.ObservedAt ?? DateTimeOffset.UtcNow
        };

        Apply(request, observation);
        db.MarketObservations.Add(observation);
        await db.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(List), new { productIdentityId = observation.ProductIdentityId }, observation.ToDto());
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<MarketObservationDto>> Update(Guid id, UpsertMarketObservationRequest request, CancellationToken cancellationToken)
    {
        var observation = await db.MarketObservations.SingleOrDefaultAsync(
            item => item.Id == id && item.OrganizationId == currentUser.OrganizationId && !item.IsArchived,
            cancellationToken);

        if (observation is null)
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
            return BadRequest("Referenced product identity or inventory item was not found for the current organization.");
        }

        Apply(request, observation);
        await db.SaveChangesAsync(cancellationToken);

        return observation.ToDto();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var observation = await db.MarketObservations.SingleOrDefaultAsync(
            item => item.Id == id && item.OrganizationId == currentUser.OrganizationId && !item.IsArchived,
            cancellationToken);

        if (observation is null)
        {
            return NotFound();
        }

        observation.IsArchived = true;
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MarketObservationDto>>> List(
        [FromQuery] Guid? productIdentityId,
        [FromQuery] Guid? inventoryItemId,
        [FromQuery] string? source,
        [FromQuery] MarketObservationType? type,
        CancellationToken cancellationToken)
    {
        var query = db.MarketObservations.Where(item => item.OrganizationId == currentUser.OrganizationId && !item.IsArchived);

        if (productIdentityId.HasValue)
        {
            query = query.Where(item => item.ProductIdentityId == productIdentityId.Value);
        }

        if (inventoryItemId.HasValue)
        {
            query = query.Where(item => item.InventoryItemId == inventoryItemId.Value);
        }

        if (!string.IsNullOrWhiteSpace(source))
        {
            var normalized = source.Trim().ToLowerInvariant();
            query = query.Where(item => item.Source.ToLower() == normalized);
        }

        if (type.HasValue)
        {
            query = query.Where(item => item.ObservationType == type.Value);
        }

        var observations = await query
            .OrderByDescending(item => item.ObservedAt)
            .Take(200)
            .Select(item => item.ToDto())
            .ToListAsync(cancellationToken);

        return observations;
    }

    private static string? Validate(UpsertMarketObservationRequest request)
    {
        if (request.ProductIdentityId is null && request.InventoryItemId is null)
        {
            return "A comp must be attached to either a product identity or inventory item.";
        }

        if (string.IsNullOrWhiteSpace(request.Source))
        {
            return "Source is required.";
        }

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return "Title is required.";
        }

        if (request.Price < 0 || request.ShippingPrice is < 0)
        {
            return "Prices cannot be negative.";
        }

        if (request.ObservationType is MarketObservationType.Sold or MarketObservationType.ManualSold or MarketObservationType.InternalSale
            && request.SoldAt.HasValue
            && request.SoldAt.Value > DateTimeOffset.UtcNow.AddMinutes(5))
        {
            return "Sold timestamp cannot be in the future.";
        }

        return null;
    }

    private static void Apply(UpsertMarketObservationRequest request, MarketObservation observation)
    {
        DateTimeOffset observedAt = request.ObservedAt ?? observation.ObservedAt;
        observation.ProductIdentityId = request.ProductIdentityId;
        observation.InventoryItemId = request.InventoryItemId;
        observation.ObservationType = request.ObservationType;
        observation.Source = request.Source.Trim();
        observation.SourceUrl = TrimToNull(request.SourceUrl);
        observation.Title = request.Title.Trim();
        observation.Price = request.Price;
        observation.ShippingPrice = request.ShippingPrice;
        observation.TotalPrice = request.Price + (request.ShippingPrice ?? 0);
        observation.Currency = TrimToNull(request.Currency) ?? "USD";
        observation.Condition = TrimToNull(request.Condition);
        observation.ObservedAt = observedAt;
        observation.SoldAt = IsSoldLike(request.ObservationType) ? request.SoldAt ?? observedAt : null;
        observation.Notes = TrimToNull(request.Notes);
    }

    private async Task<bool> ReferencesBelongToOrganization(UpsertMarketObservationRequest request, CancellationToken cancellationToken)
    {
        if (request.ProductIdentityId.HasValue)
        {
            bool productExists = await db.ProductIdentities.AnyAsync(
                item => item.Id == request.ProductIdentityId.Value && item.OrganizationId == currentUser.OrganizationId,
                cancellationToken);

            if (!productExists)
            {
                return false;
            }
        }

        if (request.InventoryItemId.HasValue)
        {
            bool inventoryItemExists = await db.InventoryItems.AnyAsync(
                item => item.Id == request.InventoryItemId.Value && item.OrganizationId == currentUser.OrganizationId,
                cancellationToken);

            if (!inventoryItemExists)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsSoldLike(MarketObservationType observationType) =>
        observationType is MarketObservationType.Sold
            or MarketObservationType.ManualSold
            or MarketObservationType.InternalSale;

    private static string? TrimToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
