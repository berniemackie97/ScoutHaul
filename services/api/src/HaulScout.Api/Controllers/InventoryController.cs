using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HaulScout.Api.Authorization;
using HaulScout.Api.Mapping;
using HaulScout.Api.Services;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Services;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;
using System.Text.Json;
using static HaulScout.Api.Helpers.StringHelpers;

namespace HaulScout.Api.Controllers;

[ApiController]
[Route("api/inventory")]
[RequireOrganizationPermission(HaulScoutPermissions.ManageInventory)]
public sealed class InventoryController(
    HaulScoutDbContext db,
    ICurrentUserContext currentUser,
    InventoryPhotoStorageService photoStorage,
    InventoryReferenceDataService referenceData,
    InventoryBundleWorkflowService? bundleWorkflow = null,
    InventoryDeletionWorkflowService? deletionWorkflow = null,
    InventoryUpsertWorkflowService? upsertWorkflow = null) : ControllerBase
{
    private InventoryBundleWorkflowService Bundles => bundleWorkflow ?? new InventoryBundleWorkflowService(db, currentUser);
    private InventoryDeletionWorkflowService Deletion => deletionWorkflow ?? new InventoryDeletionWorkflowService(db, currentUser);
    private InventoryUpsertWorkflowService Upserts => upsertWorkflow ?? new InventoryUpsertWorkflowService(db, currentUser, referenceData);

    public sealed class UploadInventoryPhotoForm
    {
        public IFormFile? File { get; set; }
        public bool IsHero { get; set; }
    }

    [HttpPost]
    public async Task<ActionResult<InventoryItemDto>> Create(UpsertInventoryItemRequest request, CancellationToken cancellationToken)
    {
        AppResult<InventoryItemDto> result = await Upserts.CreateAsync(request, cancellationToken);
        return result.Status switch
        {
            AppResultStatus.Ok when result.Value is not null => CreatedAtAction(nameof(Get), new { id = result.Value.Id }, result.Value),
            AppResultStatus.NotFound => NotFound(result.Message),
            AppResultStatus.Conflict => Conflict(result.Message),
            AppResultStatus.Validation => BadRequest(result.Message),
            _ => BadRequest(result.Message),
        };
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<InventoryItemDto>> Update(Guid id, UpsertInventoryItemRequest request, CancellationToken cancellationToken)
    {
        AppResult<InventoryItemDto> result = await Upserts.UpdateAsync(id, request, cancellationToken);
        return result.Status switch
        {
            AppResultStatus.Ok when result.Value is not null => result.Value,
            AppResultStatus.NotFound => NotFound(result.Message),
            AppResultStatus.Conflict => Conflict(result.Message),
            AppResultStatus.Validation => BadRequest(result.Message),
            _ => BadRequest(result.Message),
        };
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<InventoryItemDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        InventoryItem? item = await FindItem(id, cancellationToken);
        return item is null ? NotFound() : item.ToDto();
    }

    [HttpGet("reference-data")]
    public Task<InventoryReferenceDataDto> ReferenceData(CancellationToken cancellationToken) =>
        referenceData.GetAsync(cancellationToken);

    [HttpPost("bundles")]
    public async Task<ActionResult<InventoryItemDto>> CreateBundle(CreateInventoryBundleRequest request, CancellationToken cancellationToken)
    {
        AppResult<InventoryItemDto> result = await Bundles.CreateAsync(request, cancellationToken);
        return result.Status switch
        {
            AppResultStatus.Ok when result.Value is not null => CreatedAtAction(nameof(Get), new { id = result.Value.Id }, result.Value),
            AppResultStatus.NotFound => NotFound(result.Message),
            AppResultStatus.Conflict => Conflict(result.Message),
            AppResultStatus.Validation => BadRequest(result.Message),
            _ => BadRequest(result.Message),
        };
    }

    [HttpPost("{id:guid}/photos")]
    [RequestSizeLimit(10_000_000)]
    public async Task<ActionResult<InventoryPhotoUploadResultDto>> UploadPhoto(
        Guid id,
        [FromForm] UploadInventoryPhotoForm request,
        CancellationToken cancellationToken)
    {
        InventoryItem? item = await FindItem(id, cancellationToken);
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
            foreach (InventoryPhoto existingPhoto in item.Photos)
            {
                existingPhoto.IsHero = false;
            }
        }

        InventoryPhoto photo = new()
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
        InventoryItem? item = await FindItem(id, cancellationToken);
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
        InventoryItem? item = await FindItem(id, cancellationToken);
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
            foreach (InventoryPhoto existingPhoto in item.Photos)
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

    // Public so a plain <Image> tag (which can't send auth headers) can load it. Safe: addressed by
    // two unguessable GUIDs (item + photo), and only image bytes are exposed.
    [HttpGet("{id:guid}/photos/{photoId:guid}/thumbnail")]
    [AllowAnonymous]
    [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Client)]
    public async Task<IActionResult> GetPhotoThumbnail(Guid id, Guid photoId, CancellationToken cancellationToken)
    {
        InventoryPhoto? photo = await FindPhotoUnscoped(id, photoId, cancellationToken);
        return photo is null ? NotFound() : File(photo.ThumbnailBytes, "image/jpeg");
    }

    [HttpGet("{id:guid}/photos/{photoId:guid}/original")]
    [AllowAnonymous]
    [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Client)]
    public async Task<IActionResult> GetPhotoOriginal(Guid id, Guid photoId, CancellationToken cancellationToken)
    {
        InventoryPhoto? photo = await FindPhotoUnscoped(id, photoId, cancellationToken);
        return photo is null ? NotFound() : File(photo.OriginalBytes, photo.ContentType);
    }

    [HttpDelete("{id:guid}/photos/{photoId:guid}")]
    public async Task<ActionResult<InventoryPhotoUploadResultDto>> DeletePhotoById(
        Guid id,
        Guid photoId,
        CancellationToken cancellationToken)
    {
        InventoryItem? item = await FindItem(id, cancellationToken);
        if (item is null)
        {
            return NotFound();
        }

        InventoryPhoto? photo = item.Photos.SingleOrDefault(candidate => candidate.Id == photoId);
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
        [FromQuery] InventoryLifecycleGroup? lifecycle,
        [FromQuery] bool includeArchived,
        CancellationToken cancellationToken)
    {
        IQueryable<InventoryItem> query = db.InventoryItems.Where(item => item.OrganizationId == currentUser.OrganizationId);

        if (!includeArchived)
        {
            query = query.Where(item => !item.IsArchived);
        }

        if (status.HasValue)
        {
            query = query.Where(item => item.Status == status.Value);
        }

        // Order-aware filter: e.g. "Active" excludes Sold/Packed/Shipped/Delivered, so a shipped
        // item never shows up under "not sold".
        if (lifecycle.HasValue)
        {
            InventoryStatus[] groupStatuses = InventoryLifecycle.InGroup(lifecycle.Value).ToArray();
            query = query.Where(item => groupStatuses.Contains(item.Status));
        }

        List<InventoryItemDto> items = await query
            .Include(item => item.Photos)
            .Include(item => item.AcquisitionLotItems)
                .ThenInclude(item => item.AcquisitionLot)
            .Include(item => item.BundleComponents)
            .OrderByDescending(item => item.UpdatedAt)
            .Take(200)
            .Select(item => item.ToDto())
            .ToListAsync(cancellationToken);

        return items;
    }

    /// <summary>Searchable, sortable, paged inventory for the admin table, plus a summary over the whole filtered
    /// set (so the KPIs stay correct while the table pages). Separate from <see cref="List"/> to keep that plain
    /// array response stable for its many callers (mobile, extension, pickers).</summary>
    [HttpGet("browse")]
    public async Task<ActionResult<InventoryListDto>> Browse(
        [FromQuery] InventoryStatus? status,
        [FromQuery] InventoryLifecycleGroup? lifecycle,
        [FromQuery] bool includeArchived,
        [FromQuery] string? search,
        [FromQuery] string? sort,
        [FromQuery] bool desc = true,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        IQueryable<InventoryItem> query = db.InventoryItems.Where(item => item.OrganizationId == currentUser.OrganizationId);

        if (!includeArchived)
        {
            query = query.Where(item => !item.IsArchived);
        }

        if (status.HasValue)
        {
            query = query.Where(item => item.Status == status.Value);
        }

        if (lifecycle.HasValue)
        {
            InventoryStatus[] groupStatuses = InventoryLifecycle.InGroup(lifecycle.Value).ToArray();
            query = query.Where(item => groupStatuses.Contains(item.Status));
        }

        string? term = TrimToNull(search)?.ToLower();
        if (term is not null)
        {
            query = query.Where(item =>
                item.Title.ToLower().Contains(term)
                || (item.Brand != null && item.Brand.ToLower().Contains(term))
                || (item.Model != null && item.Model.ToLower().Contains(term))
                || (item.Category != null && item.Category.ToLower().Contains(term))
                || (item.Barcode != null && item.Barcode.ToLower().Contains(term))
                || (item.StorageLocation != null && item.StorageLocation.ToLower().Contains(term)));
        }

        // Summary over the whole filtered set (scalars only — no includes).
        var scalars = await query
            .Select(item => new { item.Status, item.PurchasePrice, item.EstimatedSalePrice, item.SellerAskingPrice })
            .ToListAsync(cancellationToken);
        int total = scalars.Count;
        InventorySummaryDto summary = new(
            total,
            scalars.Count(item => InventoryLifecycle.Group(item.Status) == InventoryLifecycleGroup.Active),
            scalars.Count(item => item.Status is InventoryStatus.ReadyToList or InventoryStatus.Listed),
            scalars.Count(item => InventoryLifecycle.IsSold(item.Status)),
            scalars.Sum(item => item.PurchasePrice ?? 0),
            scalars.Sum(item => item.EstimatedSalePrice ?? item.SellerAskingPrice ?? 0));

        query = sort?.ToLowerInvariant() switch
        {
            "title" => desc ? query.OrderByDescending(item => item.Title) : query.OrderBy(item => item.Title),
            "purchaseprice" => desc ? query.OrderByDescending(item => item.PurchasePrice) : query.OrderBy(item => item.PurchasePrice),
            "estimatedsaleprice" => desc ? query.OrderByDescending(item => item.EstimatedSalePrice) : query.OrderBy(item => item.EstimatedSalePrice),
            "createdat" => desc ? query.OrderByDescending(item => item.CreatedAt) : query.OrderBy(item => item.CreatedAt),
            _ => desc ? query.OrderByDescending(item => item.UpdatedAt) : query.OrderBy(item => item.UpdatedAt),
        };

        List<InventoryItem> pageItems = await query
            .Include(item => item.Photos)
            .Include(item => item.AcquisitionLotItems)
                .ThenInclude(item => item.AcquisitionLot)
            .Include(item => item.BundleComponents)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        List<InventoryItemDto> dtos = pageItems.Select(item => item.ToDto()).ToList();
        int pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        return new InventoryListDto(dtos, total, page, pageSize, pageCount, summary);
    }

    [HttpGet("{id:guid}/issues")]
    public async Task<ActionResult<IReadOnlyList<InventoryIssueDto>>> ListIssues(Guid id, CancellationToken cancellationToken)
    {
        if (!await db.InventoryItems.AnyAsync(item => item.Id == id && item.OrganizationId == currentUser.OrganizationId, cancellationToken))
        {
            return NotFound();
        }

        List<InventoryIssueDto> issues = await db.InventoryIssues
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
        InventoryItem? item = await FindItem(id, cancellationToken);
        if (item is null)
        {
            return NotFound();
        }

        string? validation = await ValidateIssueRequest(id, request, cancellationToken);
        if (validation is not null)
        {
            return BadRequest(validation);
        }

        InventoryIssue issue = new()
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
        InventoryIssue? issue = await db.InventoryIssues.SingleOrDefaultAsync(
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
        InventoryIssue? issue = await db.InventoryIssues.SingleOrDefaultAsync(
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
        InventoryItem? item = await FindItem(id, cancellationToken);
        if (item is null)
        {
            return NotFound();
        }

        bool hasActiveSale = await db.Sales.AnyAsync(
            sale => sale.InventoryItemId == id
                && sale.OrganizationId == currentUser.OrganizationId
                && sale.Status != SaleStatus.Returned
                && sale.Status != SaleStatus.Refunded
                && sale.Status != SaleStatus.Cancelled,
            cancellationToken);
        if (hasActiveSale)
        {
            return Conflict("This item has an active sale. Change its stage through the sales workflow.");
        }

        if (request.Status is InventoryStatus.Bundled
            or InventoryStatus.Sold
            or InventoryStatus.Packed
            or InventoryStatus.Shipped
            or InventoryStatus.Delivered
            or InventoryStatus.Returned)
        {
            return Conflict("Bundle, sale, and fulfillment stages must be changed through their dedicated workflows.");
        }

        item.Status = request.Status;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        item.PurchasedAt ??= request.Status == InventoryStatus.Purchased ? now : null;
        item.ListedAt ??= request.Status == InventoryStatus.Listed ? now : null;

        await db.SaveChangesAsync(cancellationToken);
        return item.ToDto();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(
        Guid id,
        [FromQuery] bool hard = false,
        CancellationToken cancellationToken = default)
    {
        AppResult result = await Deletion.DeleteAsync(id, hard, cancellationToken);
        return result.Status switch
        {
            AppResultStatus.Ok => NoContent(),
            AppResultStatus.NotFound => NotFound(result.Message),
            AppResultStatus.Conflict => Conflict(result.Message),
            AppResultStatus.Validation => BadRequest(result.Message),
            _ => BadRequest(result.Message),
        };
    }

    private Task<InventoryItem?> FindItem(Guid id, CancellationToken cancellationToken) =>
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

    private Task<InventoryPhoto?> FindPhoto(Guid itemId, Guid photoId, CancellationToken cancellationToken) =>
        db.InventoryPhotos.SingleOrDefaultAsync(
            item => item.Id == photoId
                && item.InventoryItemId == itemId
                && item.OrganizationId == currentUser.OrganizationId,
            cancellationToken);

    // Org-unscoped (no authenticated user) — used only by the anonymous image-byte GET endpoints,
    // which are already gated by the unguessable {itemId}+{photoId} GUID pair.
    // Serves photo bytes to anonymous <img>/Image requests by unguessable GUID. Must IgnoreQueryFilters:
    // the endpoint is [AllowAnonymous], so no tenant is resolved and the org query filter would otherwise
    // fail closed (OrganizationId == Guid.Empty) and 404 every photo.
    private Task<InventoryPhoto?> FindPhotoUnscoped(Guid itemId, Guid photoId, CancellationToken cancellationToken) =>
        db.InventoryPhotos
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                item => item.Id == photoId && item.InventoryItemId == itemId,
                cancellationToken);

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
