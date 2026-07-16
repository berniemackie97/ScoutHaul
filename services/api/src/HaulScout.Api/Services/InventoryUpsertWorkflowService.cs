using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using HaulScout.Api.Mapping;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Services;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;
using static HaulScout.Api.Helpers.StringHelpers;
using HaulScout.Domain.Services;

namespace HaulScout.Api.Services;

/// <summary>
/// Owns inventory create/update rules and their financial side effects.
/// Controllers keep HTTP shape; this service owns validation, reference checks, purchase expense sync, and COGS corrections.
/// </summary>
public sealed class InventoryUpsertWorkflowService(
    HaulScoutDbContext db,
    ICurrentUserContext currentUser,
    InventoryReferenceDataService referenceData)
{
    public async Task<AppResult<InventoryItemDto>> CreateAsync(
        UpsertInventoryItemRequest request,
        CancellationToken cancellationToken)
    {
        string? validation = Validate(request);
        if (validation is not null)
        {
            return AppResult<InventoryItemDto>.Validation(validation);
        }

        if (!await ReferencesBelongToOrganizationAsync(request, cancellationToken))
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
        if (!CanCreateInStatus(startingStatus))
        {
            return AppResult<InventoryItemDto>.Validation("A new inventory item must start in a pre-sale intake, prep, or ready-to-list stage.");
        }

        InventoryItem item = new()
        {
            OrganizationId = currentUser.OrganizationId,
            Title = request.Title.Trim(),
            Status = startingStatus,
            PurchasedAt = startingStatus == InventoryStatus.Purchased || request.PurchasePrice.HasValue ? DateTimeOffset.UtcNow : null
        };

        Apply(request, controlledValues!, item);
        db.InventoryItems.Add(item);
        await SyncInventoryPurchaseExpenseAsync(item, previousPurchasePrice: null, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return AppResult<InventoryItemDto>.Ok(item.ToDto());
    }

    public async Task<AppResult<InventoryItemDto>> UpdateAsync(
        Guid id,
        UpsertInventoryItemRequest request,
        CancellationToken cancellationToken)
    {
        InventoryItem? item = await FindItemAsync(id, cancellationToken);
        if (item is null)
        {
            return AppResult<InventoryItemDto>.NotFound();
        }

        string? validation = Validate(request);
        if (validation is not null)
        {
            return AppResult<InventoryItemDto>.Validation(validation);
        }

        if (!await ReferencesBelongToOrganizationAsync(request, cancellationToken))
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
        await SyncInventoryPurchaseExpenseAsync(item, previousPurchasePrice, cancellationToken);
        // Correcting cost on an already-sold legacy item flows through to sale COGS and true net, audited.
        await SyncSaleCostBasisAsync(item, previousPurchasePrice, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return AppResult<InventoryItemDto>.Ok(item.ToDto());
    }

    private Task<InventoryItem?> FindItemAsync(Guid id, CancellationToken cancellationToken) =>
        db.InventoryItems
            .Include(item => item.Photos)
            .Include(item => item.Issues)
            .Include(item => item.AcquisitionLotItems)
                .ThenInclude(item => item.AcquisitionLot)
            .Include(item => item.BundleComponents)
                .ThenInclude(item => item.ComponentInventoryItem)
            .SingleOrDefaultAsync(
                item => item.Id == id && item.OrganizationId == currentUser.OrganizationId,
                cancellationToken);

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

        List<Sale> sales = await db.Sales
            .Include(sale => sale.Refunds)
            .Where(sale => sale.InventoryItemId == item.Id && sale.OrganizationId == currentUser.OrganizationId)
            .ToListAsync(cancellationToken);
        foreach (Sale sale in sales)
        {
            if (sale.CostOfGoods == newCost)
            {
                continue;
            }

            string before = SnapshotSale(sale);
            sale.CostOfGoods = newCost;
            sale.RecomputeNet();

            int version = await db.SaleCorrections.CountAsync(
                correction => correction.SaleId == sale.Id && correction.OrganizationId == currentUser.OrganizationId,
                cancellationToken) + 1;
            db.SaleCorrections.Add(new SaleCorrection
            {
                OrganizationId = currentUser.OrganizationId,
                SaleId = sale.Id,
                CorrectedByUserId = currentUser.UserId,
                Reason = "Acquisition cost corrected on the item.",
                BeforeJson = before,
                AfterJson = SnapshotSale(sale),
                Version = version,
            });
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

    private async Task SyncInventoryPurchaseExpenseAsync(
        InventoryItem item,
        decimal? previousPurchasePrice,
        CancellationToken cancellationToken)
    {
        decimal? currentPurchasePrice = item.PurchasePrice;
        if (previousPurchasePrice == currentPurchasePrice)
        {
            return;
        }

        Expense? existingExpense = await db.Expenses.SingleOrDefaultAsync(expense =>
            expense.OrganizationId == currentUser.OrganizationId
            && expense.InventoryItemId == item.Id
            && expense.Category == ExpenseCategory.InventoryPurchase,
            cancellationToken);

        decimal previousAmount = existingExpense?.Amount ?? previousPurchasePrice ?? 0;
        decimal nextAmount = currentPurchasePrice ?? 0;
        decimal budgetDelta = nextAmount - previousAmount;

        if (currentPurchasePrice is null or <= 0)
        {
            if (existingExpense is not null)
            {
                db.Expenses.Remove(existingExpense);
            }

            await ApplyBudgetDeltaAsync(budgetDelta, cancellationToken);
            return;
        }

        if (existingExpense is null)
        {
            db.Expenses.Add(new Expense
            {
                OrganizationId = currentUser.OrganizationId,
                InventoryItemId = item.Id,
                SourcingTripId = item.SourcingTripId,
                SourcingTripStopId = item.SourcingTripStopId,
                Category = ExpenseCategory.InventoryPurchase,
                Description = $"Inventory purchase: {item.Title}",
                Amount = currentPurchasePrice.Value,
                OccurredAt = item.PurchasedAt ?? DateTimeOffset.UtcNow
            });
        }
        else
        {
            existingExpense.SourcingTripId = item.SourcingTripId;
            existingExpense.SourcingTripStopId = item.SourcingTripStopId;
            existingExpense.Description = $"Inventory purchase: {item.Title}";
            existingExpense.Amount = currentPurchasePrice.Value;
            existingExpense.OccurredAt = item.PurchasedAt ?? existingExpense.OccurredAt;
        }

        item.PurchasedAt ??= DateTimeOffset.UtcNow;
        await ApplyBudgetDeltaAsync(budgetDelta, cancellationToken);
    }

    private async Task ApplyBudgetDeltaAsync(decimal delta, CancellationToken cancellationToken)
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

    private async Task<bool> ReferencesBelongToOrganizationAsync(
        UpsertInventoryItemRequest request,
        CancellationToken cancellationToken)
    {
        if (request.SourcingTripStopId.HasValue)
        {
            var stop = await db.SourcingTripStops
                .Where(item => item.Id == request.SourcingTripStopId.Value && item.OrganizationId == currentUser.OrganizationId)
                .Select(item => new { item.SourcingTripId })
                .SingleOrDefaultAsync(cancellationToken);

            if (stop is null)
            {
                return false;
            }

            if (request.SourcingTripId.HasValue && stop.SourcingTripId != request.SourcingTripId.Value)
            {
                return false;
            }
        }

        if (request.SourcingTripId.HasValue)
        {
            bool tripExists = await db.SourcingTrips.AnyAsync(
                item => item.Id == request.SourcingTripId.Value && item.OrganizationId == currentUser.OrganizationId,
                cancellationToken);

            if (!tripExists)
            {
                return false;
            }
        }

        return true;
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

    private static bool CanCreateInStatus(InventoryStatus status) =>
        status is InventoryStatus.Draft
            or InventoryStatus.Researching
            or InventoryStatus.Watchlist
            or InventoryStatus.Purchased
            or InventoryStatus.NeedsCleaning
            or InventoryStatus.NeedsTesting
            or InventoryStatus.NeedsRepair
            or InventoryStatus.NeedsPhotos
            or InventoryStatus.ReadyToList;

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
        item.Color = controlled.Color;
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
        item.MeasurementsJson = NormalizeJsonObject(request.MeasurementsJson);
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
    }

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
