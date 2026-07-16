using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HaulScout.Api.Authorization;
using static HaulScout.Api.Helpers.StringHelpers;
using HaulScout.Api.Mapping;
using HaulScout.Api.Services;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Application.Services;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Domain.Services;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Api.Controllers;

/// <summary>
/// The landing zone for marketplace orders imported from platform reports or manually supplied normalized data.
/// Ingest is idempotent (keyed on platform + external order id); reconciliation turns an imported order into
/// ScoutHaul <see cref="Sale"/>(s) using the platform's OWN money figures — item prices, buyer shipping, tax, and
/// the exact fee total — so our finances match the marketplace's report by definition. No marketplace API is used.
/// </summary>
[ApiController]
[Route("api/platform-orders")]
[RequireOrganizationPermission(HaulScoutPermissions.ManageFinance)]
public sealed class PlatformOrdersController(
    HaulScoutDbContext db,
    ICurrentUserContext currentUser,
    OrderAssembler orderAssembler,
    MarketplaceRefundImportService marketplaceRefundImportService,
    PlatformOrderFeeEnrichmentService platformOrderFeeEnrichmentService,
    PlatformOrderIngestionService platformOrderIngestionService,
    PlatformOrderSuggestionService platformOrderSuggestionService) : ControllerBase
{
    private static readonly SaleStatus[] ReleasedInventoryStatuses = [SaleStatus.Returned, SaleStatus.Refunded, SaleStatus.Cancelled];

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PlatformOrderDto>>> List(
        [FromQuery] PlatformOrderStatus? status,
        CancellationToken cancellationToken)
    {
        IQueryable<PlatformOrder> query = db.PlatformOrders
            .Include(order => order.Lines).ThenInclude(line => line.InventoryItem)
            .Where(order => order.OrganizationId == currentUser.OrganizationId);

        if (status.HasValue)
        {
            query = query.Where(order => order.Status == status.Value);
        }

        List<PlatformOrder> orders = await query
            .OrderByDescending(order => order.OrderedAt)
            .Take(200)
            .ToListAsync(cancellationToken);

        Dictionary<Guid, (Guid Id, string Title, bool AlreadySold)> suggestions =
            await platformOrderSuggestionService.BuildSuggestionsAsync(orders, cancellationToken);
        return orders.Select(order => order.ToDto(suggestions)).ToList();
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PlatformOrderDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        PlatformOrder? order = await db.PlatformOrders
            .Include(item => item.Lines).ThenInclude(line => line.InventoryItem)
            .SingleOrDefaultAsync(item => item.Id == id && item.OrganizationId == currentUser.OrganizationId, cancellationToken);

        if (order is null)
        {
            return NotFound();
        }

        Dictionary<Guid, (Guid Id, string Title, bool AlreadySold)> suggestions =
            await platformOrderSuggestionService.BuildSuggestionsAsync([order], cancellationToken);
        return order.ToDto(suggestions);
    }

    /// <summary>Idempotent batch upsert of imported orders. Re-ingesting an order that is still <c>Imported</c>
    /// refreshes its figures and lines; an order already <c>Reconciled</c> is left untouched (its sales are posted).</summary>
    [HttpPost("ingest")]
    public async Task<ActionResult<IngestPlatformOrdersResultDto>> Ingest(IngestPlatformOrdersRequest request, CancellationToken cancellationToken)
    {
        if (request.Orders is null || request.Orders.Count == 0)
        {
            return BadRequest("No orders were supplied.");
        }

        Guid? channelAccountId = null;
        if (request.ChannelAccountId.HasValue)
        {
            bool accountExists = await db.ChannelAccounts.AnyAsync(
                account => account.Id == request.ChannelAccountId.Value && account.OrganizationId == currentUser.OrganizationId,
                cancellationToken);
            if (!accountExists)
            {
                return NotFound("Channel account was not found.");
            }
            channelAccountId = request.ChannelAccountId;
        }

        (IngestPlatformOrdersResultDto? result, string? error) =
            await platformOrderIngestionService.IngestAsync(request.Platform, request.Orders, channelAccountId, cancellationToken);
        return error is not null ? BadRequest(error) : result!;
    }

    /// <summary>Import eBay seller CSVs. The all-orders report creates/updates real order groups; the listing-sales
    /// report enriches those order lines with exact eBay fees, shipping labels, and net sales.</summary>
    [HttpPost("import/ebay-csv")]
    public async Task<ActionResult<IngestPlatformOrdersResultDto>> ImportEbayCsv(ImportPlatformOrdersCsvRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Csv))
        {
            return BadRequest("Paste or upload the eBay report CSV.");
        }

        EbayReportParser.ParseResult parsed = EbayReportParser.ParseReport(request.Csv);
        if (parsed.Kind == EbayReportParser.EbayReportKind.AllOrders)
        {
            if (parsed.Orders.Count == 0)
            {
                return BadRequest(parsed.Warning ?? "No order rows were found in the report.");
            }

            (IngestPlatformOrdersResultDto? result, string? error) = await platformOrderIngestionService.IngestAsync(
                string.IsNullOrWhiteSpace(request.Platform) ? "eBay" : request.Platform,
                parsed.Orders,
                null,
                cancellationToken);
            return error is not null ? BadRequest(error) : result!;
        }

        if (parsed.Kind == EbayReportParser.EbayReportKind.ListingSales)
        {
            if (parsed.ListingSales.Count == 0)
            {
                return BadRequest(parsed.Warning ?? "No listing-sales rows were found in the report.");
            }
            return await platformOrderFeeEnrichmentService.EnrichEbayListingSalesAsync(parsed.ListingSales, cancellationToken);
        }

        return BadRequest(parsed.Warning ?? "Unsupported eBay CSV.");
    }

    /// <summary>Import refund/fee-credit evidence from marketplace CSVs onto already reconciled sales.</summary>
    [HttpPost("import/refunds-csv")]
    public async Task<ActionResult<ImportMarketplaceRefundsResultDto>> ImportMarketplaceRefundsCsv(
        ImportMarketplaceRefundsCsvRequest request,
        CancellationToken cancellationToken)
    {
        (ImportMarketplaceRefundsResultDto? result, string? error) = await marketplaceRefundImportService.ImportCsvAsync(request, cancellationToken);
        return error is not null ? BadRequest(error) : result!;
    }

    /// <summary>Turn an imported order into ScoutHaul sales. Each mapped line becomes a linked sale carrying the
    /// platform's exact price, with the order's shipping, tax, and fee total apportioned across the items — so the
    /// item nets and the order total reconcile to the marketplace's own figures.</summary>
    [HttpPost("{id:guid}/reconcile")]
    public async Task<ActionResult<PlatformOrderReconcileResultDto>> Reconcile(Guid id, ReconcilePlatformOrderRequest request, CancellationToken cancellationToken)
    {
        PlatformOrder? order = await db.PlatformOrders
            .Include(item => item.Lines)
            .SingleOrDefaultAsync(item => item.Id == id && item.OrganizationId == currentUser.OrganizationId, cancellationToken);

        if (order is null)
        {
            return NotFound();
        }

        Dictionary<Guid, Guid> mapByLine = (request.Lines ?? []).GroupBy(line => line.PlatformOrderLineId).ToDictionary(group => group.Key, group => group.First().InventoryItemId);
        foreach (PlatformOrderLine line in order.Lines.Where(line => line.InventoryItemId.HasValue))
        {
            mapByLine.TryAdd(line.Id, line.InventoryItemId!.Value);
        }
        if (order.Lines.Any(line => !mapByLine.ContainsKey(line.Id)))
        {
            return BadRequest("Every order line must be matched to an inventory item before reconciling.");
        }

        Guid[] itemIds = mapByLine.Values.ToArray();
        if (itemIds.Length != itemIds.Distinct().Count())
        {
            return BadRequest("Each inventory item can be matched to only one order line.");
        }

        List<InventoryItem> items = await db.InventoryItems
            .Include(item => item.BundleComponents).ThenInclude(component => component.ComponentInventoryItem)
            .Where(item => itemIds.Contains(item.Id) && item.OrganizationId == currentUser.OrganizationId)
            .ToListAsync(cancellationToken);
        if (items.Count != itemIds.Length)
        {
            return NotFound("One or more matched inventory items were not found.");
        }

        // A matched item that already carries an active sale (e.g. hand-entered) is CORRECTED in place with the
        // platform's exact figures rather than sold twice — so a recorded history and a fresh order both end up
        // matching eBay. Released (returned/refunded/cancelled) sales don't block a new one.
        List<Sale> existingSales = await db.Sales
            .Include(sale => sale.Refunds)
            .Include(sale => sale.InventoryItem)
            .Include(sale => sale.Listing)
            .Include(sale => sale.Order)
            .Where(sale => itemIds.Contains(sale.InventoryItemId) && sale.OrganizationId == currentUser.OrganizationId && !ReleasedInventoryStatuses.Contains(sale.Status))
            .ToListAsync(cancellationToken);
        Dictionary<Guid, Sale> saleByItem = existingSales
            .GroupBy(sale => sale.InventoryItemId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(sale => sale.SoldAt).First());

        // Each line carries the platform's exact per-item price; the shared order costs are apportioned by price.
        List<BundledSaleItemAllocation> allocationInputs = order.Lines
            .Select(line => new BundledSaleItemAllocation(mapByLine[line.Id], line.UnitPrice * line.Quantity, 0))
            .ToList();
        decimal totalSoldPrice = order.Lines.Sum(line => line.UnitPrice * line.Quantity);

        IReadOnlyList<AllocatedSaleLine> lines;
        try
        {
            lines = BundledSaleAllocator.Allocate(
                allocationInputs, totalSoldPrice, order.ShippingCharged, order.FeeTotal,
                paymentFees: 0, shippingCost: order.SellerShippingCost ?? 0, suppliesCost: 0, otherCosts: 0, order.TaxCollected);
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(exception.Message);
        }

        // The marketplace order becomes a first-class Order — the same entity the storefront uses. This replaces
        // the old SaleGroupId/GroupOrderReference pair, which was an order header smuggled onto the Sale rows.
        Order salesOrder = await orderAssembler.GetOrCreateAsync(
            order.Platform, order.ExternalOrderId, null, order.BuyerReference, order.BuyerName,
            order.OrderedAt, order.ChannelAccountId, order.ItemsSubtotal, order.ShippingCharged, order.TaxCollected,
            cancellationToken,
            order.Carrier,
            order.TrackingNumber,
            order.BuyerEmail,
            order.BuyerNote,
            order.ShipToName,
            order.ShipToPhone,
            order.ShipToLine1,
            order.ShipToLine2,
            order.ShipToCity,
            order.ShipToRegion,
            order.ShipToPostalCode,
            order.ShipToCountry,
            order.PaidAt,
            order.ShippedAt);

        DateTimeOffset soldAt = order.OrderedAt;
        Dictionary<Guid, InventoryItem> byId = items.ToDictionary(item => item.Id);
        Dictionary<Guid, PlatformOrderLine> lineByItem = order.Lines.ToDictionary(line => mapByLine[line.Id], line => line);
        List<Sale> resultSales = [];
        Dictionary<Guid, List<Guid>> removedSaleIdsByPreviousOrder = [];
        int createdCount = 0;
        int correctedCount = 0;

        foreach (AllocatedSaleLine allocated in lines)
        {
            InventoryItem item = byId[allocated.InventoryItemId];
            PlatformOrderLine platformLine = lineByItem[item.Id];
            platformLine.InventoryItemId = item.Id;
            AllocatedSaleLine applied = allocated with
            {
                ShippingCharged = platformLine.ShippingCharged ?? allocated.ShippingCharged,
                PlatformFees = platformLine.PlatformFees ?? allocated.PlatformFees,
                ShippingCost = platformLine.SellerShippingCost ?? allocated.ShippingCost,
                SalesTaxCollected = platformLine.TaxCollected ?? allocated.SalesTaxCollected
            };

            if (saleByItem.TryGetValue(item.Id, out Sale? existing))
            {
                // Correct the recorded sale: overwrite ONLY what the platform reports (price, buyer shipping, tax,
                // fee total; and the shipping label when the platform gives it). The operator's real sale DATE and
                // any supply/other costs are preserved. Net is recomputed; the change is audited.
                string before = SnapshotSale(existing);
                existing.Platform = order.Platform;
                existing.SoldPrice = applied.Price;
                existing.ShippingCharged = applied.ShippingCharged;
                existing.PlatformFees = applied.PlatformFees;
                existing.PaymentFees = applied.PaymentFees;
                existing.SalesTaxCollected = applied.SalesTaxCollected;
                existing.TaxRemittedByPlatform = order.TaxRemittedByPlatform;
                if (platformLine.SellerShippingCost.HasValue || order.SellerShippingCost.HasValue)
                {
                    existing.ShippingCost = applied.ShippingCost;
                }
                if (existing.OrderId is Guid previousOrderId && previousOrderId != salesOrder.Id)
                {
                    if (!removedSaleIdsByPreviousOrder.TryGetValue(previousOrderId, out List<Guid>? removedIds))
                    {
                        removedIds = [];
                        removedSaleIdsByPreviousOrder[previousOrderId] = removedIds;
                    }
                    removedIds.Add(existing.Id);
                }
                existing.OrderId = salesOrder.Id;
                existing.Order = salesOrder;

                existing.RecomputeNet();

                await UpdateInternalSaleObservation(item, existing.SoldAt, applied, cancellationToken);
                await AddCorrection(existing, before, $"Reconciled to {order.Platform} order {order.ExternalOrderId} — exact platform fees.", cancellationToken);
                resultSales.Add(existing);
                correctedCount++;
            }
            else
            {
                RealizedSaleFinancials financials = ResaleFinancialCalculator.CalculateRealizedSale(
                    applied.Price, applied.ShippingCharged, item.PurchasePrice ?? 0, applied.PlatformFees,
                    applied.PaymentFees, applied.ShippingCost, applied.SuppliesCost, applied.OtherCosts);

                Sale sale = new()
                {
                    OrganizationId = currentUser.OrganizationId,
                    InventoryItemId = item.Id,
                    Platform = order.Platform,
                    SoldPrice = applied.Price,
                    ShippingCharged = applied.ShippingCharged,
                    CostOfGoods = item.PurchasePrice ?? 0,
                    PlatformFees = applied.PlatformFees,
                    PaymentFees = applied.PaymentFees,
                    ShippingCost = applied.ShippingCost,
                    SuppliesCost = applied.SuppliesCost,
                    OtherCosts = applied.OtherCosts,
                    SalesTaxCollected = applied.SalesTaxCollected,
                    TaxRemittedByPlatform = order.TaxRemittedByPlatform,
                    SoldAt = soldAt,
                    Notes = TrimToNull(request.Notes),
                    OrderId = salesOrder.Id,
                    Order = salesOrder,
                    InventoryItem = item,
                };
                sale.RecomputeNet();
                db.Sales.Add(sale);
                resultSales.Add(sale);
                createdCount++;

                item.Status = InventoryStatus.Sold;
                item.SoldAt = soldAt;
                InventoryStatePropagation.ApplyBundleComponentInventoryStatus(item, InventoryStatus.Sold, soldAt);

                db.MarketObservations.Add(new MarketObservation
                {
                    OrganizationId = currentUser.OrganizationId,
                    ProductIdentityId = item.ProductIdentityId,
                    InventoryItemId = item.Id,
                    ObservationType = MarketObservationType.InternalSale,
                    Source = order.Platform,
                    Title = item.Title,
                    Price = applied.Price,
                    TotalPrice = applied.Price + applied.ShippingCharged,
                    Condition = item.Condition,
                    ObservedAt = soldAt,
                    SoldAt = soldAt,
                    Notes = $"Reconciled from {order.Platform} order {order.ExternalOrderId}."
                });
            }

            List<ChannelListing> channelListings = await db.ChannelListings
                .Include(listing => listing.ChannelAccount)
                .Where(listing => listing.InventoryItemId == item.Id && listing.OrganizationId == currentUser.OrganizationId)
                .ToListAsync(cancellationToken);
            ChannelListingSync.ApplySale(channelListings, order.Platform, DateTimeOffset.UtcNow);
        }

        await orderAssembler.SyncSalesOrderAsync(salesOrder, resultSales, cancellationToken);
        foreach ((Guid previousOrderId, List<Guid> removedSaleIds) in removedSaleIdsByPreviousOrder)
        {
            Order? previousOrder = await db.Orders.SingleOrDefaultAsync(
                item => item.Id == previousOrderId && item.OrganizationId == currentUser.OrganizationId,
                cancellationToken);
            if (previousOrder is not null)
            {
                await orderAssembler.SyncSalesOrderAsync(previousOrder, [], cancellationToken, removedSaleIds);
                await orderAssembler.PruneEmptyMarketplaceOrderAsync(previousOrder.Id, cancellationToken);
            }
        }

        order.Status = PlatformOrderStatus.Reconciled;
        // Records which Order this imported row reconciled into (the column name predates the Order unification).
        order.ReconciledSaleGroupId = salesOrder.Id;
        order.ReconciledAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        var dtos = resultSales.Select(sale => sale.ToDto()).ToList();
        return new PlatformOrderReconcileResultDto(order.Id, salesOrder.Id, createdCount, correctedCount, dtos);
    }

    [HttpPost("{id:guid}/ignore")]
    public async Task<ActionResult<PlatformOrderDto>> Ignore(Guid id, CancellationToken cancellationToken)
    {
        PlatformOrder? order = await db.PlatformOrders
            .Include(item => item.Lines).ThenInclude(line => line.InventoryItem)
            .SingleOrDefaultAsync(item => item.Id == id && item.OrganizationId == currentUser.OrganizationId, cancellationToken);

        if (order is null)
        {
            return NotFound();
        }

        if (order.Status == PlatformOrderStatus.Reconciled)
        {
            return Conflict("A reconciled order cannot be ignored. Correct its sales instead.");
        }

        order.Status = PlatformOrderStatus.Ignored;
        await db.SaveChangesAsync(cancellationToken);
        return order.ToDto();
    }

    // Serializes a sale's financial state for the immutable before/after correction snapshot (mirrors SalesController).
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

    private async Task AddCorrection(Sale sale, string before, string reason, CancellationToken cancellationToken)
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

    // Keeps the internal-sale comp for this item in step with the corrected price (the recorded date is preserved).
    private async Task UpdateInternalSaleObservation(InventoryItem item, DateTimeOffset soldAt, AllocatedSaleLine allocated, CancellationToken cancellationToken)
    {
        MarketObservation? observation = await db.MarketObservations
            .Where(obs => obs.InventoryItemId == item.Id
                && obs.ObservationType == MarketObservationType.InternalSale
                && obs.OrganizationId == currentUser.OrganizationId
                && obs.SoldAt == soldAt)
            .OrderByDescending(obs => obs.ObservedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (observation is null)
        {
            db.MarketObservations.Add(new MarketObservation
            {
                OrganizationId = currentUser.OrganizationId,
                ProductIdentityId = item.ProductIdentityId,
                InventoryItemId = item.Id,
                ObservationType = MarketObservationType.InternalSale,
                Source = "Internal",
                Title = item.Title,
                Price = allocated.Price,
                TotalPrice = allocated.Price + allocated.ShippingCharged,
                Condition = item.Condition,
                ObservedAt = soldAt,
                SoldAt = soldAt,
                Notes = "Created from platform-order reconciliation.",
            });
            return;
        }

        observation.Price = allocated.Price;
        observation.TotalPrice = allocated.Price + allocated.ShippingCharged;
    }

}
