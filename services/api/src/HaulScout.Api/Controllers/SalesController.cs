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
    private static readonly SaleStatus[] ReleasedInventoryStatuses = [SaleStatus.Returned, SaleStatus.Refunded, SaleStatus.Cancelled];

    [HttpGet("reference-data")]
    public async Task<ActionResult<SalesReferenceDataDto>> ReferenceData(CancellationToken cancellationToken)
    {
        List<string> existing = await ExistingChannels(cancellationToken);
        var channels = SalesChannelCatalog.Channels.ToList();
        channels.AddRange(existing
            .Where(value => channels.All(option => !string.Equals(option.Value, value, StringComparison.OrdinalIgnoreCase)))
            .Select(value => new SaleChannelOptionDto(value, $"{value} (legacy — standardize)", "Legacy data", true, [])));
        return new SalesReferenceDataDto(channels);
    }

    [HttpPost("fee-estimate")]
    public async Task<ActionResult<ChannelFeeEstimateDto>> EstimateFees(EstimateChannelFeesRequest request, CancellationToken cancellationToken)
    {
        string? channel = SalesChannelCatalog.CanonicalValue(request.Platform, await ExistingChannels(cancellationToken));
        if (channel is null)
        {
            return BadRequest("Select a controlled sales channel.");
        }

        if (request.SoldPrice < 0)
        {
            return BadRequest("Sold price cannot be negative.");
        }

        ChannelFeeProfileDto? profile = SalesChannelCatalog.FindProfile(channel, request.FeeProfileId);
        if (profile is null)
        {
            return BadRequest("Select a valid fee profile for this sales channel.");
        }

        return ChannelFeeCalculator.Estimate(channel, profile, request.SoldPrice);
    }

    [HttpGet("candidates")]
    public async Task<ActionResult<IReadOnlyList<SaleCandidateDto>>> Candidates(CancellationToken cancellationToken)
    {
        var items = await db.InventoryItems
            .AsNoTracking()
            .Where(item => item.OrganizationId == currentUser.OrganizationId
                && !item.IsArchived
                && item.Status != InventoryStatus.Bundled
                && !item.Sales.Any(sale => !ReleasedInventoryStatuses.Contains(sale.Status)))
            .OrderByDescending(item => item.UpdatedAt)
            .Take(500)
            .Select(item => new SaleCandidateDto(
                item.Id,
                item.Title,
                item.Status,
                item.PurchasePrice ?? 0,
                item.EstimatedSalePrice ?? item.SellerAskingPrice,
                item.Listings
                    .Where(listing => listing.Status == ListingStatus.Active || listing.Status == ListingStatus.Draft)
                    .OrderByDescending(listing => listing.UpdatedAt)
                    .Select(listing => (Guid?)listing.Id)
                    .FirstOrDefault(),
                item.Listings
                    .Where(listing => listing.Status == ListingStatus.Active || listing.Status == ListingStatus.Draft)
                    .OrderByDescending(listing => listing.UpdatedAt)
                    .Select(listing => listing.Platform)
                    .FirstOrDefault(),
                item.Listings
                    .Where(listing => listing.Status == ListingStatus.Active || listing.Status == ListingStatus.Draft)
                    .OrderByDescending(listing => listing.UpdatedAt)
                    .Select(listing => (decimal?)listing.ListPrice)
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);

        return items;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SaleDto>>> List(
        [FromQuery] SaleStatus? status,
        CancellationToken cancellationToken)
    {
        var query = db.Sales
            .Include(item => item.InventoryItem)
            .Include(item => item.Refunds)
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
            .Include(item => item.Refunds)
            .SingleOrDefaultAsync(item => item.Id == id && item.OrganizationId == currentUser.OrganizationId, cancellationToken);

        return sale is null ? NotFound() : sale.ToDto();
    }

    [HttpPost]
    [RequireOrganizationPermission(HaulScoutPermissions.ManageFinance)]
    public async Task<ActionResult<SaleDto>> Record(RecordSaleRequest request, CancellationToken cancellationToken)
    {
        var item = await db.InventoryItems
            .Include(inventoryItem => inventoryItem.BundleComponents)
                .ThenInclude(component => component.ComponentInventoryItem)
            .SingleOrDefaultAsync(
            inventoryItem => inventoryItem.Id == request.InventoryItemId && inventoryItem.OrganizationId == currentUser.OrganizationId,
            cancellationToken);

        if (item is null)
        {
            return NotFound("Inventory item was not found.");
        }

        string? channel = SalesChannelCatalog.CanonicalValue(request.Platform, await ExistingChannels(cancellationToken));
        if (channel is null || request.SoldPrice <= 0)
        {
            return BadRequest("Select a controlled sales channel and enter a positive sold price.");
        }

        if (request.ShippingCharged < 0 || request.PlatformFees < 0 || request.PaymentFees < 0 || request.ShippingCost < 0 || request.SuppliesCost < 0 || request.OtherCosts < 0 || request.SalesTaxCollected < 0)
        {
            return BadRequest("Revenue, costs, fees, and tax cannot be negative.");
        }

        if (request.SoldAt?.UtcDateTime.Date > DateTime.UtcNow.Date)
        {
            return BadRequest("Sale date cannot be in the future.");
        }

        bool alreadySold = await db.Sales.AnyAsync(
            saleItem => saleItem.InventoryItemId == request.InventoryItemId
                        && saleItem.OrganizationId == currentUser.OrganizationId
                        && !ReleasedInventoryStatuses.Contains(saleItem.Status),
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
            request.ShippingCharged,
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
            Platform = channel,
            SoldPrice = request.SoldPrice,
            ShippingCharged = request.ShippingCharged,
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
        ApplyBundleComponentInventoryStatus(item, InventoryStatus.Sold, sale.SoldAt);

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
            TotalPrice = request.SoldPrice + request.ShippingCharged,
            Condition = item.Condition,
            ObservedAt = sale.SoldAt,
            SoldAt = sale.SoldAt,
            Notes = "Created from recorded sale."
        });

        // Reflect the sale across channels: the channel it sold on → Sold, every other live channel → PendingDelist.
        var channelListings = await db.ChannelListings
            .Include(listing => listing.ChannelAccount)
            .Where(listing => listing.InventoryItemId == item.Id && listing.OrganizationId == currentUser.OrganizationId)
            .ToListAsync(cancellationToken);
        ChannelListingSync.ApplySale(channelListings, channel, DateTimeOffset.UtcNow);

        await db.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(GetProfit), new { id = sale.Id }, sale.ToDto());
    }

    /// <summary>Record one bundled order covering several items — one package, one tracking. Creates a linked
    /// sale per item with the order's price and shared costs allocated across them, so each item keeps its own net.</summary>
    [HttpPost("bundled")]
    [RequireOrganizationPermission(HaulScoutPermissions.ManageFinance)]
    public async Task<ActionResult<BundledSaleResultDto>> RecordBundled(RecordBundledSaleRequest request, CancellationToken cancellationToken)
    {
        Guid[] itemIds = (request.Items ?? []).Select(item => item.InventoryItemId).Distinct().ToArray();
        if (itemIds.Length < 2)
        {
            return BadRequest("A bundled sale needs at least two distinct items. Use a normal sale for one item.");
        }

        string? channel = SalesChannelCatalog.CanonicalValue(request.Platform, await ExistingChannels(cancellationToken));
        if (channel is null || request.TotalSoldPrice <= 0)
        {
            return BadRequest("Select a controlled sales channel and enter a positive order total.");
        }

        if (new[] { request.ShippingCharged, request.PlatformFees, request.PaymentFees, request.ShippingCost, request.SuppliesCost, request.OtherCosts, request.SalesTaxCollected }.Any(value => value < 0))
        {
            return BadRequest("Revenue, costs, fees, and tax cannot be negative.");
        }

        if (request.SoldAt?.UtcDateTime.Date > DateTime.UtcNow.Date)
        {
            return BadRequest("Sale date cannot be in the future.");
        }

        var items = await db.InventoryItems
            .Include(inventoryItem => inventoryItem.BundleComponents).ThenInclude(component => component.ComponentInventoryItem)
            .Where(inventoryItem => itemIds.Contains(inventoryItem.Id) && inventoryItem.OrganizationId == currentUser.OrganizationId)
            .ToListAsync(cancellationToken);
        if (items.Count != itemIds.Length)
        {
            return NotFound("One or more inventory items were not found.");
        }

        bool anySold = await db.Sales.AnyAsync(
            sale => itemIds.Contains(sale.InventoryItemId) && sale.OrganizationId == currentUser.OrganizationId && !ReleasedInventoryStatuses.Contains(sale.Status),
            cancellationToken);
        if (anySold)
        {
            return Conflict("One or more of these items already has a recorded sale.");
        }

        var manualById = request.Items!.GroupBy(item => item.InventoryItemId).ToDictionary(group => group.Key, group => group.First().ManualPrice);
        var allocationInputs = items
            .Select(item => new BundledSaleItemAllocation(item.Id, manualById[item.Id], item.EstimatedSalePrice ?? item.SellerAskingPrice ?? item.PurchasePrice ?? 0))
            .ToList();

        IReadOnlyList<AllocatedSaleLine> lines;
        try
        {
            lines = BundledSaleAllocator.Allocate(allocationInputs, request.TotalSoldPrice, request.ShippingCharged, request.PlatformFees, request.PaymentFees, request.ShippingCost, request.SuppliesCost, request.OtherCosts, request.SalesTaxCollected);
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(exception.Message);
        }

        Guid groupId = Guid.NewGuid();
        string? orderReference = TrimToNull(request.OrderReference);
        DateTimeOffset soldAt = request.SoldAt ?? DateTimeOffset.UtcNow;
        var byId = items.ToDictionary(item => item.Id);
        var created = new List<Sale>();

        foreach (var line in lines)
        {
            var item = byId[line.InventoryItemId];
            RealizedSaleFinancials financials = ResaleFinancialCalculator.CalculateRealizedSale(
                line.Price, line.ShippingCharged, item.PurchasePrice ?? 0, line.PlatformFees, line.PaymentFees, line.ShippingCost, line.SuppliesCost, line.OtherCosts);

            var sale = new Sale
            {
                OrganizationId = currentUser.OrganizationId,
                InventoryItemId = item.Id,
                Platform = channel,
                SoldPrice = line.Price,
                ShippingCharged = line.ShippingCharged,
                CostOfGoods = item.PurchasePrice ?? 0,
                PlatformFees = line.PlatformFees,
                PaymentFees = line.PaymentFees,
                ShippingCost = line.ShippingCost,
                SuppliesCost = line.SuppliesCost,
                OtherCosts = line.OtherCosts,
                SalesTaxCollected = line.SalesTaxCollected,
                TaxRemittedByPlatform = request.TaxRemittedByPlatform,
                NetProfit = financials.NetProfit,
                SoldAt = soldAt,
                Notes = TrimToNull(request.Notes),
                SaleGroupId = groupId,
                GroupOrderReference = orderReference,
                InventoryItem = item,
            };
            db.Sales.Add(sale);
            created.Add(sale);

            item.Status = InventoryStatus.Sold;
            item.SoldAt = soldAt;
            ApplyBundleComponentInventoryStatus(item, InventoryStatus.Sold, soldAt);

            var channelListings = await db.ChannelListings
                .Include(listing => listing.ChannelAccount)
                .Where(listing => listing.InventoryItemId == item.Id && listing.OrganizationId == currentUser.OrganizationId)
                .ToListAsync(cancellationToken);
            ChannelListingSync.ApplySale(channelListings, channel, DateTimeOffset.UtcNow);
        }

        await db.SaveChangesAsync(cancellationToken);

        var dtos = created.Select(sale => sale.ToDto()).ToList();
        return new BundledSaleResultDto(groupId, orderReference, dtos.Count, dtos.Sum(sale => sale.NetProfit), dtos);
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
            sale.ShippingCharged,
            sale.CostOfGoods,
            sale.PlatformFees,
            sale.PaymentFees,
            sale.ShippingCost,
            sale.SuppliesCost,
            sale.OtherCosts);

        return new RealizedProfitDto(
            sale.Id,
            financials.SoldPrice,
            financials.ShippingCharged,
            financials.SalesRevenue,
            financials.PurchaseCost,
            financials.SellingCosts,
            financials.TotalFeesAndCosts,
            financials.MarketplaceNetBeforeCogs,
            ResaleFinancialCalculator.SalesTaxOwed(sale.SalesTaxCollected, sale.TaxRemittedByPlatform),
            financials.NetProfit,
            financials.RoiPercent,
            financials.MarginPercent);
    }

    [HttpPatch("{id:guid}/packed")]
    public Task<ActionResult<SaleDto>> MarkPacked(Guid id, CancellationToken cancellationToken) =>
        Transition(id, new SaleStatusTransitionRequest(SaleStatus.Packed, null, null, null, null), cancellationToken);

    [HttpPatch("{id:guid}/shipped")]
    public Task<ActionResult<SaleDto>> MarkShipped(Guid id, CancellationToken cancellationToken) =>
        Transition(id, new SaleStatusTransitionRequest(SaleStatus.Shipped, null, null, null, null), cancellationToken);

    [HttpPatch("{id:guid}/delivered")]
    public Task<ActionResult<SaleDto>> MarkDelivered(Guid id, CancellationToken cancellationToken) =>
        Transition(id, new SaleStatusTransitionRequest(SaleStatus.Delivered, null, null, null, null), cancellationToken);

    [HttpGet("{id:guid}/shipment")]
    public async Task<ActionResult<SaleShipmentDto>> GetShipment(Guid id, CancellationToken cancellationToken)
    {
        bool saleExists = await db.Sales.AnyAsync(
            item => item.Id == id && item.OrganizationId == currentUser.OrganizationId,
            cancellationToken);
        if (!saleExists) return NotFound();

        Shipment? shipment = await db.Shipments
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.SaleId == id && item.OrganizationId == currentUser.OrganizationId, cancellationToken);
        return shipment is null ? NotFound() : shipment.ToDto();
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<SaleDto>> Transition(
        Guid id,
        SaleStatusTransitionRequest request,
        CancellationToken cancellationToken)
    {
        var sale = await db.Sales
            .Include(item => item.InventoryItem)
                .ThenInclude(item => item!.BundleComponents)
                    .ThenInclude(item => item.ComponentInventoryItem)
            .Include(item => item.Listing)
            .SingleOrDefaultAsync(item => item.Id == id && item.OrganizationId == currentUser.OrganizationId, cancellationToken);

        if (sale is null)
        {
            return NotFound();
        }

        if (!SaleWorkflow.CanTransition(sale.Status, request.Status))
        {
            return Conflict($"A sale cannot move from {sale.Status} to {request.Status}.");
        }

        string? reason = TrimToNull(request.Reason);
        if (SaleWorkflow.RequiresReason(sale.Status, request.Status) && reason is null)
        {
            return BadRequest("A reason is required for this sale transition.");
        }

        string? carrier = TrimToNull(request.Carrier);
        string? trackingNumber = TrimToNull(request.TrackingNumber);
        if ((carrier is null) != (trackingNumber is null))
        {
            return BadRequest("Carrier and tracking number must be supplied together.");
        }

        DateTimeOffset occurredAt = request.OccurredAt ?? DateTimeOffset.UtcNow;
        if (occurredAt > DateTimeOffset.UtcNow.AddMinutes(5))
        {
            return BadRequest("Transition time cannot be in the future.");
        }

        if (occurredAt < sale.SoldAt)
        {
            return BadRequest("Transition time cannot be earlier than the sale date.");
        }

        Shipment? shipment = await db.Shipments.SingleOrDefaultAsync(
            item => item.SaleId == sale.Id && item.OrganizationId == currentUser.OrganizationId,
            cancellationToken);
        if (request.Status == SaleStatus.Shipped && shipment?.PackedAt > occurredAt)
        {
            return BadRequest("Shipped time cannot be earlier than packed time.");
        }

        if (request.Status == SaleStatus.Delivered && shipment?.ShippedAt > occurredAt)
        {
            return BadRequest("Delivered time cannot be earlier than shipped time.");
        }

        SaleStatus previousStatus = sale.Status;
        string before = SnapshotSale(sale);
        sale.Status = request.Status;
        if (sale.InventoryItem is not null)
        {
            if (request.Status != SaleStatus.ReturnRequested)
            {
                sale.InventoryItem.Status = SaleWorkflow.InventoryStatusFor(request.Status, sale.ListingId.HasValue);
            }

            if (request.Status == SaleStatus.Cancelled)
            {
                sale.InventoryItem.SoldAt = null;
            }

            ApplyBundleComponentInventoryStatus(
                sale.InventoryItem,
                request.Status == SaleStatus.Cancelled
                    ? InventoryStatus.Bundled
                    : request.Status == SaleStatus.ReturnRequested
                        ? sale.InventoryItem.Status
                        : SaleWorkflow.InventoryStatusFor(request.Status, sale.ListingId.HasValue),
                request.Status == SaleStatus.Cancelled ? null : sale.SoldAt);
        }

        if (sale.Listing is not null && request.Status == SaleStatus.Cancelled)
        {
            sale.Listing.Status = ListingStatus.Active;
            sale.Listing.EndedAt = null;
        }

        if (request.Status is SaleStatus.Packed or SaleStatus.Shipped or SaleStatus.Delivered)
        {
            shipment ??= new Shipment
            {
                OrganizationId = currentUser.OrganizationId,
                SaleId = sale.Id,
                Cost = sale.ShippingCost,
            };
            if (db.Entry(shipment).State == EntityState.Detached) db.Shipments.Add(shipment);
            if (request.Status == SaleStatus.Packed) shipment.PackedAt ??= occurredAt;
            if (request.Status == SaleStatus.Shipped)
            {
                shipment.PackedAt ??= occurredAt;
                shipment.ShippedAt ??= occurredAt;
                shipment.Carrier = carrier ?? shipment.Carrier;
                shipment.TrackingNumber = trackingNumber ?? shipment.TrackingNumber;
            }
            if (request.Status == SaleStatus.Delivered) shipment.DeliveredAt ??= occurredAt;
        }
        else if (previousStatus == SaleStatus.Packed && request.Status == SaleStatus.Recorded && shipment is not null)
        {
            shipment.PackedAt = null;
        }

        if (request.Status == SaleStatus.Cancelled)
        {
            MarketObservation? observation = await db.MarketObservations
                .Where(item => item.InventoryItemId == sale.InventoryItemId
                    && item.ObservationType == MarketObservationType.InternalSale
                    && item.OrganizationId == currentUser.OrganizationId
                    && item.SoldAt == sale.SoldAt)
                .OrderByDescending(item => item.ObservedAt)
                .FirstOrDefaultAsync(cancellationToken);
            if (observation is not null) db.MarketObservations.Remove(observation);
        }

        await AddCorrection(
            sale,
            before,
            reason ?? $"Workflow transition: {previousStatus} → {request.Status}",
            cancellationToken);
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

        string? channel = SalesChannelCatalog.CanonicalValue(request.Platform, await ExistingChannels(cancellationToken));
        if (channel is null || request.SoldPrice <= 0)
        {
            return BadRequest("Select a controlled sales channel and enter a positive sold price.");
        }

        if (request.ShippingCharged < 0 || request.PlatformFees < 0 || request.PaymentFees < 0 || request.ShippingCost < 0
            || request.SuppliesCost < 0 || request.OtherCosts < 0 || request.SalesTaxCollected < 0)
        {
            return BadRequest("Revenue, costs, fees, and tax cannot be negative.");
        }

        if (request.SoldAt.UtcDateTime.Date > DateTime.UtcNow.Date)
        {
            return BadRequest("Sale date cannot be in the future.");
        }

        var sale = await db.Sales
            .Include(item => item.InventoryItem)
                .ThenInclude(item => item!.BundleComponents)
                    .ThenInclude(item => item.ComponentInventoryItem)
            .Include(item => item.Listing)
            .SingleOrDefaultAsync(item => item.Id == id && item.OrganizationId == currentUser.OrganizationId, cancellationToken);

        if (sale is null)
        {
            return NotFound();
        }

        string before = SnapshotSale(sale);
        DateTimeOffset previousSoldAt = sale.SoldAt;

        sale.Platform = channel;
        sale.SoldPrice = request.SoldPrice;
        sale.ShippingCharged = request.ShippingCharged;
        sale.PlatformFees = request.PlatformFees;
        sale.PaymentFees = request.PaymentFees;
        sale.ShippingCost = request.ShippingCost;
        sale.SuppliesCost = request.SuppliesCost;
        sale.OtherCosts = request.OtherCosts;
        sale.SalesTaxCollected = request.SalesTaxCollected;
        sale.TaxRemittedByPlatform = request.TaxRemittedByPlatform;
        sale.SoldAt = request.SoldAt;
        sale.Notes = TrimToNull(request.Notes);
        if (sale.InventoryItem is not null) sale.InventoryItem.SoldAt = request.SoldAt;
        if (sale.InventoryItem is not null && !ReleasedInventoryStatuses.Contains(sale.Status))
        {
            ApplyBundleComponentInventoryStatus(
                sale.InventoryItem,
                SaleWorkflow.InventoryStatusFor(sale.Status, sale.ListingId.HasValue),
                request.SoldAt);
        }
        if (sale.Listing is not null && sale.Listing.Status == ListingStatus.Sold) sale.Listing.EndedAt = request.SoldAt;

        MarketObservation? observation = await db.MarketObservations
            .Where(item => item.InventoryItemId == sale.InventoryItemId
                && item.ObservationType == MarketObservationType.InternalSale
                && item.OrganizationId == currentUser.OrganizationId
                && item.SoldAt == previousSoldAt)
            .OrderByDescending(item => item.ObservedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (observation is not null)
        {
            observation.Price = request.SoldPrice;
            observation.TotalPrice = request.SoldPrice + request.ShippingCharged;
            observation.ObservedAt = request.SoldAt;
            observation.SoldAt = request.SoldAt;
        }

        // Net profit is always recomputed from the corrected inputs (tax excluded); never trust a stored figure.
        RealizedSaleFinancials financials = ResaleFinancialCalculator.CalculateRealizedSale(
            sale.SoldPrice, sale.ShippingCharged, sale.CostOfGoods, sale.PlatformFees, sale.PaymentFees,
            sale.ShippingCost, sale.SuppliesCost, sale.OtherCosts);
        sale.NetProfit = financials.NetProfit;

        await AddCorrection(sale, before, request.Reason.Trim(), cancellationToken);

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

    [HttpGet("{id:guid}/refunds")]
    public async Task<ActionResult<IReadOnlyList<RefundDto>>> ListRefunds(Guid id, CancellationToken cancellationToken)
    {
        bool saleExists = await db.Sales.AnyAsync(item => item.Id == id && item.OrganizationId == currentUser.OrganizationId, cancellationToken);
        if (!saleExists)
        {
            return NotFound();
        }

        var refunds = await db.Refunds
            .Include(item => item.RecordedByUser)
            .Where(item => item.SaleId == id && item.OrganizationId == currentUser.OrganizationId)
            .OrderByDescending(item => item.RefundedAt)
            .ToListAsync(cancellationToken);
        return refunds.Select(item => item.ToDto()).ToList();
    }

    /// <summary>Record a refund (full or partial) against a sale as a first-class financial event. Net profit is
    /// recomputed from the original figures minus every refund; the change is audited. A partial refund keeps the
    /// item sold; a full refund with <c>ItemReturned</c> releases the item back to inventory.</summary>
    [HttpPost("{id:guid}/refunds")]
    [RequireOrganizationPermission(HaulScoutPermissions.ManageFinance)]
    public async Task<ActionResult<SaleDto>> RecordRefund(Guid id, RecordRefundRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return BadRequest("A reason is required for a refund.");
        }

        if (request.ItemAmount < 0 || request.ShippingRefunded < 0 || request.TaxRefunded < 0 || request.FeeCredit < 0)
        {
            return BadRequest("Refund amounts cannot be negative.");
        }

        if (request.ItemAmount + request.ShippingRefunded + request.TaxRefunded + request.FeeCredit <= 0)
        {
            return BadRequest("A refund needs at least one non-zero amount.");
        }

        DateTimeOffset refundedAt = request.RefundedAt ?? DateTimeOffset.UtcNow;
        if (refundedAt > DateTimeOffset.UtcNow.AddMinutes(5))
        {
            return BadRequest("Refund date cannot be in the future.");
        }

        var sale = await db.Sales
            .Include(item => item.InventoryItem)
                .ThenInclude(item => item!.BundleComponents)
                    .ThenInclude(item => item.ComponentInventoryItem)
            .Include(item => item.Listing)
            .Include(item => item.Refunds)
            .SingleOrDefaultAsync(item => item.Id == id && item.OrganizationId == currentUser.OrganizationId, cancellationToken);

        if (sale is null)
        {
            return NotFound();
        }

        if (sale.Status is SaleStatus.Cancelled)
        {
            return Conflict("A cancelled sale cannot be refunded.");
        }

        decimal alreadyRefundedItem = sale.Refunds.Sum(refund => refund.ItemAmount);
        if (alreadyRefundedItem + request.ItemAmount > sale.SoldPrice + 0.01m)
        {
            return BadRequest("Total item refunds cannot exceed the sold price.");
        }

        string before = SnapshotSale(sale);
        var refund = new Refund
        {
            OrganizationId = currentUser.OrganizationId,
            SaleId = sale.Id,
            Type = request.Type,
            ItemAmount = request.ItemAmount,
            ShippingRefunded = request.ShippingRefunded,
            TaxRefunded = request.TaxRefunded,
            FeeCredit = request.FeeCredit,
            ItemReturned = request.ItemReturned,
            Reason = request.Reason.Trim(),
            Notes = TrimToNull(request.Notes),
            RefundedAt = refundedAt,
            RecordedByUserId = currentUser.UserId,
            Sale = sale,
        };
        // Explicit Add + reference nav (the codebase's pattern); relationship fixup also puts it on sale.Refunds
        // so the recompute + DTO see it. Adding to the DbSet AND the collection would double-insert.
        db.Refunds.Add(refund);

        ApplyRefundState(sale);
        await AddCorrection(sale, before,
            $"Refund recorded ({request.Type}): item {request.ItemAmount:C}, shipping {request.ShippingRefunded:C}, fee credit {request.FeeCredit:C} — {request.Reason.Trim()}",
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return sale.ToDto();
    }

    [HttpDelete("{id:guid}/refunds/{refundId:guid}")]
    [RequireOrganizationPermission(HaulScoutPermissions.ManageFinance)]
    public async Task<ActionResult<SaleDto>> VoidRefund(Guid id, Guid refundId, CancellationToken cancellationToken)
    {
        var sale = await db.Sales
            .Include(item => item.InventoryItem)
                .ThenInclude(item => item!.BundleComponents)
                    .ThenInclude(item => item.ComponentInventoryItem)
            .Include(item => item.Listing)
            .Include(item => item.Refunds)
            .SingleOrDefaultAsync(item => item.Id == id && item.OrganizationId == currentUser.OrganizationId, cancellationToken);

        if (sale is null)
        {
            return NotFound();
        }

        var refund = sale.Refunds.SingleOrDefault(item => item.Id == refundId);
        if (refund is null)
        {
            return NotFound("Refund was not found on this sale.");
        }

        string before = SnapshotSale(sale);
        sale.Refunds.Remove(refund);
        db.Refunds.Remove(refund);

        ApplyRefundState(sale);
        await AddCorrection(sale, before, $"Refund voided: item {refund.ItemAmount:C} ({refund.Reason})", cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return sale.ToDto();
    }

    /// <summary>Recomputes a sale's net profit and status from its current set of refunds. Item + buyer shipping
    /// reduce revenue, the platform's fee credit reduces selling cost, a returned item drops COGS; tax never nets.
    /// Full/partial + restock decide the resulting status and whether the item releases back to inventory.</summary>
    private static void ApplyRefundState(Sale sale)
    {
        var refunds = sale.Refunds;
        decimal itemRefunded = refunds.Sum(refund => refund.ItemAmount);
        bool restocked = refunds.Any(refund => refund.ItemReturned);
        var impact = new ResaleFinancialCalculator.SaleRefundImpact(
            itemRefunded,
            refunds.Sum(refund => refund.ShippingRefunded),
            refunds.Sum(refund => refund.TaxRefunded),
            refunds.Sum(refund => refund.FeeCredit),
            restocked);
        sale.NetProfit = ResaleFinancialCalculator.NetProfitAfterRefunds(
            sale.SoldPrice, sale.ShippingCharged, sale.CostOfGoods, sale.PlatformFees, sale.PaymentFees,
            sale.ShippingCost, sale.SuppliesCost, sale.OtherCosts, impact);

        if (refunds.Count == 0)
        {
            // Last refund voided — revert to a realized state; the operator can re-set the exact status.
            sale.Status = SaleStatus.Delivered;
            if (sale.InventoryItem is not null && sale.InventoryItem.Status == InventoryStatus.Returned)
            {
                sale.InventoryItem.Status = InventoryStatus.Delivered;
                ApplyBundleComponentInventoryStatus(sale.InventoryItem, InventoryStatus.Delivered, sale.InventoryItem.SoldAt);
            }
            return;
        }

        bool isFull = refunds.Any(refund => refund.Type == RefundType.Full) || (sale.SoldPrice > 0 && itemRefunded >= sale.SoldPrice - 0.01m);
        sale.Status = restocked ? SaleStatus.Returned : isFull ? SaleStatus.Refunded : SaleStatus.PartiallyRefunded;

        if (sale.InventoryItem is not null)
        {
            if (restocked)
            {
                sale.InventoryItem.Status = InventoryStatus.Returned;
                ApplyBundleComponentInventoryStatus(sale.InventoryItem, InventoryStatus.Returned, sale.InventoryItem.SoldAt);
            }
            // Partial or full-but-kept: the buyer still has the item, so inventory state is unchanged.
        }
    }

    // Serializes a sale's financial state for the immutable before/after audit snapshot.
    private static string SnapshotSale(Sale sale) => JsonSerializer.Serialize(new
    {
        sale.Platform,
        sale.SoldPrice,
        sale.ShippingCharged,
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

    private async Task AddCorrection(
        Sale sale,
        string before,
        string reason,
        CancellationToken cancellationToken)
    {
        int nextVersion = await db.SaleCorrections
            .CountAsync(item => item.SaleId == sale.Id && item.OrganizationId == currentUser.OrganizationId, cancellationToken) + 1;
        db.SaleCorrections.Add(new SaleCorrection
        {
            OrganizationId = currentUser.OrganizationId,
            SaleId = sale.Id,
            CorrectedByUserId = currentUser.UserId,
            Reason = reason,
            BeforeJson = before,
            AfterJson = SnapshotSale(sale),
            Version = nextVersion,
        });
    }

    private static void ApplyBundleComponentInventoryStatus(
        InventoryItem inventoryItem,
        InventoryStatus componentStatus,
        DateTimeOffset? soldAt)
    {
        if (!inventoryItem.IsBundle)
        {
            return;
        }

        foreach (var componentLink in inventoryItem.BundleComponents)
        {
            if (componentLink.ComponentInventoryItem is null)
            {
                continue;
            }

            componentLink.ComponentInventoryItem.Status = componentStatus;
            componentLink.ComponentInventoryItem.SoldAt = InventoryLifecycle.IsSold(componentStatus) || componentStatus == InventoryStatus.Returned
                ? soldAt
                : null;
        }
    }

    private async Task<List<string>> ExistingChannels(CancellationToken cancellationToken)
    {
        List<string> saleChannels = await db.Sales
            .AsNoTracking()
            .Where(item => item.OrganizationId == currentUser.OrganizationId)
            .Select(item => item.Platform)
            .Distinct()
            .ToListAsync(cancellationToken);
        List<string> listingChannels = await db.Listings
            .AsNoTracking()
            .Where(item => item.OrganizationId == currentUser.OrganizationId)
            .Select(item => item.Platform)
            .Distinct()
            .ToListAsync(cancellationToken);
        return saleChannels.Concat(listingChannels).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToList();
    }

}
