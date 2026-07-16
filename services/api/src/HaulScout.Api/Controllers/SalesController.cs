using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HaulScout.Api.Authorization;
using HaulScout.Api.Mapping;
using HaulScout.Api.Services;
using HaulScout.Application.Common;
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
public sealed class SalesController(
    HaulScoutDbContext db,
    ICurrentUserContext currentUser,
    OrderAssembler? orderAssembler = null,
    SalesChannelWorkflowService? salesChannels = null,
    SaleRecordingWorkflowService? saleRecording = null,
    SaleFinancialStateService? saleState = null,
    SaleReturnWorkflowService? saleReturns = null) : ControllerBase
{
    private OrderAssembler OrderWriter => orderAssembler ?? new OrderAssembler(db, currentUser);
    private SalesChannelWorkflowService SalesChannels => salesChannels ?? new SalesChannelWorkflowService(db, currentUser);
    private SaleRecordingWorkflowService SaleRecording => saleRecording ?? new SaleRecordingWorkflowService(db, currentUser, OrderWriter, SalesChannels);
    private SaleFinancialStateService SaleState => saleState ?? new SaleFinancialStateService(db, currentUser);
    private SaleReturnWorkflowService SaleReturns => saleReturns ?? new SaleReturnWorkflowService(db, currentUser, SaleState);

    [HttpGet("reference-data")]
    public async Task<ActionResult<SalesReferenceDataDto>> ReferenceData(CancellationToken cancellationToken) =>
        await SalesChannels.ReferenceDataAsync(cancellationToken);

    [HttpPost("fee-estimate")]
    public async Task<ActionResult<ChannelFeeEstimateDto>> EstimateFees(EstimateChannelFeesRequest request, CancellationToken cancellationToken)
    {
        AppResult<ChannelFeeEstimateDto> result = await SalesChannels.EstimateFeesAsync(request, cancellationToken);
        return result.Status == AppResultStatus.Ok && result.Value is not null
            ? result.Value
            : BadRequest(result.Message);
    }

    [HttpGet("candidates")]
    public async Task<ActionResult<IReadOnlyList<SaleCandidateDto>>> Candidates(CancellationToken cancellationToken) =>
        Ok(await SalesChannels.CandidatesAsync(cancellationToken));

    /// <summary>Combine several already-recorded sales into one marketplace order — a shared SaleGroupId + order
    /// reference, the way eBay groups a multi-item order (one package, one tracking). Each sale keeps its own
    /// allocated price, fees, and net; this only links them. Selecting sales already in a group merges them.</summary>
    [HttpPost("group")]
    [RequireOrganizationPermission(HaulScoutPermissions.ManageFinance)]
    public async Task<ActionResult<SaleGroupResultDto>> GroupIntoOrder(GroupSalesRequest request, CancellationToken cancellationToken)
    {
        Guid[] ids = (request.SaleIds ?? []).Distinct().ToArray();
        if (ids.Length < 2)
        {
            return BadRequest("Select at least two sales to combine into one order.");
        }

        List<Sale> sales = await db.Sales
            .Include(sale => sale.InventoryItem)
            .Include(sale => sale.Order)
            .Where(sale => ids.Contains(sale.Id) && sale.OrganizationId == currentUser.OrganizationId)
            .ToListAsync(cancellationToken);
        if (sales.Count != ids.Length)
        {
            return NotFound("One or more sales were not found.");
        }

        string? reference = TrimToNull(request.OrderReference)
            ?? sales.Select(sale => sale.Order?.ExternalOrderId ?? sale.GroupOrderReference).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        List<string> selectedChannels = sales.Select(sale => sale.Platform).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (selectedChannels.Count > 1)
        {
            return BadRequest("Sales from different channels cannot be combined into one marketplace order.");
        }
        string channel = selectedChannels[0];

        Order? order = sales.Select(sale => sale.Order).FirstOrDefault(existing => existing is not null);
        if (order is null)
        {
            DateTimeOffset orderedAt = sales.Min(sale => sale.SoldAt);
            order = await OrderWriter.GetOrCreateAsync(
                channel,
                reference,
                externalOrderUrl: null,
                buyerReference: null,
                buyerName: null,
                orderedAt,
                channelAccountId: null,
                itemsSubtotal: sales.Sum(sale => sale.SoldPrice),
                shippingTotal: sales.Sum(sale => sale.ShippingCharged),
                taxTotal: sales.Sum(sale => sale.SalesTaxCollected),
                cancellationToken);
        }
        Guid groupId = order.Id;

        foreach (Sale sale in sales)
        {
            sale.SaleGroupId = groupId;
            sale.GroupOrderReference = reference;
            sale.OrderId = order.Id;
            sale.Order = order;
        }

        await OrderWriter.SyncSalesOrderAsync(order, sales, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        List<SaleDto> dtos = sales.OrderByDescending(sale => sale.SoldAt).Select(sale => sale.ToDto()).ToList();
        return new SaleGroupResultDto(groupId, reference, dtos);
    }

    /// <summary>Split selected sale lines out of a combined order. Every sale still keeps a real Order header so
    /// Orders/Customers remain complete; only the shared-order relationship is split.</summary>
    [HttpPost("ungroup")]
    [RequireOrganizationPermission(HaulScoutPermissions.ManageFinance)]
    public async Task<ActionResult<IReadOnlyList<SaleDto>>> RemoveFromOrder(UngroupSalesRequest request, CancellationToken cancellationToken)
    {
        Guid[] ids = (request.SaleIds ?? []).Distinct().ToArray();
        if (ids.Length == 0)
        {
            return BadRequest("Select at least one sale to remove from its order.");
        }

        List<Sale> selected = await db.Sales
            .Include(sale => sale.InventoryItem)
            .Include(sale => sale.Order)
            .Where(sale => ids.Contains(sale.Id) && sale.OrganizationId == currentUser.OrganizationId)
            .ToListAsync(cancellationToken);
        if (selected.Count == 0)
        {
            return NotFound();
        }

        List<Guid> affectedOrders = selected.Where(sale => sale.OrderId.HasValue).Select(sale => sale.OrderId!.Value).Distinct().ToList();
        List<Sale> orderSales = affectedOrders.Count == 0
            ? []
            : await db.Sales
                .Include(sale => sale.InventoryItem)
                .Include(sale => sale.Order)
                .Where(sale => sale.OrderId != null && affectedOrders.Contains(sale.OrderId.Value) && sale.OrganizationId == currentUser.OrganizationId)
                .ToListAsync(cancellationToken);

        HashSet<Guid> selectedIds = ids.ToHashSet();
        foreach (Guid orderId in affectedOrders)
        {
            List<Sale> members = orderSales.Where(sale => sale.OrderId == orderId).OrderBy(sale => sale.SoldAt).ToList();
            if (members.Count <= 1)
            {
                continue;
            }

            Order? originalOrder = members.Select(sale => sale.Order).FirstOrDefault(order => order is not null);
            if (originalOrder is null)
            {
                continue;
            }

            List<Sale> selectedMembers = members.Where(sale => selectedIds.Contains(sale.Id)).ToList();
            if (selectedMembers.Count == 0)
            {
                continue;
            }

            // If the operator selects the whole order, keep one sale on the original header and split the rest.
            // This avoids creating an orphaned empty order while still dissolving the bundle presentation.
            List<Sale> toSplit = selectedMembers.Count == members.Count ? selectedMembers.Skip(1).ToList() : selectedMembers;
            foreach (Sale sale in toSplit)
            {
                sale.SaleGroupId = null;
                sale.GroupOrderReference = null;
                Order splitOrder = await OrderWriter.GetOrCreateAsync(
                    sale.Platform,
                    externalOrderId: null,
                    externalOrderUrl: null,
                    buyerReference: null,
                    buyerName: null,
                    sale.SoldAt,
                    channelAccountId: null,
                    itemsSubtotal: sale.SoldPrice,
                    shippingTotal: sale.ShippingCharged,
                    taxTotal: sale.SalesTaxCollected,
                    cancellationToken);
                sale.OrderId = splitOrder.Id;
                sale.Order = splitOrder;
                await OrderWriter.SyncSalesOrderAsync(splitOrder, [sale], cancellationToken);
            }

            List<Sale> remaining = members.Where(sale => !toSplit.Any(split => split.Id == sale.Id)).ToList();
            if (remaining.Count == 1)
            {
                remaining[0].SaleGroupId = null;
                remaining[0].GroupOrderReference = null;
            }
            else
            {
                foreach (Sale member in remaining)
                {
                    member.SaleGroupId = originalOrder.Id;
                    member.GroupOrderReference = originalOrder.ExternalOrderId ?? originalOrder.OrderNumber;
                }
            }

            await OrderWriter.SyncSalesOrderAsync(originalOrder, remaining, cancellationToken, toSplit.Select(sale => sale.Id).ToList());
        }

        await db.SaveChangesAsync(cancellationToken);
        return selected.Select(sale => sale.ToDto()).ToList();
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SaleDto>>> List(
        [FromQuery] SaleStatus? status,
        CancellationToken cancellationToken)
    {
        IQueryable<Sale> query = db.Sales
            .Include(item => item.InventoryItem)
            .Include(item => item.Order)
            .Include(item => item.Refunds)
            .Where(item => item.OrganizationId == currentUser.OrganizationId);

        if (status.HasValue)
        {
            query = query.Where(item => item.Status == status.Value);
        }

        List<Sale> sales = await query
            .OrderByDescending(item => item.SoldAt)
            .Take(200)
            .ToListAsync(cancellationToken);

        return sales.Select(item => item.ToDto()).ToList();
    }

    /// <summary>Searchable, sortable, paged sales for the finance table, plus the P&amp;L summary over the whole
    /// filtered set (so the KPIs stay correct while the table pages). Separate from <see cref="List"/> to keep the
    /// plain array response stable for the mobile app.</summary>
    [HttpGet("browse")]
    public async Task<ActionResult<SaleListDto>> Browse(
        [FromQuery] SaleStatus? status,
        [FromQuery] string? search,
        [FromQuery] string? sort,
        [FromQuery] bool desc = true,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        IQueryable<Sale> query = db.Sales
            .Include(item => item.InventoryItem)
            .Include(item => item.Order)
            .Include(item => item.Refunds)
            .Where(item => item.OrganizationId == currentUser.OrganizationId);

        if (status.HasValue)
        {
            query = query.Where(item => item.Status == status.Value);
        }

        string? term = TrimToNull(search)?.ToLower();
        if (term is not null)
        {
            query = query.Where(item =>
                (item.InventoryItem != null && item.InventoryItem.Title.ToLower().Contains(term))
                || item.Platform.ToLower().Contains(term)
                || (item.Order != null && item.Order.ExternalOrderId != null && item.Order.ExternalOrderId.ToLower().Contains(term))
                || (item.Order != null && item.Order.TrackingNumber != null && item.Order.TrackingNumber.ToLower().Contains(term))
                || (item.GroupOrderReference != null && item.GroupOrderReference.ToLower().Contains(term)));
        }

        List<Sale> filtered = await query.ToListAsync(cancellationToken);
        List<Sale> realized = filtered.Where(item => SaleWorkflow.IsRealized(item.Status)).ToList();

        decimal itemSales = realized.Sum(item => item.SoldPrice);
        decimal buyerShipping = realized.Sum(item => item.ShippingCharged);
        decimal taxCollected = realized.Sum(item => item.SalesTaxCollected);
        decimal platformFees = realized.Sum(item => item.PlatformFees);
        decimal paymentFees = realized.Sum(item => item.PaymentFees);
        decimal shippingLabels = realized.Sum(item => item.ShippingCost);
        decimal suppliesAndOther = realized.Sum(item => item.SuppliesCost + item.OtherCosts);
        decimal sellingCosts = platformFees + paymentFees + shippingLabels + suppliesAndOther;
        SalesSummaryDto summary = new(
            realized.Count,
            itemSales,
            buyerShipping,
            taxCollected,
            itemSales + buyerShipping + taxCollected,
            platformFees,
            paymentFees,
            shippingLabels,
            suppliesAndOther,
            sellingCosts,
            itemSales + buyerShipping - sellingCosts,
            realized.Sum(item => item.CostOfGoods),
            realized.Sum(item => item.NetProfit),
            realized.Sum(item => ResaleFinancialCalculator.SalesTaxOwed(item.SalesTaxCollected, item.TaxRemittedByPlatform)),
            realized.Sum(item => item.Refunds.Sum(refund => refund.ItemAmount + refund.ShippingRefunded)));

        IEnumerable<Sale> ordered = sort?.ToLowerInvariant() switch
        {
            "netprofit" => desc ? filtered.OrderByDescending(item => item.NetProfit) : filtered.OrderBy(item => item.NetProfit),
            "soldprice" => desc ? filtered.OrderByDescending(item => item.SoldPrice) : filtered.OrderBy(item => item.SoldPrice),
            "item" => desc ? filtered.OrderByDescending(item => item.InventoryItem!.Title) : filtered.OrderBy(item => item.InventoryItem!.Title),
            _ => desc ? filtered.OrderByDescending(item => item.SoldAt) : filtered.OrderBy(item => item.SoldAt),
        };

        List<SaleDto> items = ordered.Skip((page - 1) * pageSize).Take(pageSize).Select(item => item.ToDto()).ToList();
        int pageCount = (int)Math.Ceiling(filtered.Count / (double)pageSize);
        return new SaleListDto(items, filtered.Count, page, pageSize, Math.Max(1, pageCount), summary);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SaleDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        Sale? sale = await db.Sales
            .Include(item => item.InventoryItem)
            .Include(item => item.Order)
            .Include(item => item.Refunds)
            .SingleOrDefaultAsync(item => item.Id == id && item.OrganizationId == currentUser.OrganizationId, cancellationToken);

        return sale is null ? NotFound() : sale.ToDto();
    }

    /// <summary>Permanently delete a recorded sale (its corrections, refunds, and shipment cascade away). The item
    /// is released back to sellable inventory unless another live sale still holds it; the internal-sale comp is
    /// removed and the deletion is written to the audit log.</summary>
    [HttpDelete("{id:guid}")]
    [RequireOrganizationPermission(HaulScoutPermissions.ManageFinance)]
    public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        Sale? sale = await db.Sales
            .Include(item => item.InventoryItem)
                .ThenInclude(item => item!.BundleComponents)
                    .ThenInclude(item => item.ComponentInventoryItem)
            .Include(item => item.Listing)
            .Include(item => item.Order)
            .SingleOrDefaultAsync(item => item.Id == id && item.OrganizationId == currentUser.OrganizationId, cancellationToken);

        if (sale is null)
        {
            return NotFound();
        }

        if (sale.InventoryItem is not null)
        {
            bool otherActiveSale = await db.Sales.AnyAsync(
                other => other.InventoryItemId == sale.InventoryItemId && other.Id != id
                    && other.OrganizationId == currentUser.OrganizationId && !SaleWorkflow.InventoryReleaseStatuses.Contains(other.Status),
                cancellationToken);
            if (!otherActiveSale)
            {
                sale.InventoryItem.Status = sale.ListingId.HasValue ? InventoryStatus.Listed : InventoryStatus.ReadyToList;
                sale.InventoryItem.SoldAt = null;
                InventoryStatePropagation.ApplyBundleComponentInventoryStatus(sale.InventoryItem, sale.InventoryItem.Status, null);
            }
        }

        if (sale.Listing is not null && sale.Listing.Status == ListingStatus.Sold)
        {
            sale.Listing.Status = ListingStatus.Active;
            sale.Listing.EndedAt = null;
        }

        List<MarketObservation> observations = await db.MarketObservations
            .Where(observation => observation.InventoryItemId == sale.InventoryItemId
                && observation.ObservationType == MarketObservationType.InternalSale
                && observation.OrganizationId == currentUser.OrganizationId
                && observation.SoldAt == sale.SoldAt)
            .ToListAsync(cancellationToken);
        db.MarketObservations.RemoveRange(observations);

        db.AuditLogs.Add(new AuditLog
        {
            OrganizationId = currentUser.OrganizationId,
            ActorUserId = currentUser.UserId,
            EntityName = "Sale",
            EntityId = id,
            Action = "Deleted",
            DetailsJson = SnapshotSale(sale),
        });

        // Remove the sale's dependents explicitly (rather than lean on DB cascade) so the delete is deterministic.
        db.Refunds.RemoveRange(await db.Refunds.Where(refund => refund.SaleId == id && refund.OrganizationId == currentUser.OrganizationId).ToListAsync(cancellationToken));
        db.SaleCorrections.RemoveRange(await db.SaleCorrections.Where(correction => correction.SaleId == id && correction.OrganizationId == currentUser.OrganizationId).ToListAsync(cancellationToken));
        Shipment? shipment = await db.Shipments.SingleOrDefaultAsync(item => item.SaleId == id && item.OrganizationId == currentUser.OrganizationId, cancellationToken);
        if (shipment is not null)
        {
            db.Shipments.Remove(shipment);
        }

        db.Sales.Remove(sale);
        Guid? affectedOrderId = sale.OrderId;
        if (sale.Order is not null)
        {
            await OrderWriter.SyncSalesOrderAsync(sale.Order, [], cancellationToken, [sale.Id]);
        }
        await db.SaveChangesAsync(cancellationToken);

        // Deleting the last sale on a marketplace order would otherwise strand an empty phantom order (no lines,
        // $0, no buyer). Prune it once the sale removal is persisted so the emptiness check is accurate.
        if (affectedOrderId is Guid orderId)
        {
            await OrderWriter.PruneEmptyMarketplaceOrderAsync(orderId, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }
        return NoContent();
    }

    [HttpPost]
    [RequireOrganizationPermission(HaulScoutPermissions.ManageFinance)]
    public async Task<ActionResult<SaleDto>> Record(RecordSaleRequest request, CancellationToken cancellationToken)
    {
        AppResult<SaleDto> result = await SaleRecording.RecordAsync(request, cancellationToken);
        return result.Status switch
        {
            AppResultStatus.Ok when result.Value is not null => CreatedAtAction(nameof(GetProfit), new { id = result.Value.Id }, result.Value),
            AppResultStatus.NotFound => NotFound(result.Message),
            AppResultStatus.Conflict => Conflict(result.Message),
            AppResultStatus.Validation => BadRequest(result.Message),
            _ => BadRequest(result.Message),
        };
    }

    /// <summary>Record one bundled order covering several items — one package, one tracking. Creates a linked
    /// sale per item with the order's price and shared costs allocated across them, so each item keeps its own net.</summary>
    [HttpPost("bundled")]
    [RequireOrganizationPermission(HaulScoutPermissions.ManageFinance)]
    public async Task<ActionResult<BundledSaleResultDto>> RecordBundled(RecordBundledSaleRequest request, CancellationToken cancellationToken)
    {
        AppResult<BundledSaleResultDto> result = await SaleRecording.RecordBundledAsync(request, cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet("{id:guid}/profit")]
    public async Task<ActionResult<RealizedProfitDto>> GetProfit(Guid id, CancellationToken cancellationToken)
    {
        Sale? sale = await db.Sales
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
        Sale? sale = await db.Sales
            .Include(item => item.InventoryItem)
                .ThenInclude(item => item!.BundleComponents)
                    .ThenInclude(item => item.ComponentInventoryItem)
            .Include(item => item.Listing)
            .Include(item => item.Order)
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

            InventoryStatePropagation.ApplyBundleComponentInventoryStatus(
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

        if (sale.Order is not null)
        {
            if (carrier is not null) sale.Order.ShippingCarrier = carrier;
            if (trackingNumber is not null) sale.Order.TrackingNumber = trackingNumber;
            if (request.Status == SaleStatus.Shipped) sale.Order.ShippedAt ??= occurredAt;
            if (request.Status == SaleStatus.Delivered) sale.Order.DeliveredAt ??= occurredAt;
            await OrderWriter.SyncSalesOrderAsync(sale.Order, [sale], cancellationToken);
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

        string? channel = await SalesChannels.CanonicalChannelAsync(request.Platform, cancellationToken);
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

        Sale? sale = await db.Sales
            .Include(item => item.InventoryItem)
                .ThenInclude(item => item!.BundleComponents)
                    .ThenInclude(item => item.ComponentInventoryItem)
            .Include(item => item.Listing)
            .Include(item => item.Order)
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
        if (sale.InventoryItem is not null && !SaleWorkflow.InventoryReleaseStatuses.Contains(sale.Status))
        {
            InventoryStatePropagation.ApplyBundleComponentInventoryStatus(
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
        if (sale.Order is not null)
        {
            await OrderWriter.SyncSalesOrderAsync(sale.Order, [sale], cancellationToken);
        }

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

        List<SaleCorrection> corrections = await db.SaleCorrections
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

        List<Refund> refunds = await db.Refunds
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

        Sale? sale = await db.Sales
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
        Refund refund = new()
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
        Sale? sale = await db.Sales
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

        Refund? refund = sale.Refunds.SingleOrDefault(item => item.Id == refundId);
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

    // ── Returns (RMA lifecycle) ────────────────────────────────────────────
    // A return is the process; the Refund is the money event issued at the end. Requested → LabelIssued (seller pays
    // return shipping, booked now) → Received (inspected) → Refunded (issues the Refund, restock if resellable), or
    // Rejected/Cancelled. Refunds are never issued before Received. The rare "keep the item, refund anyway" case skips
    // all of this and posts a Refund directly (RecordRefund).

    [HttpGet("{id:guid}/returns")]
    public async Task<ActionResult<IReadOnlyList<ReturnDto>>> ListReturns(Guid id, CancellationToken cancellationToken) =>
        (await SaleReturns.ListAsync(id, cancellationToken)).ToList();

    [HttpPost("{id:guid}/returns")]
    [RequireOrganizationPermission(HaulScoutPermissions.ManageFinance)]
    public async Task<ActionResult<ReturnDto>> OpenReturn(Guid id, OpenReturnRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await SaleReturns.OpenAsync(id, request, cancellationToken));

    [HttpPost("returns/{returnId:guid}/label")]
    [RequireOrganizationPermission(HaulScoutPermissions.ManageFinance)]
    public async Task<ActionResult<ReturnDto>> IssueReturnLabel(Guid returnId, IssueReturnLabelRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await SaleReturns.IssueLabelAsync(returnId, request, cancellationToken));

    [HttpPost("returns/{returnId:guid}/receive")]
    [RequireOrganizationPermission(HaulScoutPermissions.ManageFinance)]
    public async Task<ActionResult<ReturnDto>> ReceiveReturn(Guid returnId, CancellationToken cancellationToken) =>
        ToActionResult(await SaleReturns.ReceiveAsync(returnId, cancellationToken));

    [HttpPost("returns/{returnId:guid}/resolve")]
    [RequireOrganizationPermission(HaulScoutPermissions.ManageFinance)]
    public async Task<ActionResult<ReturnDto>> ResolveReturn(Guid returnId, ResolveReturnRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await SaleReturns.ResolveAsync(returnId, request, cancellationToken));

    [HttpPost("returns/{returnId:guid}/reject")]
    [RequireOrganizationPermission(HaulScoutPermissions.ManageFinance)]
    public async Task<ActionResult<ReturnDto>> RejectReturn(Guid returnId, RejectReturnRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await SaleReturns.RejectAsync(returnId, request, cancellationToken));

    [HttpPost("returns/{returnId:guid}/cancel")]
    [RequireOrganizationPermission(HaulScoutPermissions.ManageFinance)]
    public async Task<ActionResult<ReturnDto>> CancelReturn(Guid returnId, CancellationToken cancellationToken) =>
        ToActionResult(await SaleReturns.CancelAsync(returnId, cancellationToken));

    private ActionResult<T> ToActionResult<T>(AppResult<T> result) where T : class =>
        result.Status switch
        {
            AppResultStatus.Ok when result.Value is not null => result.Value,
            AppResultStatus.NotFound => NotFound(result.Message),
            AppResultStatus.Conflict => Conflict(result.Message),
            AppResultStatus.Validation => BadRequest(result.Message),
            _ => BadRequest(result.Message),
        };

    // Sale money state + audit trail live in SaleFinancialStateService so refunds, returns, and corrections all
    // derive the same invariants. These thin wrappers keep the existing call sites readable.
    private static void ApplyRefundState(Sale sale) => SaleFinancialStateService.ApplyRefundState(sale);

    private static string SnapshotSale(Sale sale) => SaleFinancialStateService.Snapshot(sale);

    private Task AddCorrection(Sale sale, string before, string reason, CancellationToken cancellationToken) =>
        SaleState.AddCorrectionAsync(sale, before, reason, cancellationToken);
}
