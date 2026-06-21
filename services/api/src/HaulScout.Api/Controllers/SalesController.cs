using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HaulScout.Api.Authorization;
using HaulScout.Api.Mapping;
using HaulScout.Api.Services;
using HaulScout.Application.Contracts;
using HaulScout.Application.Services;
using static HaulScout.Api.Helpers.StringHelpers;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Api.Controllers;

[ApiController]
[Route("api/sales")]
[RequireOrganizationPermission(HaulScoutPermissions.ManageListings)]
public sealed class SalesController(HaulScoutDbContext db, ICurrentUserContext currentUser) : ControllerBase
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
            .ToListAsync(cancellationToken);

        return sales.Select(item => item.ToDto()).ToList();
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SaleDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var sale = await db.Sales
            .Include(item => item.InventoryItem)
            .SingleOrDefaultAsync(item => item.Id == id && item.OrganizationId == currentUser.OrganizationId, cancellationToken);

        return sale is null ? NotFound() : sale.ToDto();
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

        if (request.PlatformFees < 0 || request.PaymentFees < 0 || request.ShippingCost < 0 || request.SuppliesCost < 0 || request.OtherCosts < 0 || request.SalesTaxCollected < 0)
        {
            return BadRequest("Costs, fees, and tax cannot be negative.");
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
            CostOfGoods = item.PurchasePrice ?? 0,
            PlatformFees = request.PlatformFees,
            PaymentFees = request.PaymentFees,
            ShippingCost = request.ShippingCost,
            SuppliesCost = request.SuppliesCost,
            OtherCosts = request.OtherCosts,
            SalesTaxCollected = request.SalesTaxCollected,
            TaxRemittedByPlatform = request.TaxRemittedByPlatform,
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
            sale.CostOfGoods,
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
            ResaleFinancialCalculator.SalesTaxOwed(sale.SalesTaxCollected, sale.TaxRemittedByPlatform),
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

    [HttpPut("{id:guid}")]
    [RequireOrganizationPermission(HaulScoutPermissions.ManageFinance)]
    public async Task<ActionResult<SaleDto>> Update(Guid id, UpdateSaleRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return BadRequest("A reason is required to correct a sale.");
        }

        if (string.IsNullOrWhiteSpace(request.Platform) || request.SoldPrice <= 0)
        {
            return BadRequest("Platform and a positive sold price are required.");
        }

        if (request.PlatformFees < 0 || request.PaymentFees < 0 || request.ShippingCost < 0
            || request.SuppliesCost < 0 || request.OtherCosts < 0 || request.SalesTaxCollected < 0)
        {
            return BadRequest("Costs, fees, and tax cannot be negative.");
        }

        var sale = await db.Sales
            .Include(item => item.InventoryItem)
            .SingleOrDefaultAsync(item => item.Id == id && item.OrganizationId == currentUser.OrganizationId, cancellationToken);

        if (sale is null)
        {
            return NotFound();
        }

        string before = SnapshotSale(sale);

        sale.Platform = request.Platform.Trim();
        sale.SoldPrice = request.SoldPrice;
        sale.PlatformFees = request.PlatformFees;
        sale.PaymentFees = request.PaymentFees;
        sale.ShippingCost = request.ShippingCost;
        sale.SuppliesCost = request.SuppliesCost;
        sale.OtherCosts = request.OtherCosts;
        sale.SalesTaxCollected = request.SalesTaxCollected;
        sale.TaxRemittedByPlatform = request.TaxRemittedByPlatform;
        sale.SoldAt = request.SoldAt;
        sale.Notes = TrimToNull(request.Notes);

        // Net profit is always recomputed from the corrected inputs (tax excluded); never trust a stored figure.
        RealizedSaleFinancials financials = ResaleFinancialCalculator.CalculateRealizedSale(
            sale.SoldPrice, sale.CostOfGoods, sale.PlatformFees, sale.PaymentFees,
            sale.ShippingCost, sale.SuppliesCost, sale.OtherCosts);
        sale.NetProfit = financials.NetProfit;

        int nextVersion = await db.SaleCorrections
            .CountAsync(item => item.SaleId == sale.Id && item.OrganizationId == currentUser.OrganizationId, cancellationToken) + 1;

        db.SaleCorrections.Add(new SaleCorrection
        {
            OrganizationId = currentUser.OrganizationId,
            SaleId = sale.Id,
            CorrectedByUserId = currentUser.UserId,
            Reason = request.Reason.Trim(),
            BeforeJson = before,
            AfterJson = SnapshotSale(sale),
            Version = nextVersion,
        });

        await db.SaveChangesAsync(cancellationToken);
        return sale.ToDto();
    }

    [HttpGet("{id:guid}/corrections")]
    public async Task<ActionResult<IReadOnlyList<SaleCorrectionDto>>> ListCorrections(Guid id, CancellationToken cancellationToken)
    {
        bool saleExists = await db.Sales
            .AnyAsync(item => item.Id == id && item.OrganizationId == currentUser.OrganizationId, cancellationToken);
        if (!saleExists)
        {
            return NotFound();
        }

        var corrections = await db.SaleCorrections
            .Include(item => item.CorrectedByUser)
            .Where(item => item.SaleId == id && item.OrganizationId == currentUser.OrganizationId)
            .OrderByDescending(item => item.Version)
            .ToListAsync(cancellationToken);

        return corrections.Select(item => item.ToDto()).ToList();
    }

    // Serializes a sale's financial state for the immutable before/after audit snapshot.
    private static string SnapshotSale(Sale sale) => JsonSerializer.Serialize(new
    {
        sale.Platform,
        sale.SoldPrice,
        sale.CostOfGoods,
        sale.PlatformFees,
        sale.PaymentFees,
        sale.ShippingCost,
        sale.SuppliesCost,
        sale.OtherCosts,
        sale.SalesTaxCollected,
        sale.TaxRemittedByPlatform,
        sale.NetProfit,
        sale.SoldAt,
        Status = sale.Status.ToString(),
        sale.Notes,
    });

}
