using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScoutHaul.Api.Authorization;
using ScoutHaul.Api.Mapping;
using ScoutHaul.Api.Services;
using ScoutHaul.Application.Contracts;
using ScoutHaul.Application.Services;
using static ScoutHaul.Api.Helpers.StringHelpers;
using ScoutHaul.Domain;
using ScoutHaul.Domain.Entities;
using ScoutHaul.Infrastructure.Persistence;

namespace ScoutHaul.Api.Controllers;

[ApiController]
[Route("api/sales")]
[RequireOrganizationPermission(ScoutHaulPermissions.ManageListings)]
public sealed class SalesController(ScoutHaulDbContext db, ICurrentUserContext currentUser) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SaleDto>>> List(
        [FromQuery] SaleStatus? status,
        CancellationToken cancellationToken)
    {
        var query = db.Sales
            .Include(item => item.InventoryItem)
            .Where(item => item.OrganizationId == currentUser.OrganizationId);

        if (status.HasValue)
        {
            query = query.Where(item => item.Status == status.Value);
        }

        var sales = await query
            .OrderByDescending(item => item.SoldAt)
            .Take(200)
            .Select(item => item.ToDto())
            .ToListAsync(cancellationToken);

        return sales;
    }

    [HttpPost]
    public async Task<ActionResult<SaleDto>> Record(RecordSaleRequest request, CancellationToken cancellationToken)
    {
        var item = await db.InventoryItems.SingleOrDefaultAsync(
            inventoryItem => inventoryItem.Id == request.InventoryItemId && inventoryItem.OrganizationId == currentUser.OrganizationId,
            cancellationToken);

        if (item is null)
        {
            return NotFound("Inventory item was not found.");
        }

        if (string.IsNullOrWhiteSpace(request.Platform) || request.SoldPrice <= 0)
        {
            return BadRequest("Platform and positive sold price are required.");
        }

        if (request.PlatformFees < 0 || request.PaymentFees < 0 || request.ShippingCost < 0 || request.SuppliesCost < 0 || request.OtherCosts < 0)
        {
            return BadRequest("Costs and fees cannot be negative.");
        }

        bool alreadySold = await db.Sales.AnyAsync(
            saleItem => saleItem.InventoryItemId == request.InventoryItemId
                        && saleItem.OrganizationId == currentUser.OrganizationId
                        && saleItem.Status != SaleStatus.Returned,
            cancellationToken);

        if (alreadySold)
        {
            return Conflict("Inventory item already has a recorded sale.");
        }

        Listing? listing = null;
        if (request.ListingId.HasValue)
        {
            listing = await db.Listings.SingleOrDefaultAsync(
                listingItem => listingItem.Id == request.ListingId.Value && listingItem.OrganizationId == currentUser.OrganizationId,
                cancellationToken);

            if (listing is null)
            {
                return NotFound("Listing was not found.");
            }

            if (listing.InventoryItemId != item.Id)
            {
                return BadRequest("Listing does not belong to the supplied inventory item.");
            }
        }

        RealizedSaleFinancials financials = ResaleFinancialCalculator.CalculateRealizedSale(
            request.SoldPrice,
            item.PurchasePrice ?? 0,
            request.PlatformFees,
            request.PaymentFees,
            request.ShippingCost,
            request.SuppliesCost,
            request.OtherCosts);

        var sale = new Sale
        {
            OrganizationId = currentUser.OrganizationId,
            InventoryItemId = request.InventoryItemId,
            ListingId = request.ListingId,
            Platform = request.Platform.Trim(),
            SoldPrice = request.SoldPrice,
            PlatformFees = request.PlatformFees,
            PaymentFees = request.PaymentFees,
            ShippingCost = request.ShippingCost,
            SuppliesCost = request.SuppliesCost,
            OtherCosts = request.OtherCosts,
            NetProfit = financials.NetProfit,
            SoldAt = request.SoldAt ?? DateTimeOffset.UtcNow,
            Notes = TrimToNull(request.Notes)
        };

        item.Status = InventoryStatus.Sold;
        item.SoldAt = sale.SoldAt;

        if (listing is not null)
        {
            listing.Status = ListingStatus.Sold;
            listing.EndedAt ??= sale.SoldAt;
        }

        db.Sales.Add(sale);
        sale.InventoryItem = item;
        db.MarketObservations.Add(new MarketObservation
        {
            OrganizationId = currentUser.OrganizationId,
            ProductIdentityId = item.ProductIdentityId,
            InventoryItemId = item.Id,
            ObservationType = MarketObservationType.InternalSale,
            Source = "Internal",
            Title = item.Title,
            Price = request.SoldPrice,
            TotalPrice = request.SoldPrice,
            Condition = item.Condition,
            ObservedAt = sale.SoldAt,
            SoldAt = sale.SoldAt,
            Notes = "Created from recorded sale."
        });

        await db.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(GetProfit), new { id = sale.Id }, sale.ToDto());
    }

    [HttpGet("{id:guid}/profit")]
    public async Task<ActionResult<RealizedProfitDto>> GetProfit(Guid id, CancellationToken cancellationToken)
    {
        var sale = await db.Sales
            .Include(item => item.InventoryItem)
            .SingleOrDefaultAsync(item => item.Id == id && item.OrganizationId == currentUser.OrganizationId, cancellationToken);

        if (sale is null)
        {
            return NotFound();
        }

        RealizedSaleFinancials financials = ResaleFinancialCalculator.CalculateRealizedSale(
            sale.SoldPrice,
            sale.InventoryItem?.PurchasePrice ?? 0,
            sale.PlatformFees,
            sale.PaymentFees,
            sale.ShippingCost,
            sale.SuppliesCost,
            sale.OtherCosts);

        return new RealizedProfitDto(
            sale.Id,
            financials.SoldPrice,
            financials.PurchaseCost,
            financials.TotalFeesAndCosts,
            financials.NetProfit,
            financials.RoiPercent,
            financials.MarginPercent);
    }

    [HttpPatch("{id:guid}/packed")]
    public Task<ActionResult<SaleDto>> MarkPacked(Guid id, CancellationToken cancellationToken) =>
        MarkStatus(id, SaleStatus.Packed, InventoryStatus.Packed, cancellationToken);

    [HttpPatch("{id:guid}/shipped")]
    public Task<ActionResult<SaleDto>> MarkShipped(Guid id, CancellationToken cancellationToken) =>
        MarkStatus(id, SaleStatus.Shipped, InventoryStatus.Shipped, cancellationToken);

    [HttpPatch("{id:guid}/delivered")]
    public Task<ActionResult<SaleDto>> MarkDelivered(Guid id, CancellationToken cancellationToken) =>
        MarkStatus(id, SaleStatus.Delivered, InventoryStatus.Delivered, cancellationToken);

    private async Task<ActionResult<SaleDto>> MarkStatus(Guid id, SaleStatus saleStatus, InventoryStatus inventoryStatus, CancellationToken cancellationToken)
    {
        var sale = await db.Sales
            .Include(item => item.InventoryItem)
            .SingleOrDefaultAsync(item => item.Id == id && item.OrganizationId == currentUser.OrganizationId, cancellationToken);

        if (sale is null)
        {
            return NotFound();
        }

        sale.Status = saleStatus;
        if (sale.InventoryItem is not null)
        {
            sale.InventoryItem.Status = inventoryStatus;
        }

        await db.SaveChangesAsync(cancellationToken);
        return sale.ToDto();
    }

}
