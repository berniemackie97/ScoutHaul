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

        InventoryPhotoStorageService.ProcessedInventoryPhoto processed;
        try
        {
            processed = await photoStorage.ProcessAsync(request.File, cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(exception.Message);
        }

        if (request.IsHero || item.Photos.Count == 0)
        {
            foreach (var existingPhoto in item.Photos)
            {
                existingPhoto.IsHero = false;
            }
        }

        var photo = new InventoryPhoto
        {
            OrganizationId = currentUser.OrganizationId,
            InventoryItemId = item.Id,
            IsHero = request.IsHero || item.Photos.Count == 0,
            SortOrder = item.Photos.Count == 0 ? 0 : item.Photos.Max(existing => existing.SortOrder) + 1,
            OriginalFileName = processed.OriginalFileName,
            ContentType = processed.ContentType,
            OriginalBytes = processed.OriginalBytes,
            ThumbnailBytes = processed.ThumbnailBytes,
            OriginalByteSize = processed.OriginalByteSize,
            ThumbnailByteSize = processed.ThumbnailByteSize,
            Width = processed.Width,
            Height = processed.Height
        };

        db.InventoryPhotos.Add(photo);

        List<string> photoUrls = ParsePhotoUrls(item.PhotoLocalReferencesJson);
        string uploadedUrl = PhotoOriginalUrl(item.Id, photo.Id);
        photoUrls.RemoveAll(url => string.Equals(url, uploadedUrl, StringComparison.OrdinalIgnoreCase));
        if (photo.IsHero || photoUrls.Count == 0)
        {
            photoUrls.Insert(0, uploadedUrl);
        }
        else
        {
            photoUrls.Add(uploadedUrl);
        }

        item.PhotoLocalReferencesJson = JsonSerializer.Serialize(photoUrls);
        await db.SaveChangesAsync(cancellationToken);

        await db.Entry(item).Collection(existing => existing.Photos).LoadAsync(cancellationToken);
        return new InventoryPhotoUploadResultDto(item.Id, photoUrls, uploadedUrl, OrderedPhotos(item).Select(item => item.ToDto()).ToArray());
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

        InventoryPhoto? photo = ResolvePhotoByUrl(item, url);
        if (photo is not null)
        {
            db.InventoryPhotos.Remove(photo);
        }

        List<string> photoUrls = ParsePhotoUrls(item.PhotoLocalReferencesJson);
        bool removedLegacyUrl = photoUrls.RemoveAll(existing => string.Equals(existing, url, StringComparison.OrdinalIgnoreCase)) > 0;
        if (photo is not null)
        {
            photoUrls.RemoveAll(existing =>
                string.Equals(existing, PhotoOriginalUrl(item.Id, photo.Id), StringComparison.OrdinalIgnoreCase)
                || string.Equals(existing, PhotoThumbnailUrl(item.Id, photo.Id), StringComparison.OrdinalIgnoreCase));
        }

        if (photo is null && !removedLegacyUrl)
        {
            return NotFound("Photo was not found on this inventory item.");
        }

        item.PhotoLocalReferencesJson = photoUrls.Count == 0 ? null : JsonSerializer.Serialize(photoUrls);
        if (photo is null)
        {
            await photoStorage.DeleteAsync(currentUser.OrganizationId, item.Id, url);
        }

        if (photo?.IsHero == true)
        {
            InventoryPhoto? nextHero = item.Photos
                .Where(candidate => candidate.Id != photo.Id)
                .OrderBy(candidate => candidate.SortOrder)
                .ThenBy(candidate => candidate.CreatedAt)
                .FirstOrDefault();
            if (nextHero is not null)
            {
                nextHero.IsHero = true;
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        return new InventoryPhotoUploadResultDto(item.Id, photoUrls, UploadedUrl: null, OrderedPhotos(item).Where(candidate => candidate.Id != photo?.Id).Select(candidate => candidate.ToDto()).ToArray());
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

        InventoryPhoto? photo = ResolvePhotoByUrl(item, url);
        if (photo is not null)
        {
            foreach (var existingPhoto in item.Photos)
            {
                existingPhoto.IsHero = existingPhoto.Id == photo.Id;
            }
        }

        List<string> photoUrls = ParsePhotoUrls(item.PhotoLocalReferencesJson);
        int index = photoUrls.FindIndex(existing => string.Equals(existing, url, StringComparison.OrdinalIgnoreCase));
        if (index < 0 && photo is not null)
        {
            url = PhotoOriginalUrl(item.Id, photo.Id);
            index = photoUrls.FindIndex(existing => string.Equals(existing, url, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                photoUrls.Insert(0, url);
                index = 0;
            }
        }

        if (index < 0)
        {
            return NotFound("Photo was not found on this inventory item.");
        }

        photoUrls.RemoveAt(index);
        photoUrls.Insert(0, url);
        item.PhotoLocalReferencesJson = JsonSerializer.Serialize(photoUrls);
        await db.SaveChangesAsync(cancellationToken);

        return new InventoryPhotoUploadResultDto(item.Id, photoUrls, UploadedUrl: null, OrderedPhotos(item).Select(item => item.ToDto()).ToArray());
    }

    [HttpGet("{id:guid}/photos/{photoId:guid}/thumbnail")]
    [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Client)]
    public async Task<IActionResult> GetPhotoThumbnail(Guid id, Guid photoId, CancellationToken cancellationToken)
    {
        var photo = await FindPhoto(id, photoId, cancellationToken);
        return photo is null ? NotFound() : File(photo.ThumbnailBytes, "image/jpeg");
    }

    [HttpGet("{id:guid}/photos/{photoId:guid}/original")]
    [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Client)]
    public async Task<IActionResult> GetPhotoOriginal(Guid id, Guid photoId, CancellationToken cancellationToken)
    {
        var photo = await FindPhoto(id, photoId, cancellationToken);
        return photo is null ? NotFound() : File(photo.OriginalBytes, photo.ContentType);
    }

    [HttpDelete("{id:guid}/photos/{photoId:guid}")]
    public async Task<ActionResult<InventoryPhotoUploadResultDto>> DeletePhotoById(
        Guid id,
        Guid photoId,
        CancellationToken cancellationToken)
    {
        var item = await FindItem(id, cancellationToken);
        if (item is null)
        {
            return NotFound();
        }

        var photo = item.Photos.SingleOrDefault(candidate => candidate.Id == photoId);
        if (photo is null)
        {
            return NotFound("Photo was not found on this inventory item.");
        }

        db.InventoryPhotos.Remove(photo);
        List<string> photoUrls = ParsePhotoUrls(item.PhotoLocalReferencesJson);
        photoUrls.RemoveAll(existing =>
            string.Equals(existing, PhotoOriginalUrl(item.Id, photo.Id), StringComparison.OrdinalIgnoreCase)
            || string.Equals(existing, PhotoThumbnailUrl(item.Id, photo.Id), StringComparison.OrdinalIgnoreCase));
        item.PhotoLocalReferencesJson = photoUrls.Count == 0 ? null : JsonSerializer.Serialize(photoUrls);

        if (photo.IsHero)
        {
            InventoryPhoto? nextHero = item.Photos
                .Where(candidate => candidate.Id != photo.Id)
                .OrderBy(candidate => candidate.SortOrder)
                .ThenBy(candidate => candidate.CreatedAt)
                .FirstOrDefault();
            if (nextHero is not null)
            {
                nextHero.IsHero = true;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return new InventoryPhotoUploadResultDto(item.Id, photoUrls, UploadedUrl: null, OrderedPhotos(item).Where(candidate => candidate.Id != photo.Id).Select(candidate => candidate.ToDto()).ToArray());
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
            .Include(item => item.Photos)
            .OrderByDescending(item => item.UpdatedAt)
            .Take(200)
            .Select(item => item.ToDto())
            .ToListAsync(cancellationToken);

        return items;
    }

    [HttpGet("{id:guid}/issues")]
    public async Task<ActionResult<IReadOnlyList<InventoryIssueDto>>> ListIssues(Guid id, CancellationToken cancellationToken)
    {
        if (!await db.InventoryItems.AnyAsync(item => item.Id == id && item.OrganizationId == currentUser.OrganizationId, cancellationToken))
        {
            return NotFound();
        }

        var issues = await db.InventoryIssues
            .Where(item => item.InventoryItemId == id && item.OrganizationId == currentUser.OrganizationId)
            .OrderBy(item => item.IsResolved)
            .ThenBy(item => item.SortOrder)
            .ThenByDescending(item => item.CreatedAt)
            .Select(item => item.ToDto())
            .ToListAsync(cancellationToken);

        return issues;
    }

    [HttpPost("{id:guid}/issues")]
    public async Task<ActionResult<InventoryIssueDto>> CreateIssue(
        Guid id,
        UpsertInventoryIssueRequest request,
        CancellationToken cancellationToken)
    {
        var item = await FindItem(id, cancellationToken);
        if (item is null)
        {
            return NotFound();
        }

        string? validation = await ValidateIssueRequest(id, request, cancellationToken);
        if (validation is not null)
        {
            return BadRequest(validation);
        }

        var issue = new InventoryIssue
        {
            OrganizationId = currentUser.OrganizationId,
            InventoryItemId = id,
            InventoryPhotoId = request.InventoryPhotoId,
            IssueType = request.IssueType.Trim(),
            Severity = request.Severity.Trim(),
            Note = TrimToNull(request.Note),
            SortOrder = request.SortOrder ?? await NextIssueSortOrder(id, cancellationToken),
            IsResolved = request.IsResolved ?? false
        };

        db.InventoryIssues.Add(issue);
        await db.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(ListIssues), new { id }, issue.ToDto());
    }

    [HttpPut("{id:guid}/issues/{issueId:guid}")]
    public async Task<ActionResult<InventoryIssueDto>> UpdateIssue(
        Guid id,
        Guid issueId,
        UpsertInventoryIssueRequest request,
        CancellationToken cancellationToken)
    {
        var issue = await db.InventoryIssues.SingleOrDefaultAsync(
            item => item.Id == issueId && item.InventoryItemId == id && item.OrganizationId == currentUser.OrganizationId,
            cancellationToken);
        if (issue is null)
        {
            return NotFound();
        }

        string? validation = await ValidateIssueRequest(id, request, cancellationToken);
        if (validation is not null)
        {
            return BadRequest(validation);
        }

        issue.InventoryPhotoId = request.InventoryPhotoId;
        issue.IssueType = request.IssueType.Trim();
        issue.Severity = request.Severity.Trim();
        issue.Note = TrimToNull(request.Note);
        issue.SortOrder = request.SortOrder ?? issue.SortOrder;
        issue.IsResolved = request.IsResolved ?? issue.IsResolved;

        await db.SaveChangesAsync(cancellationToken);
        return issue.ToDto();
    }

    [HttpDelete("{id:guid}/issues/{issueId:guid}")]
    public async Task<IActionResult> DeleteIssue(Guid id, Guid issueId, CancellationToken cancellationToken)
    {
        var issue = await db.InventoryIssues.SingleOrDefaultAsync(
            item => item.Id == issueId && item.InventoryItemId == id && item.OrganizationId == currentUser.OrganizationId,
            cancellationToken);
        if (issue is null)
        {
            return NotFound();
        }

        db.InventoryIssues.Remove(issue);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
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
        db.InventoryItems
            .Include(item => item.Photos)
            .Include(item => item.Issues)
            .SingleOrDefaultAsync(
            item => item.Id == id && item.OrganizationId == currentUser.OrganizationId,
            cancellationToken);

    private Task<InventoryPhoto?> FindPhoto(Guid itemId, Guid photoId, CancellationToken cancellationToken) =>
        db.InventoryPhotos.SingleOrDefaultAsync(
            item => item.Id == photoId
                && item.InventoryItemId == itemId
                && item.OrganizationId == currentUser.OrganizationId,
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
        item.Category = TrimToNull(request.Category);
        item.Brand = TrimToNull(request.Brand);
        item.Model = TrimToNull(request.Model);
        item.Color = TrimToNull(request.Color);
        item.Condition = TrimToNull(request.Condition);
        item.SizeSystem = TrimToNull(request.SizeSystem);
        item.SizeValue = TrimToNull(request.SizeValue);
        item.AttributesJson = NormalizeJsonObject(request.AttributesJson);
        item.MeasurementsJson = NormalizeJsonObject(request.MeasurementsJson);
        item.StorageLocation = TrimToNull(request.StorageLocation);
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

    private static string? TrimToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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

    private async Task<string?> ValidateIssueRequest(Guid inventoryItemId, UpsertInventoryIssueRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.IssueType))
        {
            return "Issue type is required.";
        }

        if (string.IsNullOrWhiteSpace(request.Severity))
        {
            return "Issue severity is required.";
        }

        if (request.InventoryPhotoId.HasValue)
        {
            bool photoExists = await db.InventoryPhotos.AnyAsync(
                item => item.Id == request.InventoryPhotoId.Value
                    && item.InventoryItemId == inventoryItemId
                    && item.OrganizationId == currentUser.OrganizationId,
                cancellationToken);
            if (!photoExists)
            {
                return "Issue photo does not belong to this inventory item.";
            }
        }

        return null;
    }

    private async Task<int> NextIssueSortOrder(Guid inventoryItemId, CancellationToken cancellationToken)
    {
        int? max = await db.InventoryIssues
            .Where(item => item.InventoryItemId == inventoryItemId && item.OrganizationId == currentUser.OrganizationId)
            .MaxAsync(item => (int?)item.SortOrder, cancellationToken);
        return (max ?? -1) + 1;
    }

    private static IReadOnlyList<InventoryPhoto> OrderedPhotos(InventoryItem item) =>
        item.Photos
            .OrderByDescending(photo => photo.IsHero)
            .ThenBy(photo => photo.SortOrder)
            .ThenBy(photo => photo.CreatedAt)
            .ToArray();

    private static InventoryPhoto? ResolvePhotoByUrl(InventoryItem item, string url)
    {
        if (TryExtractPhotoId(url, out Guid photoId))
        {
            return item.Photos.SingleOrDefault(photo => photo.Id == photoId);
        }

        return null;
    }

    private static bool TryExtractPhotoId(string url, out Guid photoId)
    {
        photoId = default;
        string[] parts = url.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        int photosIndex = Array.FindIndex(parts, part => string.Equals(part, "photos", StringComparison.OrdinalIgnoreCase));
        return photosIndex >= 0
            && photosIndex + 1 < parts.Length
            && Guid.TryParse(parts[photosIndex + 1], out photoId);
    }

    private static string PhotoOriginalUrl(Guid itemId, Guid photoId) => $"/api/inventory/{itemId}/photos/{photoId}/original";

    private static string PhotoThumbnailUrl(Guid itemId, Guid photoId) => $"/api/inventory/{itemId}/photos/{photoId}/thumbnail";

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
