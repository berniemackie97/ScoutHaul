using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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
using HaulScout.Application.Inventory;

namespace HaulScout.Api.Controllers;

[ApiController]
[Route("api/acquisition-lots")]
[RequireOrganizationPermission(HaulScoutPermissions.ManageFinance)]
public sealed class AcquisitionLotsController(HaulScoutDbContext db, ICurrentUserContext currentUser) : HaulScoutControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AcquisitionLotDto>>> List(CancellationToken cancellationToken)
    {
        List<AcquisitionLot> lots = await QueryLots()
            .OrderByDescending(item => item.PurchasedAt)
            .ThenByDescending(item => item.UpdatedAt)
            .Take(200)
            .ToListAsync(cancellationToken);

        return lots.Select(item => item.ToDto()).ToList();
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AcquisitionLotDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        AcquisitionLot? lot = await QueryLots().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        return lot is null ? NotFound() : lot.ToDto();
    }

    [HttpPost]
    public async Task<ActionResult<AcquisitionLotDto>> Create(UpsertAcquisitionLotRequest request, CancellationToken cancellationToken)
    {
        ActionResult<AcquisitionLotDto> result = await Upsert(existingLotId: null, request, cancellationToken);
        if (result.Result is not null)
        {
            return result.Result;
        }

        return CreatedAtAction(nameof(Get), new { id = result.Value!.Id }, result.Value);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<AcquisitionLotDto>> Update(Guid id, UpsertAcquisitionLotRequest request, CancellationToken cancellationToken)
    {
        return await Upsert(id, request, cancellationToken);
    }

    /// <summary>
    /// Itemize a lot: create draft inventory items directly inside it, then re-allocate landed cost across
    /// all items (existing + new) using the lot's current allocation method. This is the natural sourcing
    /// order — record the lot first, then inventory each item out of it.
    /// </summary>
    [HttpPost("{id:guid}/items")]
    public async Task<ActionResult<AcquisitionLotDto>> AddItems(Guid id, AddLotItemsRequest request, CancellationToken cancellationToken)
    {
        if (request.Items is null || request.Items.Count == 0)
        {
            return Invalid("Provide at least one item to add to the lot.");
        }

        if (request.Items.Any(item => string.IsNullOrWhiteSpace(item.Title)))
        {
            return Invalid("Every new lot item needs a title.");
        }

        AcquisitionLot? lot = await db.AcquisitionLots
            .Include(item => item.Items)
            .SingleOrDefaultAsync(item => item.Id == id && item.OrganizationId == currentUser.OrganizationId, cancellationToken);
        if (lot is null)
        {
            return Missing();
        }

        // Creating the items and re-allocating must be atomic: if allocation fails (e.g. a manual lot no
        // longer balances) the new draft items must not be left orphaned without a lot.
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        List<(InventoryItem Item, NewLotItemDto Input)> created = [];
        foreach (NewLotItemDto input in request.Items)
        {
            InventoryItem item = new()
            {
                OrganizationId = currentUser.OrganizationId,
                Title = input.Title.Trim(),
                Category = TrimToNull(input.Category),
                Brand = TrimToNull(input.Brand),
                Condition = TrimToNull(input.Condition),
                StorageLocation = TrimToNull(input.StorageLocation),
                EstimatedSalePrice = input.EstimatedSalePrice,
                Notes = TrimToNull(input.Notes),
                Status = InventoryStatus.Purchased,
                PurchasedAt = lot.PurchasedAt,
            };
            db.InventoryItems.Add(item);
            created.Add((item, input));
        }

        // Persist so the new items have identities before we re-run the shared allocation machinery.
        await db.SaveChangesAsync(cancellationToken);

        // Preserve each existing item's allocation as its manual input so a Manual-method lot keeps its
        // hand-set costs; Equal/Weighted methods ignore these and re-split across the full set.
        List<AcquisitionLotItemInputDto> mergedItems = lot.Items
            .Select(existing => new AcquisitionLotItemInputDto(
                existing.InventoryItemId,
                ManualAllocatedCost: existing.AllocatedCost,
                AllocationWeight: existing.AllocationWeight,
                Notes: existing.Notes))
            .Concat(created.Select(entry => new AcquisitionLotItemInputDto(
                entry.Item.Id,
                ManualAllocatedCost: entry.Input.ManualAllocatedCost,
                AllocationWeight: entry.Input.AllocationWeight,
                Notes: TrimToNull(entry.Input.Notes))))
            .ToList();

        UpsertAcquisitionLotRequest mergedRequest = new(
            lot.Name,
            lot.PurchasedAt,
            lot.PurchaseSubtotal,
            lot.ShippingCost,
            lot.TaxCost,
            lot.FeeCost,
            lot.OtherCost,
            lot.AllocationMethod,
            lot.SupplierName,
            lot.SourceLocationId,
            lot.SourcingTripId,
            lot.SourcingTripStopId,
            lot.ExternalReference,
            lot.Notes,
            mergedItems);

        ActionResult<AcquisitionLotDto> result = await Upsert(id, mergedRequest, cancellationToken);
        if (result.Result is not null)
        {
            // Upsert produced an error response (BadRequest/NotFound/Conflict); roll back the new items.
            return result;
        }

        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private IQueryable<AcquisitionLot> QueryLots() =>
        db.AcquisitionLots
            .Include(item => item.Items)
                .ThenInclude(item => item.InventoryItem)
            .Where(item => item.OrganizationId == currentUser.OrganizationId);

    private async Task<ActionResult<AcquisitionLotDto>> Upsert(
        Guid? existingLotId,
        UpsertAcquisitionLotRequest request,
        CancellationToken cancellationToken)
    {
        string? validation = Validate(request);
        if (validation is not null)
        {
            return Invalid(validation);
        }

        if (!await ReferencesBelongToOrganization(request, cancellationToken))
        {
            return Invalid("One or more acquisition references do not belong to this organization.");
        }

        Guid[] inventoryItemIds = request.Items.Select(item => item.InventoryItemId).Distinct().ToArray();
        List<InventoryItem> items = await db.InventoryItems
            .Where(item => inventoryItemIds.Contains(item.Id) && item.OrganizationId == currentUser.OrganizationId)
            .ToListAsync(cancellationToken);
        if (items.Count != inventoryItemIds.Length)
        {
            return Missing("One or more inventory items were not found.");
        }

        List<AcquisitionLotItem> existingAssignments = await db.AcquisitionLotItems
            .Where(item => inventoryItemIds.Contains(item.InventoryItemId)
                && item.OrganizationId == currentUser.OrganizationId
                && (!existingLotId.HasValue || item.AcquisitionLotId != existingLotId.Value))
            .Include(item => item.AcquisitionLot)
            .ToListAsync(cancellationToken);
        if (existingAssignments.Count > 0)
        {
            return Blocked($"These items already belong to an acquisition lot: {string.Join(", ", existingAssignments.Select(item => item.InventoryItem?.Title ?? item.InventoryItemId.ToString()))}.");
        }

        AcquisitionLot lot;
        Guid[] oldItemIds = [];
        if (existingLotId.HasValue)
        {
            AcquisitionLot? existingLot = await db.AcquisitionLots
                .Include(item => item.Items)
                .SingleOrDefaultAsync(item => item.Id == existingLotId.Value && item.OrganizationId == currentUser.OrganizationId, cancellationToken);
            if (existingLot is null)
            {
                return Missing();
            }

            lot = existingLot;
            oldItemIds = lot.Items.Select(item => item.InventoryItemId).ToArray();
            db.AcquisitionLotItems.RemoveRange(lot.Items);
        }
        else
        {
            lot = new AcquisitionLot { OrganizationId = currentUser.OrganizationId, Name = request.Name.Trim() };
            db.AcquisitionLots.Add(lot);
        }

        Apply(request, lot);

        Dictionary<Guid, decimal> allocations = Allocate(request, items);
        Dictionary<Guid, AcquisitionLotItemInputDto> inputs = request.Items
            .GroupBy(item => item.InventoryItemId)
            .ToDictionary(group => group.Key, group => group.First());

        foreach (InventoryItem item in items)
        {
            decimal allocatedCost = allocations[item.Id];
            item.PurchasePrice = allocatedCost;
            item.PurchasedAt ??= lot.PurchasedAt;
            if (item.Status is InventoryStatus.Draft or InventoryStatus.Researching or InventoryStatus.Watchlist)
            {
                item.Status = InventoryStatus.Purchased;
            }

            db.AcquisitionLotItems.Add(new AcquisitionLotItem
            {
                OrganizationId = currentUser.OrganizationId,
                AcquisitionLot = lot,
                InventoryItemId = item.Id,
                AllocatedCost = allocatedCost,
                AllocationWeight = inputs[item.Id].AllocationWeight,
                Notes = TrimToNull(inputs[item.Id].Notes),
            });
        }

        Guid[] affectedItemIds = inventoryItemIds.Concat(oldItemIds).Distinct().ToArray();
        await SyncInventoryPurchaseExpense(lot, affectedItemIds, cancellationToken);
        await SyncHistoricalSaleCostBasis(items, cancellationToken);

        db.AuditLogs.Add(new AuditLog
        {
            OrganizationId = currentUser.OrganizationId,
            ActorUserId = currentUser.UserId,
            EntityName = nameof(AcquisitionLot),
            EntityId = lot.Id,
            Action = existingLotId.HasValue ? "AcquisitionLotUpdated" : "AcquisitionLotCreated",
            DetailsJson = JsonSerializer.Serialize(new
            {
                lot.Name,
                lot.PurchasedAt,
                lot.PurchaseSubtotal,
                lot.ShippingCost,
                lot.TaxCost,
                lot.FeeCost,
                lot.OtherCost,
                TotalLandedCost = lot.TotalLandedCost,
                InventoryItemIds = inventoryItemIds,
            })
        });

        await db.SaveChangesAsync(cancellationToken);
        AcquisitionLot saved = await QueryLots().SingleAsync(item => item.Id == lot.Id, cancellationToken);
        return saved.ToDto();
    }

    private async Task SyncInventoryPurchaseExpense(
        AcquisitionLot lot,
        IReadOnlyCollection<Guid> affectedInventoryItemIds,
        CancellationToken cancellationToken)
    {
        List<Expense> individualExpenses = await db.Expenses
            .Where(expense => expense.OrganizationId == currentUser.OrganizationId
                && expense.Category == ExpenseCategory.InventoryPurchase
                && expense.InventoryItemId.HasValue
                && affectedInventoryItemIds.Contains(expense.InventoryItemId.Value)
                && expense.AcquisitionLotId == null)
            .ToListAsync(cancellationToken);

        Expense? lotExpense = await db.Expenses.SingleOrDefaultAsync(
            expense => expense.OrganizationId == currentUser.OrganizationId
                && expense.Category == ExpenseCategory.InventoryPurchase
                && expense.AcquisitionLotId == lot.Id,
            cancellationToken);

        decimal previousTrackedAmount = individualExpenses.Sum(expense => expense.Amount) + (lotExpense?.Amount ?? 0);
        decimal nextTrackedAmount = lot.TotalLandedCost;
        db.Expenses.RemoveRange(individualExpenses);

        if (lotExpense is null)
        {
            db.Expenses.Add(new Expense
            {
                OrganizationId = currentUser.OrganizationId,
                AcquisitionLot = lot,
                SourcingTripId = lot.SourcingTripId,
                SourcingTripStopId = lot.SourcingTripStopId,
                Category = ExpenseCategory.InventoryPurchase,
                Description = $"Acquisition lot: {lot.Name}",
                Amount = nextTrackedAmount,
                OccurredAt = lot.PurchasedAt
            });
        }
        else
        {
            lotExpense.InventoryItemId = null;
            lotExpense.SourcingTripId = lot.SourcingTripId;
            lotExpense.SourcingTripStopId = lot.SourcingTripStopId;
            lotExpense.Description = $"Acquisition lot: {lot.Name}";
            lotExpense.Amount = nextTrackedAmount;
            lotExpense.OccurredAt = lot.PurchasedAt;
        }

        await ApplyBudgetDelta(nextTrackedAmount - previousTrackedAmount, cancellationToken);
    }

    private async Task SyncHistoricalSaleCostBasis(IReadOnlyList<InventoryItem> items, CancellationToken cancellationToken)
    {
        Guid[] itemIds = items.Select(item => item.Id).ToArray();
        Dictionary<Guid, decimal> costs = items.ToDictionary(item => item.Id, item => item.PurchasePrice ?? 0);
        List<Sale> sales = await db.Sales
            .Include(item => item.Refunds)
            .Where(item => item.OrganizationId == currentUser.OrganizationId && itemIds.Contains(item.InventoryItemId))
            .ToListAsync(cancellationToken);

        foreach (Sale sale in sales)
        {
            decimal correctedCost = costs[sale.InventoryItemId];
            if (sale.CostOfGoods == correctedCost)
            {
                continue;
            }

            string before = SnapshotSale(sale);
            sale.CostOfGoods = correctedCost;
            sale.RecomputeNet();
            await AddCorrection(sale, before, "Acquisition lot allocation updated cost basis.", cancellationToken);
        }
    }

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

    private async Task ApplyBudgetDelta(decimal delta, CancellationToken cancellationToken)
    {
        if (delta == 0)
        {
            return;
        }

        Budget? budget = await db.Budgets
            .Where(item => item.OrganizationId == currentUser.OrganizationId && item.IsActive)
            .OrderByDescending(item => item.StartsAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (budget is not null)
        {
            budget.CurrentAmount -= delta;
        }
    }

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

    private static Dictionary<Guid, decimal> Allocate(UpsertAcquisitionLotRequest request, IReadOnlyList<InventoryItem> items)
    {
        // A costs-only lot with nothing itemized yet allocates to nothing; the full landed cost stays unallocated.
        if (items.Count == 0)
        {
            return new Dictionary<Guid, decimal>();
        }

        decimal total = RoundMoney(TotalLandedCost(request));
        Dictionary<Guid, AcquisitionLotItemInputDto> inputs = request.Items
            .GroupBy(item => item.InventoryItemId)
            .ToDictionary(group => group.Key, group => group.First());

        if (request.AllocationMethod == AcquisitionLotAllocationMethod.Manual)
        {
            Dictionary<Guid, decimal> manual = inputs.ToDictionary(
                pair => pair.Key,
                pair => RoundMoney(pair.Value.ManualAllocatedCost ?? 0));
            decimal manualTotal = manual.Sum(pair => pair.Value);
            if (Math.Abs(manualTotal - total) > 0.01m)
            {
                throw new InvalidOperationException("Manual allocations must equal the lot's landed cost.");
            }

            return manual;
        }

        decimal[] weights = items.Select(item =>
        {
            if (request.AllocationMethod == AcquisitionLotAllocationMethod.WeightedByExpectedValue)
            {
                decimal? explicitWeight = inputs[item.Id].AllocationWeight;
                if (explicitWeight is > 0) return explicitWeight.Value;
                return item.EstimatedSalePrice ?? item.SellerAskingPrice ?? item.PurchasePrice ?? 0;
            }

            return 1m;
        }).ToArray();

        if (weights.Sum() <= 0)
        {
            weights = items.Select(_ => 1m).ToArray();
        }

        decimal weightTotal = weights.Sum();
        Dictionary<Guid, decimal> result = new();
        decimal allocated = 0;
        for (int index = 0; index < items.Count; index++)
        {
            decimal amount = index == items.Count - 1
                ? total - allocated
                : RoundMoney(total * weights[index] / weightTotal);
            result[items[index].Id] = amount;
            allocated += amount;
        }

        return result;
    }

    private async Task<bool> ReferencesBelongToOrganization(UpsertAcquisitionLotRequest request, CancellationToken cancellationToken)
    {
        if (request.SourceLocationId.HasValue)
        {
            bool locationExists = await db.SourceLocations.AnyAsync(
                item => item.Id == request.SourceLocationId.Value && item.OrganizationId == currentUser.OrganizationId,
                cancellationToken);
            if (!locationExists) return false;
        }

        if (request.SourcingTripStopId.HasValue)
        {
            var stop = await db.SourcingTripStops
                .Where(item => item.Id == request.SourcingTripStopId.Value && item.OrganizationId == currentUser.OrganizationId)
                .Select(item => new { item.SourcingTripId })
                .SingleOrDefaultAsync(cancellationToken);
            if (stop is null) return false;
            if (request.SourcingTripId.HasValue && stop.SourcingTripId != request.SourcingTripId.Value) return false;
        }

        if (request.SourcingTripId.HasValue)
        {
            bool tripExists = await db.SourcingTrips.AnyAsync(
                item => item.Id == request.SourcingTripId.Value && item.OrganizationId == currentUser.OrganizationId,
                cancellationToken);
            if (!tripExists) return false;
        }

        return true;
    }

    private static void Apply(UpsertAcquisitionLotRequest request, AcquisitionLot lot)
    {
        lot.Name = request.Name.Trim();
        lot.SupplierName = TrimToNull(request.SupplierName);
        lot.SourceLocationId = request.SourceLocationId;
        lot.SourcingTripId = request.SourcingTripId;
        lot.SourcingTripStopId = request.SourcingTripStopId;
        lot.PurchasedAt = request.PurchasedAt ?? DateTimeOffset.UtcNow;
        lot.PurchaseSubtotal = RoundMoney(request.PurchaseSubtotal);
        lot.ShippingCost = RoundMoney(request.ShippingCost);
        lot.TaxCost = RoundMoney(request.TaxCost);
        lot.FeeCost = RoundMoney(request.FeeCost);
        lot.OtherCost = RoundMoney(request.OtherCost);
        lot.AllocationMethod = request.AllocationMethod;
        lot.ExternalReference = TrimToNull(request.ExternalReference);
        lot.Notes = TrimToNull(request.Notes);
    }

    private static string? Validate(UpsertAcquisitionLotRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return "Acquisition lot name is required.";
        }

        // Items may be empty: record a costs-only lot now, itemize it later via POST {id}/items.
        if (request.PurchasedAt?.UtcDateTime.Date > DateTime.UtcNow.Date)
        {
            return "Purchase date cannot be in the future.";
        }

        if (request.PurchaseSubtotal < 0 || request.ShippingCost < 0 || request.TaxCost < 0 || request.FeeCost < 0 || request.OtherCost < 0)
        {
            return "Purchase subtotal and landed costs cannot be negative.";
        }

        if (TotalLandedCost(request) <= 0)
        {
            return "Total landed cost must be greater than zero.";
        }

        IReadOnlyList<AcquisitionLotItemInputDto> items = request.Items ?? [];

        // Manual allocation only has to balance once the lot has been itemized.
        if (items.Count > 0 && request.AllocationMethod == AcquisitionLotAllocationMethod.Manual)
        {
            if (items.Any(item => item.ManualAllocatedCost is null or < 0))
            {
                return "Manual allocation requires a non-negative allocated cost for every item.";
            }

            decimal manualTotal = items.Sum(item => item.ManualAllocatedCost ?? 0);
            if (Math.Abs(RoundMoney(manualTotal) - RoundMoney(TotalLandedCost(request))) > 0.01m)
            {
                return "Manual allocations must equal the lot's landed cost.";
            }
        }

        if (items.Any(item => item.AllocationWeight is < 0))
        {
            return "Allocation weights cannot be negative.";
        }

        return null;
    }

    private static decimal TotalLandedCost(UpsertAcquisitionLotRequest request) =>
        request.PurchaseSubtotal + request.ShippingCost + request.TaxCost + request.FeeCost + request.OtherCost;

    private static decimal RoundMoney(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
