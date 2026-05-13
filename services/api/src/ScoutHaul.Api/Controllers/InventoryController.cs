using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScoutHaul.Api.Authorization;
using ScoutHaul.Api.Mapping;
using ScoutHaul.Api.Services;
using ScoutHaul.Application.Contracts;
using ScoutHaul.Domain;
using ScoutHaul.Domain.Entities;
using ScoutHaul.Infrastructure.Persistence;
using System.Text.Json;

namespace ScoutHaul.Api.Controllers;

[ApiController]
[Route("api/inventory")]
[RequireOrganizationPermission(ScoutHaulPermissions.ManageInventory)]
public sealed class InventoryController(
    ScoutHaulDbContext db,
    ICurrentUserContext currentUser,
    InventoryPhotoStorageService photoStorage) : ControllerBase
{
    public sealed class UploadInventoryPhotoForm
    {
        public IFormFile? File { get; set; }
        public bool IsHero { get; set; }
    }

    [HttpPost]
    public async Task<ActionResult<InventoryItemDto>> Create(UpsertInventoryItemRequest request, CancellationToken cancellationToken)
    {
        var validation = Validate(request);
        if (validation is not null)
        {
            return BadRequest(validation);
        }

        if (!await ReferencesBelongToOrganization(request, cancellationToken))
        {
            return BadRequest("One or more inventory references do not belong to this organization.");
        }

        var item = new InventoryItem
        {
            OrganizationId = currentUser.OrganizationId,
            Title = request.Title.Trim(),
            Status = request.PurchasePrice.HasValue ? InventoryStatus.Purchased : InventoryStatus.Draft,
            PurchasedAt = request.PurchasePrice.HasValue ? DateTimeOffset.UtcNow : null
        };

        Apply(request, item);
        db.InventoryItems.Add(item);
        await SyncInventoryPurchaseExpense(item, previousPurchasePrice: null, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = item.Id }, item.ToDto());
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<InventoryItemDto>> Update(Guid id, UpsertInventoryItemRequest request, CancellationToken cancellationToken)
    {
        var item = await FindItem(id, cancellationToken);
        if (item is null)
        {
            return NotFound();
        }

        var validation = Validate(request);
        if (validation is not null)
        {
            return BadRequest(validation);
        }

        if (!await ReferencesBelongToOrganization(request, cancellationToken))
        {
            return BadRequest("One or more inventory references do not belong to this organization.");
        }

        decimal? previousPurchasePrice = item.PurchasePrice;
        Apply(request, item);
        await SyncInventoryPurchaseExpense(item, previousPurchasePrice, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return item.ToDto();
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<InventoryItemDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var item = await FindItem(id, cancellationToken);
        return item is null ? NotFound() : item.ToDto();
    }

    [HttpPost("{id:guid}/photos")]
    [RequestSizeLimit(10_000_000)]
    public async Task<ActionResult<InventoryPhotoUploadResultDto>> UploadPhoto(
        Guid id,
        [FromForm] UploadInventoryPhotoForm request,
        CancellationToken cancellationToken)
    {
        var item = await FindItem(id, cancellationToken);
        if (item is null)
        {
            return NotFound();
        }

        if (request.File is null)
        {
            return BadRequest("Photo file is required.");
        }

        string uploadedUrl;
        try
        {
            uploadedUrl = await photoStorage.SaveAsync(currentUser.OrganizationId, item.Id, request.File, cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(exception.Message);
        }

        List<string> photoUrls = ParsePhotoUrls(item.PhotoLocalReferencesJson);
        photoUrls.RemoveAll(url => string.Equals(url, uploadedUrl, StringComparison.OrdinalIgnoreCase));
        if (request.IsHero || photoUrls.Count == 0)
        {
            photoUrls.Insert(0, uploadedUrl);
        }
        else
        {
            photoUrls.Add(uploadedUrl);
        }

        item.PhotoLocalReferencesJson = JsonSerializer.Serialize(photoUrls);
        await db.SaveChangesAsync(cancellationToken);

        return new InventoryPhotoUploadResultDto(item.Id, photoUrls, uploadedUrl);
    }

    [HttpDelete("{id:guid}/photos")]
    public async Task<ActionResult<InventoryPhotoUploadResultDto>> DeletePhoto(
        Guid id,
        [FromQuery] string url,
        CancellationToken cancellationToken)
    {
        var item = await FindItem(id, cancellationToken);
        if (item is null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(url))
        {
            return BadRequest("Photo URL is required.");
        }

        List<string> photoUrls = ParsePhotoUrls(item.PhotoLocalReferencesJson);
        bool removed = photoUrls.RemoveAll(existing => string.Equals(existing, url, StringComparison.OrdinalIgnoreCase)) > 0;
        if (!removed)
        {
            return NotFound("Photo was not found on this inventory item.");
        }

        item.PhotoLocalReferencesJson = photoUrls.Count == 0 ? null : JsonSerializer.Serialize(photoUrls);
        await photoStorage.DeleteAsync(currentUser.OrganizationId, item.Id, url);
        await db.SaveChangesAsync(cancellationToken);

        return new InventoryPhotoUploadResultDto(item.Id, photoUrls, UploadedUrl: null);
    }

    [HttpPatch("{id:guid}/photos/hero")]
    public async Task<ActionResult<InventoryPhotoUploadResultDto>> SetHeroPhoto(
        Guid id,
        [FromQuery] string url,
        CancellationToken cancellationToken)
    {
        var item = await FindItem(id, cancellationToken);
        if (item is null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(url))
        {
            return BadRequest("Photo URL is required.");
        }

        List<string> photoUrls = ParsePhotoUrls(item.PhotoLocalReferencesJson);
        int index = photoUrls.FindIndex(existing => string.Equals(existing, url, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return NotFound("Photo was not found on this inventory item.");
        }

        photoUrls.RemoveAt(index);
        photoUrls.Insert(0, url);
        item.PhotoLocalReferencesJson = JsonSerializer.Serialize(photoUrls);
        await db.SaveChangesAsync(cancellationToken);

        return new InventoryPhotoUploadResultDto(item.Id, photoUrls, UploadedUrl: null);
    }


    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<InventoryItemDto>>> List(
        [FromQuery] InventoryStatus? status,
        [FromQuery] bool includeArchived,
        CancellationToken cancellationToken)
    {
        var query = db.InventoryItems.Where(item => item.OrganizationId == currentUser.OrganizationId);

        if (!includeArchived)
        {
            query = query.Where(item => !item.IsArchived);
        }

        if (status.HasValue)
        {
            query = query.Where(item => item.Status == status.Value);
        }

        var items = await query
            .OrderByDescending(item => item.UpdatedAt)
            .Take(200)
            .Select(item => item.ToDto())
            .ToListAsync(cancellationToken);

        return items;
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<InventoryItemDto>> ChangeStatus(Guid id, ChangeInventoryStatusRequest request, CancellationToken cancellationToken)
    {
        var item = await FindItem(id, cancellationToken);
        if (item is null)
        {
            return NotFound();
        }

        item.Status = request.Status;
        var now = DateTimeOffset.UtcNow;
        item.PurchasedAt ??= request.Status == InventoryStatus.Purchased ? now : null;
        item.ListedAt ??= request.Status == InventoryStatus.Listed ? now : null;
        item.SoldAt ??= request.Status == InventoryStatus.Sold ? now : null;

        await db.SaveChangesAsync(cancellationToken);
        return item.ToDto();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Archive(Guid id, CancellationToken cancellationToken)
    {
        var item = await FindItem(id, cancellationToken);
        if (item is null)
        {
            return NotFound();
        }

        item.IsArchived = true;
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private Task<InventoryItem?> FindItem(Guid id, CancellationToken cancellationToken) =>
        db.InventoryItems.SingleOrDefaultAsync(
            item => item.Id == id && item.OrganizationId == currentUser.OrganizationId,
            cancellationToken);

    private async Task SyncInventoryPurchaseExpense(
        InventoryItem item,
        decimal? previousPurchasePrice,
        CancellationToken cancellationToken)
    {
        decimal? currentPurchasePrice = item.PurchasePrice;
        if (previousPurchasePrice == currentPurchasePrice)
        {
            return;
        }

        var existingExpense = await db.Expenses.SingleOrDefaultAsync(expense =>
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

            await ApplyBudgetDelta(budgetDelta, cancellationToken);
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
        await ApplyBudgetDelta(budgetDelta, cancellationToken);
    }

    private async Task ApplyBudgetDelta(decimal delta, CancellationToken cancellationToken)
    {
        if (delta == 0)
        {
            return;
        }

        var budget = await db.Budgets
            .Where(item => item.OrganizationId == currentUser.OrganizationId && item.IsActive)
            .OrderByDescending(item => item.StartsAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (budget is not null)
        {
            budget.CurrentAmount -= delta;
        }
    }

    private async Task<bool> ReferencesBelongToOrganization(UpsertInventoryItemRequest request, CancellationToken cancellationToken)
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

        return null;
    }

    private static void Apply(UpsertInventoryItemRequest request, InventoryItem item)
    {
        item.ProductIdentityId = request.ProductIdentityId;
        item.SourcingTripId = request.SourcingTripId;
        item.SourcingTripStopId = request.SourcingTripStopId;
        item.SourceLocationId = request.SourceLocationId;
        item.Title = request.Title.Trim();
        item.Description = TrimToNull(request.Description);
        item.Condition = TrimToNull(request.Condition);
        item.SellerAskingPrice = request.SellerAskingPrice;
        item.PurchasePrice = request.PurchasePrice;
        item.EstimatedSalePrice = request.EstimatedSalePrice;
        item.Barcode = TrimToNull(request.Barcode);
        item.Notes = TrimToNull(request.Notes);
    }

    private static string? TrimToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static List<string> ParsePhotoUrls(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(value) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
