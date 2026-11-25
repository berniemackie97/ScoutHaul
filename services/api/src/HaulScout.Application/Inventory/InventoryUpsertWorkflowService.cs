using System.Text.Json;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using static HaulScout.Application.Common.Text;

namespace HaulScout.Application.Inventory;

/// <summary>
/// Creating and editing an item, and the money that follows from it. An item's purchase price is not just a field: it
/// is mirrored by an expense, drawn against the active budget, and — if the item has already sold — it is the cost
/// basis of those sales. Editing it in isolation would leave the books disagreeing with the item.
/// </summary>
public sealed class InventoryUpsertWorkflowService(
    IInventoryStore inventory,
    ICurrentUserContext currentUser,
    InventoryReferenceDataService referenceData,
    TimeProvider timeProvider)
{
    /// <summary>The stages an item may be created in — everything before it is committed to a channel or a buyer.</summary>
    private static readonly InventoryStatus[] CreatableStatuses =
    [
        InventoryStatus.Draft,
        InventoryStatus.Researching,
        InventoryStatus.Watchlist,
        InventoryStatus.Purchased,
        InventoryStatus.NeedsCleaning,
        InventoryStatus.NeedsTesting,
        InventoryStatus.NeedsRepair,
        InventoryStatus.NeedsPhotos,
        InventoryStatus.ReadyToList,
    ];

    public async Task<AppResult<InventoryItemDto>> CreateAsync(
        UpsertInventoryItemRequest request,
        CancellationToken cancellationToken)
    {
        if (Validate(request) is string validation)
        {
            return AppResult<InventoryItemDto>.Validation(validation);
        }

        if (!await inventory.SourcingReferencesValidAsync(
                currentUser.OrganizationId, request.SourcingTripId, request.SourcingTripStopId, cancellationToken))
        {
            return AppResult<InventoryItemDto>.Validation("One or more inventory references do not belong to this organization.");
        }

        (InventoryReferenceDataService.ControlledValues? controlledValues, string? controlledError) =
            await referenceData.ResolveAsync(request, cancellationToken);
        if (controlledError is not null)
        {
            return AppResult<InventoryItemDto>.Validation(controlledError);
        }

        InventoryStatus startingStatus = request.Status ?? (request.PurchasePrice.HasValue ? InventoryStatus.Purchased : InventoryStatus.Draft);
        if (!CreatableStatuses.Contains(startingStatus))
        {
            return AppResult<InventoryItemDto>.Validation("A new inventory item must start in a pre-sale intake, prep, or ready-to-list stage.");
        }

        InventoryItem item = new()
        {
            OrganizationId = currentUser.OrganizationId,
            Title = request.Title.Trim(),
            Status = startingStatus,
            PurchasedAt = startingStatus == InventoryStatus.Purchased || request.PurchasePrice.HasValue
                ? timeProvider.GetUtcNow()
                : null,
        };

        Apply(request, controlledValues!, item);
        await inventory.AddAsync(item, cancellationToken);
        await SyncPurchaseExpenseAsync(item, previousPurchasePrice: null, cancellationToken);
        await inventory.SaveChangesAsync(cancellationToken);

        return AppResult<InventoryItemDto>.Ok(item.ToDto());
    }

    public async Task<AppResult<InventoryItemDto>> UpdateAsync(
        Guid id,
        UpsertInventoryItemRequest request,
        CancellationToken cancellationToken)
    {
        InventoryItem? item = await inventory.FindDetailAsync(currentUser.OrganizationId, id, cancellationToken);
        if (item is null)
        {
            return AppResult<InventoryItemDto>.NotFound();
        }

        if (Validate(request) is string validation)
        {
            return AppResult<InventoryItemDto>.Validation(validation);
        }

        if (!await inventory.SourcingReferencesValidAsync(
                currentUser.OrganizationId, request.SourcingTripId, request.SourcingTripStopId, cancellationToken))
        {
            return AppResult<InventoryItemDto>.Validation("One or more inventory references do not belong to this organization.");
        }

        (InventoryReferenceDataService.ControlledValues? controlledValues, string? controlledError) =
            await referenceData.ResolveAsync(request, cancellationToken);
        if (controlledError is not null)
        {
            return AppResult<InventoryItemDto>.Validation(controlledError);
        }

        decimal? previousPurchasePrice = item.PurchasePrice;
        Apply(request, controlledValues!, item);
        await SyncPurchaseExpenseAsync(item, previousPurchasePrice, cancellationToken);
        // Correcting cost on an already-sold item flows through to sale COGS and true net, audited.
        await SyncSaleCostBasisAsync(item, previousPurchasePrice, cancellationToken);
        await inventory.SaveChangesAsync(cancellationToken);

        return AppResult<InventoryItemDto>.Ok(item.ToDto());
    }

    /// <summary>
    /// A sold item whose acquisition cost is corrected must have that flow through to what the sale actually earned,
    /// with a correction row recording the before/after. Net is re-derived by the sale itself, from its refunds too.
    /// </summary>
    private async Task SyncSaleCostBasisAsync(
        InventoryItem item,
        decimal? previousPurchasePrice,
        CancellationToken cancellationToken)
    {
        decimal newCost = item.PurchasePrice ?? 0;
        if ((previousPurchasePrice ?? 0) == newCost)
        {
            return;
        }

        IReadOnlyList<Sale> sales = await inventory.ListSalesWithRefundsAsync(
            currentUser.OrganizationId, item.Id, cancellationToken);

        foreach (Sale sale in sales)
        {
            if (sale.CostOfGoods == newCost)
            {
                continue;
            }

            string before = SnapshotSale(sale);
            sale.CostOfGoods = newCost;
            sale.RecomputeNet();

            await inventory.AddSaleCorrectionAsync(
                new SaleCorrection
                {
                    OrganizationId = currentUser.OrganizationId,
                    SaleId = sale.Id,
                    CorrectedByUserId = currentUser.UserId,
                    Reason = "Acquisition cost corrected on the item.",
                    BeforeJson = before,
                    AfterJson = SnapshotSale(sale),
                    Version = await inventory.NextSaleCorrectionVersionAsync(currentUser.OrganizationId, sale.Id, cancellationToken),
                },
                cancellationToken);
        }
    }

    /// <summary>
    /// NOTE: this is one of six identical copies of the sale snapshot in the codebase — the same duplication class as
    /// the net-profit bug, where five callers had independently forgotten refunds. It is reproduced verbatim here so
    /// this move changes no behaviour; it is deduplicated when the sales module moves and SaleFinancialStateService
    /// becomes the single owner.
    /// </summary>
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

    /// <summary>
    /// Keeps the purchase expense and the budget in step with the item's price. The expense mirrors the item, so it is
    /// created, updated or withdrawn as the price appears, changes or goes away; the budget moves by the difference,
    /// never by the whole amount, so repeated edits cannot drain it.
    /// </summary>
    private async Task SyncPurchaseExpenseAsync(
        InventoryItem item,
        decimal? previousPurchasePrice,
        CancellationToken cancellationToken)
    {
        decimal? currentPurchasePrice = item.PurchasePrice;
        if (previousPurchasePrice == currentPurchasePrice)
        {
            return;
        }

        Expense? existingExpense = await inventory.FindPurchaseExpenseAsync(
            currentUser.OrganizationId, item.Id, cancellationToken);

        decimal previousAmount = existingExpense?.Amount ?? previousPurchasePrice ?? 0;
        decimal nextAmount = currentPurchasePrice ?? 0;
        decimal budgetDelta = nextAmount - previousAmount;

        if (currentPurchasePrice is null or <= 0)
        {
            if (existingExpense is not null)
            {
                inventory.RemoveExpense(existingExpense);
            }

            await ApplyBudgetDeltaAsync(budgetDelta, cancellationToken);
            return;
        }

        if (existingExpense is null)
        {
            await inventory.AddExpenseAsync(
                new Expense
                {
                    OrganizationId = currentUser.OrganizationId,
                    InventoryItemId = item.Id,
                    SourcingTripId = item.SourcingTripId,
                    SourcingTripStopId = item.SourcingTripStopId,
                    Category = ExpenseCategory.InventoryPurchase,
                    Description = $"Inventory purchase: {item.Title}",
                    Amount = currentPurchasePrice.Value,
                    OccurredAt = item.PurchasedAt ?? timeProvider.GetUtcNow(),
                },
                cancellationToken);
        }
        else
        {
            existingExpense.SourcingTripId = item.SourcingTripId;
            existingExpense.SourcingTripStopId = item.SourcingTripStopId;
            existingExpense.Description = $"Inventory purchase: {item.Title}";
            existingExpense.Amount = currentPurchasePrice.Value;
            existingExpense.OccurredAt = item.PurchasedAt ?? existingExpense.OccurredAt;
        }

        item.PurchasedAt ??= timeProvider.GetUtcNow();
        await ApplyBudgetDeltaAsync(budgetDelta, cancellationToken);
    }

    private async Task ApplyBudgetDeltaAsync(decimal delta, CancellationToken cancellationToken)
    {
        if (delta == 0)
        {
            return;
        }

        Budget? budget = await inventory.FindActiveBudgetAsync(currentUser.OrganizationId, cancellationToken);
        if (budget is not null)
        {
            budget.CurrentAmount -= delta;
        }
    }

    private static string? Validate(UpsertInventoryItemRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return "Item title is required.";
        }

        if (request.SellerAskingPrice is < 0 || request.PurchasePrice is < 0 || request.EstimatedSalePrice is < 0)
        {
            return "Prices cannot be negative.";
        }

        if (request.ManufactureYear is < 1800 || request.ManufactureYear > 2200)
        {
            return "Manufacture year must be between 1800 and 2200.";
        }

        if (request.Length is < 0 || request.Width is < 0 || request.Height is < 0 || request.Weight is < 0)
        {
            return "Dimensions and weight cannot be negative.";
        }

        return null;
    }

    private static void Apply(
        UpsertInventoryItemRequest request,
        InventoryReferenceDataService.ControlledValues controlled,
        InventoryItem item)
    {
        item.ProductIdentityId = request.ProductIdentityId;
        item.SourcingTripId = request.SourcingTripId;
        item.SourcingTripStopId = request.SourcingTripStopId;
        item.SourceLocationId = request.SourceLocationId;
        item.Title = request.Title.Trim();
        item.Description = TrimToNull(request.Description);
        item.Category = controlled.Category;
        item.Brand = controlled.Brand;
        item.Model = controlled.Model;
        if (controlled.Colors is not null) item.Colors = [.. controlled.Colors];
        item.Condition = controlled.Condition;
        item.OwnershipType = request.OwnershipType ?? item.OwnershipType;
        item.FunctionalStatus = request.FunctionalStatus ?? item.FunctionalStatus;
        item.CompletenessStatus = request.CompletenessStatus ?? item.CompletenessStatus;
        item.AuthenticityStatus = request.AuthenticityStatus ?? item.AuthenticityStatus;
        item.PackagingStatus = request.PackagingStatus ?? item.PackagingStatus;
        item.SizeSystem = controlled.SizeSystem;
        item.SizeValue = controlled.SizeValue;
        if (request.ManufacturerPartNumber is not null) item.ManufacturerPartNumber = TrimToNull(request.ManufacturerPartNumber);
        if (request.SerialNumber is not null) item.SerialNumber = TrimToNull(request.SerialNumber);
        if (request.CountryOfOrigin is not null) item.CountryOfOrigin = TrimToNull(request.CountryOfOrigin)?.ToUpperInvariant();
        if (request.ManufactureYear.HasValue) item.ManufactureYear = request.ManufactureYear;
        if (request.Length.HasValue) item.Length = request.Length;
        if (request.Width.HasValue) item.Width = request.Width;
        if (request.Height.HasValue) item.Height = request.Height;
        item.DimensionUnit = request.DimensionUnit ?? item.DimensionUnit;
        if (request.Weight.HasValue) item.Weight = request.Weight;
        item.WeightUnit = request.WeightUnit ?? item.WeightUnit;
        item.AttributesJson = NormalizeJsonObject(request.AttributesJson);
        // Left out (older mobile builds send null) keeps what is stored; an empty object clears it.
        if (request.MeasurementsJson is not null) item.MeasurementsJson = NormalizeJsonObject(request.MeasurementsJson);
        item.StorageLocation = controlled.StorageLocation;
        item.Accessories = TrimToNull(request.Accessories);
        item.MissingParts = TrimToNull(request.MissingParts);
        item.TestingNotes = TrimToNull(request.TestingNotes);
        item.CleaningNotes = TrimToNull(request.CleaningNotes);
        item.ListingNotes = TrimToNull(request.ListingNotes);
        item.SellerAskingPrice = request.SellerAskingPrice;
        item.PurchasePrice = request.PurchasePrice;
        item.EstimatedSalePrice = request.EstimatedSalePrice;
        item.Barcode = TrimToNull(request.Barcode);
        item.Notes = TrimToNull(request.Notes);
        ItemProfiles.ClearInapplicable(item);
    }

    /// <summary>Anything that is not a JSON object — including malformed text — is stored as nothing rather than as
    /// a value that would later fail to parse.</summary>
    private static string? NormalizeJsonObject(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(value);
            return document.RootElement.ValueKind == JsonValueKind.Object
                ? JsonSerializer.Serialize(document.RootElement)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
