using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScoutHaul.Api.Mapping;
using ScoutHaul.Api.Services;
using ScoutHaul.Application.Contracts;
using ScoutHaul.Domain.Entities;
using ScoutHaul.Infrastructure.Persistence;

namespace ScoutHaul.Api.Controllers;

[ApiController]
[Route("api/sourcing-trips")]
public sealed class SourcingTripsController(ScoutHaulDbContext db, ICurrentUserContext currentUser) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<SourcingTripDto>> Create(CreateSourcingTripRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("Trip name is required.");
        }

        if (request.CashBudget is < 0)
        {
            return BadRequest("Cash budget cannot be negative.");
        }

        var trip = new SourcingTrip
        {
            OrganizationId = currentUser.OrganizationId,
            Name = request.Name.Trim(),
            StartedAt = request.StartedAt ?? DateTimeOffset.UtcNow,
            CashBudget = request.CashBudget,
            Notes = TrimToNull(request.Notes)
        };

        db.SourcingTrips.Add(trip);
        await db.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetSummary), new { id = trip.Id }, trip.ToDto());
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<SourcingTripDto>> Update(Guid id, UpdateSourcingTripRequest request, CancellationToken cancellationToken)
    {
        var trip = await db.SourcingTrips
            .Include(item => item.Items)
            .SingleOrDefaultAsync(item => item.Id == id && item.OrganizationId == currentUser.OrganizationId, cancellationToken);

        if (trip is null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("Trip name is required.");
        }

        if (request.CashBudget is < 0)
        {
            return BadRequest("Cash budget cannot be negative.");
        }

        trip.Name = request.Name.Trim();
        trip.StartedAt = request.StartedAt ?? trip.StartedAt;
        trip.EndedAt = request.EndedAt;
        trip.CashBudget = request.CashBudget;
        trip.Notes = TrimToNull(request.Notes);
        await db.SaveChangesAsync(cancellationToken);

        return trip.ToDto();
    }

    [HttpPost("{id:guid}/items")]
    public async Task<IActionResult> AddItem(Guid id, AddItemToTripRequest request, CancellationToken cancellationToken)
    {
        var tripExists = await db.SourcingTrips.AnyAsync(
            trip => trip.Id == id && trip.OrganizationId == currentUser.OrganizationId,
            cancellationToken);

        if (!tripExists)
        {
            return NotFound("Trip was not found.");
        }

        var item = await db.InventoryItems.SingleOrDefaultAsync(
            inventoryItem => inventoryItem.Id == request.InventoryItemId && inventoryItem.OrganizationId == currentUser.OrganizationId,
            cancellationToken);

        if (item is null)
        {
            return NotFound("Inventory item was not found.");
        }

        item.SourcingTripId = id;
        await db.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SourcingTripDto>>> List(CancellationToken cancellationToken)
    {
        var trips = await db.SourcingTrips
            .Include(item => item.Items)
            .Where(item => item.OrganizationId == currentUser.OrganizationId)
            .OrderByDescending(item => item.StartedAt)
            .Take(100)
            .ToListAsync(cancellationToken);

        return trips.Select(item => item.ToDto()).ToList();
    }

    [HttpGet("{id:guid}/summary")]
    public async Task<ActionResult<SourcingTripSummaryDto>> GetSummary(Guid id, CancellationToken cancellationToken)
    {
        var trip = await db.SourcingTrips
            .Include(item => item.Items)
            .SingleOrDefaultAsync(item => item.Id == id && item.OrganizationId == currentUser.OrganizationId, cancellationToken);

        if (trip is null)
        {
            return NotFound();
        }

        var purchaseCost = trip.Items.Sum(item => item.PurchasePrice ?? 0);
        var askingPrice = trip.Items.Sum(item => item.SellerAskingPrice ?? 0);
        var remainingBudget = (trip.CashBudget ?? 0) - purchaseCost;

        return new SourcingTripSummaryDto(
            trip.Id,
            trip.Name,
            trip.Items.Count,
            purchaseCost,
            askingPrice,
            trip.CashBudget,
            remainingBudget);
    }

    private static string? TrimToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
