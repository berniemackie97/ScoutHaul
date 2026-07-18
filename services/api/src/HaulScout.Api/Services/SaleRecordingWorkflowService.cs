using Microsoft.EntityFrameworkCore;
using static HaulScout.Api.Helpers.StringHelpers;
using HaulScout.Api.Mapping;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Sales;
using HaulScout.Application.Security;
using HaulScout.Application.Services;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Domain.Services;
using HaulScout.Infrastructure.Orders;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Api.Services;

/// <summary>
/// Writes normal and bundled sales. The controller owns HTTP shape; this service owns sale creation side effects:
/// inventory status, order headers, internal sale observations, and manual delist worklist updates.
/// </summary>
public sealed class SaleRecordingWorkflowService(
    HaulScoutDbContext db,
    ICurrentUserContext currentUser,
    OrderAssembler orderWriter,
    SalesChannelWorkflowService salesChannels)
{
    public async Task<AppResult<SaleDto>> RecordAsync(RecordSaleRequest request, CancellationToken cancellationToken)
    {
        InventoryItem? item = await db.InventoryItems
            .Include(inventoryItem => inventoryItem.BundleComponents)
                .ThenInclude(component => component.ComponentInventoryItem)
            .SingleOrDefaultAsync(
                inventoryItem => inventoryItem.Id == request.InventoryItemId && inventoryItem.OrganizationId == currentUser.OrganizationId,
                cancellationToken);

        if (item is null)
        {
            return AppResult<SaleDto>.NotFound("Inventory item was not found.");
        }

        string? channel = await salesChannels.CanonicalChannelAsync(request.Platform, cancellationToken);
        if (channel is null || request.SoldPrice <= 0)
        {
            return AppResult<SaleDto>.Validation("Select a controlled sales channel and enter a positive sold price.");
        }

        if (request.ShippingCharged < 0 || request.PlatformFees < 0 || request.PaymentFees < 0 || request.ShippingCost < 0 || request.SuppliesCost < 0 || request.OtherCosts < 0 || request.SalesTaxCollected < 0)
        {
            return AppResult<SaleDto>.Validation("Revenue, costs, fees, and tax cannot be negative.");
        }

        if (request.SoldAt?.UtcDateTime.Date > DateTime.UtcNow.Date)
        {
            return AppResult<SaleDto>.Validation("Sale date cannot be in the future.");
        }

        bool alreadySold = await db.Sales.AnyAsync(
            saleItem => saleItem.InventoryItemId == request.InventoryItemId
                && saleItem.OrganizationId == currentUser.OrganizationId
                && !SaleWorkflow.InventoryReleaseStatuses.Contains(saleItem.Status),
            cancellationToken);

        if (alreadySold)
        {
            return AppResult<SaleDto>.Conflict("Inventory item already has a recorded sale.");
        }

        Listing? listing = null;
        if (request.ListingId.HasValue)
        {
            listing = await db.Listings.SingleOrDefaultAsync(
                listingItem => listingItem.Id == request.ListingId.Value && listingItem.OrganizationId == currentUser.OrganizationId,
                cancellationToken);

            if (listing is null)
            {
                return AppResult<SaleDto>.NotFound("Listing was not found.");
            }

            if (listing.InventoryItemId != item.Id)
            {
                return AppResult<SaleDto>.Validation("Listing does not belong to the supplied inventory item.");
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

        Sale sale = new()
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
            SoldAt = request.SoldAt ?? DateTimeOffset.UtcNow,
            Notes = TrimToNull(request.Notes)
        };
        sale.RecomputeNet();

        Order salesOrder = await orderWriter.GetOrCreateAsync(
            channel,
            request.ExternalOrderId,
            externalOrderUrl: null,
            request.BuyerReference,
            request.BuyerName,
            sale.SoldAt,
            channelAccountId: null,
            itemsSubtotal: sale.SoldPrice,
            shippingTotal: sale.ShippingCharged,
            taxTotal: sale.SalesTaxCollected,
            cancellationToken,
            request.ShippingCarrier,
            request.TrackingNumber);
        sale.OrderId = salesOrder.Id;
        sale.Order = salesOrder;

        item.Status = InventoryStatus.Sold;
        item.SoldAt = sale.SoldAt;
        InventoryStatePropagation.ApplyBundleComponentInventoryStatus(item, InventoryStatus.Sold, sale.SoldAt);

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

        await ApplyChannelSaleAsync(item.Id, channel, cancellationToken);

        await orderWriter.SyncSalesOrderAsync(salesOrder, [sale], cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return AppResult<SaleDto>.Ok(sale.ToDto());
    }

    public async Task<AppResult<BundledSaleResultDto>> RecordBundledAsync(
        RecordBundledSaleRequest request,
        CancellationToken cancellationToken)
    {
        Guid[] itemIds = (request.Items ?? []).Select(item => item.InventoryItemId).Distinct().ToArray();
        if (itemIds.Length < 2)
        {
            return AppResult<BundledSaleResultDto>.Validation("A bundled sale needs at least two distinct items. Use a normal sale for one item.");
        }

        string? channel = await salesChannels.CanonicalChannelAsync(request.Platform, cancellationToken);
        if (channel is null || request.TotalSoldPrice <= 0)
        {
            return AppResult<BundledSaleResultDto>.Validation("Select a controlled sales channel and enter a positive order total.");
        }

        if (new[] { request.ShippingCharged, request.PlatformFees, request.PaymentFees, request.ShippingCost, request.SuppliesCost, request.OtherCosts, request.SalesTaxCollected }.Any(value => value < 0))
        {
            return AppResult<BundledSaleResultDto>.Validation("Revenue, costs, fees, and tax cannot be negative.");
        }

        if (request.SoldAt?.UtcDateTime.Date > DateTime.UtcNow.Date)
        {
            return AppResult<BundledSaleResultDto>.Validation("Sale date cannot be in the future.");
        }

        List<InventoryItem> items = await db.InventoryItems
            .Include(inventoryItem => inventoryItem.BundleComponents).ThenInclude(component => component.ComponentInventoryItem)
            .Where(inventoryItem => itemIds.Contains(inventoryItem.Id) && inventoryItem.OrganizationId == currentUser.OrganizationId)
            .ToListAsync(cancellationToken);
        if (items.Count != itemIds.Length)
        {
            return AppResult<BundledSaleResultDto>.NotFound("One or more inventory items were not found.");
        }

        bool anySold = await db.Sales.AnyAsync(
            sale => itemIds.Contains(sale.InventoryItemId)
                && sale.OrganizationId == currentUser.OrganizationId
                && !SaleWorkflow.InventoryReleaseStatuses.Contains(sale.Status),
            cancellationToken);
        if (anySold)
        {
            return AppResult<BundledSaleResultDto>.Conflict("One or more of these items already has a recorded sale.");
        }

        Dictionary<Guid, decimal?> manualById = request.Items!
            .GroupBy(item => item.InventoryItemId)
            .ToDictionary(group => group.Key, group => group.First().ManualPrice);
        List<BundledSaleItemAllocation> allocationInputs = items
            .Select(item => new BundledSaleItemAllocation(item.Id, manualById[item.Id], item.EstimatedSalePrice ?? item.SellerAskingPrice ?? item.PurchasePrice ?? 0))
            .ToList();

        IReadOnlyList<AllocatedSaleLine> lines;
        try
        {
            lines = BundledSaleAllocator.Allocate(allocationInputs, request.TotalSoldPrice, request.ShippingCharged, request.PlatformFees, request.PaymentFees, request.ShippingCost, request.SuppliesCost, request.OtherCosts, request.SalesTaxCollected);
        }
        catch (InvalidOperationException exception)
        {
            return AppResult<BundledSaleResultDto>.Validation(exception.Message);
        }

        string? orderReference = TrimToNull(request.OrderReference);
        DateTimeOffset soldAt = request.SoldAt ?? DateTimeOffset.UtcNow;
        Order salesOrder = await orderWriter.GetOrCreateAsync(
            channel,
            orderReference,
            externalOrderUrl: null,
            buyerReference: null,
            buyerName: null,
            soldAt,
            channelAccountId: null,
            itemsSubtotal: request.TotalSoldPrice,
            shippingTotal: request.ShippingCharged,
            taxTotal: request.SalesTaxCollected,
            cancellationToken);
        Guid groupId = salesOrder.Id;
        Dictionary<Guid, InventoryItem> byId = items.ToDictionary(item => item.Id);
        List<Sale> created = [];

        foreach (AllocatedSaleLine line in lines)
        {
            InventoryItem item = byId[line.InventoryItemId];
            RealizedSaleFinancials financials = ResaleFinancialCalculator.CalculateRealizedSale(
                line.Price, line.ShippingCharged, item.PurchasePrice ?? 0, line.PlatformFees, line.PaymentFees, line.ShippingCost, line.SuppliesCost, line.OtherCosts);

            Sale sale = new()
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
                SoldAt = soldAt,
                Notes = TrimToNull(request.Notes),
                SaleGroupId = groupId,
                GroupOrderReference = orderReference,
                OrderId = salesOrder.Id,
                Order = salesOrder,
                InventoryItem = item,
            };
            sale.RecomputeNet();
            db.Sales.Add(sale);
            created.Add(sale);

            item.Status = InventoryStatus.Sold;
            item.SoldAt = soldAt;
            InventoryStatePropagation.ApplyBundleComponentInventoryStatus(item, InventoryStatus.Sold, soldAt);
            await ApplyChannelSaleAsync(item.Id, channel, cancellationToken);
        }

        await orderWriter.SyncSalesOrderAsync(salesOrder, created, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        List<SaleDto> dtos = created.Select(sale => sale.ToDto()).ToList();
        return AppResult<BundledSaleResultDto>.Ok(new BundledSaleResultDto(groupId, orderReference, dtos.Count, dtos.Sum(sale => sale.NetProfit), dtos));
    }

    private async Task ApplyChannelSaleAsync(Guid inventoryItemId, string soldChannel, CancellationToken cancellationToken)
    {
        List<ChannelListing> channelListings = await db.ChannelListings
            .Include(listing => listing.ChannelAccount)
            .Where(listing => listing.InventoryItemId == inventoryItemId && listing.OrganizationId == currentUser.OrganizationId)
            .ToListAsync(cancellationToken);
        ChannelListingSync.ApplySale(channelListings, soldChannel, DateTimeOffset.UtcNow);
    }
}
