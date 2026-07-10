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

/// <summary>
/// The landing zone for marketplace orders the browser extension scrapes from the operator's logged-in sessions.
/// Ingest is idempotent (keyed on platform + external order id); reconciliation turns a scraped order into
/// ScoutHaul <see cref="Sale"/>(s) using the platform's OWN money figures — item prices, buyer shipping, tax, and
/// the exact fee total — so our finances match the marketplace's report by definition. No marketplace API is used.
/// </summary>
[ApiController]
[Route("api/platform-orders")]
[RequireOrganizationPermission(HaulScoutPermissions.ManageFinance)]
public sealed class PlatformOrdersController(HaulScoutDbContext db, ICurrentUserContext currentUser, OrderAssembler orderAssembler) : ControllerBase
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

        Dictionary<Guid, (Guid Id, string Title, bool AlreadySold)> suggestions = await BuildSuggestions(orders, cancellationToken);
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

        Dictionary<Guid, (Guid Id, string Title, bool AlreadySold)> suggestions = await BuildSuggestions([order], cancellationToken);
        return order.ToDto(suggestions);
    }

    /// <summary>Idempotent batch upsert of scraped orders. Re-ingesting an order that is still <c>Imported</c>
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

        (IngestPlatformOrdersResultDto? result, string? error) = await IngestOrders(request.Platform, request.Orders, channelAccountId, cancellationToken);
        return error is not null ? BadRequest(error) : result!;
    }

    /// <summary>Import a marketplace's own CSV export (eBay Seller Hub → Performance → Sales report today) — the
    /// authoritative fee source — parsed server-side into the same idempotent ingest as the extension push.</summary>
    [HttpPost("import/ebay-csv")]
    public async Task<ActionResult<IngestPlatformOrdersResultDto>> ImportEbayCsv(ImportPlatformOrdersCsvRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Csv))
        {
            return BadRequest("Paste or upload the eBay report CSV.");
        }

        EbayReportParser.ParseResult parsed = EbayReportParser.ParseSalesReport(request.Csv);
        if (parsed.Orders.Count == 0)
        {
            return BadRequest(parsed.Warning ?? "No sold-listing rows were found in the report.");
        }

        (IngestPlatformOrdersResultDto? result, string? error) = await IngestOrders(string.IsNullOrWhiteSpace(request.Platform) ? "eBay" : request.Platform, parsed.Orders, null, cancellationToken);
        return error is not null ? BadRequest(error) : result!;
    }

    // Shared idempotent upsert for both the extension push and CSV import. Validates the whole batch first so a
    // bad row fails atomically rather than half-importing; Reconciled/Ignored orders are never overwritten.
    private async Task<(IngestPlatformOrdersResultDto? Result, string? Error)> IngestOrders(
        string? defaultPlatform, IReadOnlyList<IngestPlatformOrderInput> orders, Guid? channelAccountId, CancellationToken cancellationToken)
    {
        List<string> existingChannels = await ExistingChannels(cancellationToken);
        string? defaultChannel = SalesChannelCatalog.CanonicalValue(defaultPlatform, existingChannels);

        List<(IngestPlatformOrderInput Input, string Channel, string ExternalOrderId)> normalized = [];
        foreach (IngestPlatformOrderInput input in orders)
        {
            string externalOrderId = (input.ExternalOrderId ?? string.Empty).Trim();
            if (externalOrderId.Length == 0)
            {
                return (null, "Every order needs an external order id.");
            }

            string? channel = SalesChannelCatalog.CanonicalValue(input.Platform, existingChannels) ?? defaultChannel;
            if (channel is null)
            {
                return (null, $"Order {externalOrderId} has no recognized sales channel.");
            }

            if (input.Lines is null || input.Lines.Count == 0)
            {
                return (null, $"Order {externalOrderId} has no line items.");
            }

            if (new[] { input.ItemsSubtotal, input.ShippingCharged, input.TaxCollected, input.FeeTotal }.Any(value => value < 0)
                || (input.ShippingCost.HasValue && input.ShippingCost.Value < 0)
                || (input.PayoutAmount.HasValue && input.PayoutAmount.Value < 0)
                || input.Lines.Any(line => line.UnitPrice < 0 || line.Quantity <= 0))
            {
                return (null, $"Order {externalOrderId} has negative money or a non-positive quantity.");
            }

            normalized.Add((input, channel, externalOrderId));
        }

        HashSet<string> keys = normalized.Select(entry => entry.ExternalOrderId).ToHashSet();
        List<PlatformOrder> existing = await db.PlatformOrders
            .Include(order => order.Lines)
            .Where(order => order.OrganizationId == currentUser.OrganizationId && keys.Contains(order.ExternalOrderId))
            .ToListAsync(cancellationToken);

        int created = 0, updated = 0, skipped = 0;
        List<Guid> touched = [];

        foreach ((IngestPlatformOrderInput input, string channel, string externalOrderId) in normalized)
        {
            PlatformOrder? order = existing.SingleOrDefault(candidate =>
                candidate.Platform == channel && candidate.ExternalOrderId == externalOrderId);

            if (order is null)
            {
                order = new PlatformOrder
                {
                    OrganizationId = currentUser.OrganizationId,
                    Platform = channel,
                    ExternalOrderId = externalOrderId,
                    ChannelAccountId = channelAccountId,
                };
                db.PlatformOrders.Add(order);
                existing.Add(order);
                ApplyScrapedFields(order, input, channelAccountId);
                AddLines(order, input);
                created++;
            }
            else if (order.Status == PlatformOrderStatus.Imported)
            {
                ApplyScrapedFields(order, input, channelAccountId);
                db.PlatformOrderLines.RemoveRange(order.Lines);
                AddLines(order, input);
                updated++;
            }
            else
            {
                // Reconciled/Ignored orders are posted or dismissed — never silently overwrite their numbers.
                skipped++;
                continue;
            }

            touched.Add(order.Id);
        }

        await db.SaveChangesAsync(cancellationToken);

        List<PlatformOrder> refreshed = await db.PlatformOrders
            .Include(order => order.Lines).ThenInclude(line => line.InventoryItem)
            .Where(order => order.OrganizationId == currentUser.OrganizationId && touched.Contains(order.Id))
            .ToListAsync(cancellationToken);
        Dictionary<Guid, (Guid Id, string Title, bool AlreadySold)> suggestions = await BuildSuggestions(refreshed, cancellationToken);
        List<PlatformOrderDto> dtos = refreshed.Select(order => order.ToDto(suggestions)).ToList();
        return (new IngestPlatformOrdersResultDto(created, updated, skipped, dtos), null);
    }

    /// <summary>Turn a scraped order into ScoutHaul sales. Each mapped line becomes a linked sale carrying the
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

        if (order.Status == PlatformOrderStatus.Reconciled)
        {
            return Conflict("This order has already been reconciled into sales.");
        }

        Dictionary<Guid, Guid> mapByLine = (request.Lines ?? []).GroupBy(line => line.PlatformOrderLineId).ToDictionary(group => group.Key, group => group.First().InventoryItemId);
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
            .Include(sale => sale.InventoryItem)
            .Include(sale => sale.Listing)
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
            cancellationToken);

        DateTimeOffset soldAt = order.OrderedAt;
        Dictionary<Guid, InventoryItem> byId = items.ToDictionary(item => item.Id);
        Dictionary<Guid, PlatformOrderLine> lineByItem = order.Lines.ToDictionary(line => mapByLine[line.Id], line => line);
        List<Sale> resultSales = [];
        int createdCount = 0;
        int correctedCount = 0;

        foreach (AllocatedSaleLine allocated in lines)
        {
            InventoryItem item = byId[allocated.InventoryItemId];
            lineByItem[item.Id].InventoryItemId = item.Id;

            if (saleByItem.TryGetValue(item.Id, out Sale? existing))
            {
                // Correct the recorded sale: overwrite ONLY what the platform reports (price, buyer shipping, tax,
                // fee total; and the shipping label when the platform gives it). The operator's real sale DATE and
                // any supply/other costs are preserved. Net is recomputed; the change is audited.
                string before = SnapshotSale(existing);
                existing.Platform = order.Platform;
                existing.SoldPrice = allocated.Price;
                existing.ShippingCharged = allocated.ShippingCharged;
                existing.PlatformFees = allocated.PlatformFees;
                existing.PaymentFees = allocated.PaymentFees;
                existing.SalesTaxCollected = allocated.SalesTaxCollected;
                existing.TaxRemittedByPlatform = order.TaxRemittedByPlatform;
                if (order.SellerShippingCost.HasValue)
                {
                    existing.ShippingCost = allocated.ShippingCost;
                }
                existing.Order = salesOrder;

                RealizedSaleFinancials corrected = ResaleFinancialCalculator.CalculateRealizedSale(
                    existing.SoldPrice, existing.ShippingCharged, existing.CostOfGoods, existing.PlatformFees,
                    existing.PaymentFees, existing.ShippingCost, existing.SuppliesCost, existing.OtherCosts);
                existing.NetProfit = corrected.NetProfit;

                await UpdateInternalSaleObservation(item, existing.SoldAt, allocated, cancellationToken);
                await AddCorrection(existing, before, $"Reconciled to {order.Platform} order {order.ExternalOrderId} — exact platform fees.", cancellationToken);
                resultSales.Add(existing);
                correctedCount++;
            }
            else
            {
                RealizedSaleFinancials financials = ResaleFinancialCalculator.CalculateRealizedSale(
                    allocated.Price, allocated.ShippingCharged, item.PurchasePrice ?? 0, allocated.PlatformFees,
                    allocated.PaymentFees, allocated.ShippingCost, allocated.SuppliesCost, allocated.OtherCosts);

                Sale sale = new()
                {
                    OrganizationId = currentUser.OrganizationId,
                    InventoryItemId = item.Id,
                    Platform = order.Platform,
                    SoldPrice = allocated.Price,
                    ShippingCharged = allocated.ShippingCharged,
                    CostOfGoods = item.PurchasePrice ?? 0,
                    PlatformFees = allocated.PlatformFees,
                    PaymentFees = allocated.PaymentFees,
                    ShippingCost = allocated.ShippingCost,
                    SuppliesCost = allocated.SuppliesCost,
                    OtherCosts = allocated.OtherCosts,
                    SalesTaxCollected = allocated.SalesTaxCollected,
                    TaxRemittedByPlatform = order.TaxRemittedByPlatform,
                    NetProfit = financials.NetProfit,
                    SoldAt = soldAt,
                    Notes = TrimToNull(request.Notes),
                    Order = salesOrder,
                    InventoryItem = item,
                };
                db.Sales.Add(sale);
                resultSales.Add(sale);
                createdCount++;

                item.Status = InventoryStatus.Sold;
                item.SoldAt = soldAt;
                ApplyBundleComponentInventoryStatus(item, InventoryStatus.Sold, soldAt);

                db.MarketObservations.Add(new MarketObservation
                {
                    OrganizationId = currentUser.OrganizationId,
                    ProductIdentityId = item.ProductIdentityId,
                    InventoryItemId = item.Id,
                    ObservationType = MarketObservationType.InternalSale,
                    Source = order.Platform,
                    Title = item.Title,
                    Price = allocated.Price,
                    TotalPrice = allocated.Price + allocated.ShippingCharged,
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

    private static void ApplyScrapedFields(PlatformOrder order, IngestPlatformOrderInput input, Guid? channelAccountId)
    {
        if (channelAccountId.HasValue)
        {
            order.ChannelAccountId = channelAccountId;
        }
        order.BuyerName = TrimToNull(input.BuyerName);
        order.BuyerReference = TrimToNull(input.BuyerReference);
        order.OrderedAt = input.OrderedAt;
        order.ItemsSubtotal = input.ItemsSubtotal;
        order.ShippingCharged = input.ShippingCharged;
        order.TaxCollected = input.TaxCollected;
        order.FeeTotal = input.FeeTotal;
        order.SellerShippingCost = input.ShippingCost;
        order.PayoutAmount = input.PayoutAmount;
        order.Currency = string.IsNullOrWhiteSpace(input.Currency) ? "USD" : input.Currency.Trim().ToUpperInvariant();
        order.TaxRemittedByPlatform = input.TaxRemittedByPlatform;
        order.FeeBreakdownJson = TrimToNull(input.FeeBreakdownJson);
        order.Carrier = TrimToNull(input.Carrier);
        order.TrackingNumber = TrimToNull(input.TrackingNumber);
        order.RawJson = TrimToNull(input.RawJson);
        order.ScrapedAt = DateTimeOffset.UtcNow;
    }

    // Adds lines against the order's key directly (rather than via the nav collection) so a re-ingest's
    // remove-old/add-new never mixes deleted and new rows in one tracked collection.
    private void AddLines(PlatformOrder order, IngestPlatformOrderInput input)
    {
        foreach (IngestPlatformOrderLineInput line in input.Lines)
        {
            db.PlatformOrderLines.Add(new PlatformOrderLine
            {
                OrganizationId = currentUser.OrganizationId,
                PlatformOrderId = order.Id,
                Title = string.IsNullOrWhiteSpace(line.Title) ? "Untitled item" : line.Title.Trim(),
                UnitPrice = line.UnitPrice,
                Quantity = line.Quantity <= 0 ? 1 : line.Quantity,
                ExternalItemId = TrimToNull(line.ExternalItemId),
                Sku = TrimToNull(line.Sku),
            });
        }
    }

    /// <summary>Best-effort auto-match of unreconciled order lines to inventory — first by the platform's own listing
    /// id (via <see cref="ChannelListing.ExternalListingId"/>), then by <see cref="TitleMatch"/> when there is no
    /// link yet. Suggestions include whether the item is already sold (so a match points at the sale to correct).
    /// Only surfaces suggestions; the operator always confirms — nothing is posted from here.</summary>
    private async Task<Dictionary<Guid, (Guid Id, string Title, bool AlreadySold)>> BuildSuggestions(IReadOnlyList<PlatformOrder> orders, CancellationToken cancellationToken)
    {
        Dictionary<Guid, (Guid, string, bool)> suggestions = new();
        List<PlatformOrderLine> openLines = orders
            .Where(order => order.Status != PlatformOrderStatus.Reconciled)
            .SelectMany(order => order.Lines.Where(line => line.InventoryItemId is null))
            .ToList();
        if (openLines.Count == 0)
        {
            return suggestions;
        }

        // Inventory pool for title matching + sold-state lookup (bounded to the most recently touched items).
        var inventory = await db.InventoryItems
            .Where(item => item.OrganizationId == currentUser.OrganizationId && !item.IsArchived)
            .OrderByDescending(item => item.UpdatedAt)
            .Select(item => new { item.Id, item.Title, item.Status })
            .Take(2000)
            .ToListAsync(cancellationToken);
        Dictionary<Guid, string> titleById = inventory.ToDictionary(item => item.Id, item => item.Title);
        Dictionary<Guid, bool> soldById = inventory.ToDictionary(item => item.Id, item => InventoryLifecycle.IsSold(item.Status));
        List<(Guid Id, string Title)> titleCandidates = inventory.Select(item => (item.Id, item.Title)).ToList();

        List<string> externalIds = openLines.Where(line => line.ExternalItemId is not null).Select(line => line.ExternalItemId!).Distinct().ToList();
        var byExternalId = externalIds.Count == 0
            ? []
            : await db.ChannelListings
                .Where(listing => listing.OrganizationId == currentUser.OrganizationId
                    && listing.ExternalListingId != null && externalIds.Contains(listing.ExternalListingId))
                .Select(listing => new { listing.ExternalListingId, listing.InventoryItemId })
                .ToListAsync(cancellationToken);

        foreach (PlatformOrderLine line in openLines)
        {
            Guid? matchId = line.ExternalItemId is null
                ? null
                : byExternalId.FirstOrDefault(candidate => candidate.ExternalListingId == line.ExternalItemId)?.InventoryItemId;
            matchId ??= TitleMatch.Best(line.Title, titleCandidates);
            if (matchId is Guid id && titleById.TryGetValue(id, out string? title))
            {
                suggestions[line.Id] = (id, title, soldById.GetValueOrDefault(id));
            }
        }

        return suggestions;
    }

    private static void ApplyBundleComponentInventoryStatus(InventoryItem inventoryItem, InventoryStatus componentStatus, DateTimeOffset? soldAt)
    {
        if (!inventoryItem.IsBundle)
        {
            return;
        }

        foreach (InventoryBundleComponent componentLink in inventoryItem.BundleComponents)
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
