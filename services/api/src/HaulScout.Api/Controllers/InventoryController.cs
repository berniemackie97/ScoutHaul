using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HaulScout.Api.Authorization;
using HaulScout.Api.Mapping;
using HaulScout.Api.Services;
using HaulScout.Application.Contracts;
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
    InventoryReferenceDataService referenceData) : ControllerBase
{
    private static readonly SaleStatus[] ReleasedSaleStatuses = [SaleStatus.Returned, SaleStatus.Refunded, SaleStatus.Cancelled];

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

        (InventoryReferenceDataService.ControlledValues? controlledValues, string? controlledError) =
            await referenceData.ResolveAsync(request, cancellationToken);
        if (controlledError is not null)
        {
            return BadRequest(controlledError);
        }

        InventoryStatus startingStatus = request.Status ?? (request.PurchasePrice.HasValue ? InventoryStatus.Purchased : InventoryStatus.Draft);
        if (!CanCreateInStatus(startingStatus))
        {
            return BadRequest("A new inventory item must start in a pre-sale intake, prep, or ready-to-list stage.");
        }

        var item = new InventoryItem
        {
            OrganizationId = currentUser.OrganizationId,
            Title = request.Title.Trim(),
            Status = startingStatus,
            PurchasedAt = startingStatus == InventoryStatus.Purchased || request.PurchasePrice.HasValue ? DateTimeOffset.UtcNow : null
        };

        Apply(request, controlledValues!, item);
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

        (InventoryReferenceDataService.ControlledValues? controlledValues, string? controlledError) =
            await referenceData.ResolveAsync(request, cancellationToken);
        if (controlledError is not null)
        {
            return BadRequest(controlledError);
        }

        decimal? previousPurchasePrice = item.PurchasePrice;
        Apply(request, controlledValues!, item);
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

    [HttpGet("reference-data")]
    public Task<InventoryReferenceDataDto> ReferenceData(CancellationToken cancellationToken) =>
        referenceData.GetAsync(cancellationToken);

    [HttpPost("bundles")]
    public async Task<ActionResult<InventoryItemDto>> CreateBundle(CreateInventoryBundleRequest request, CancellationToken cancellationToken)
    {
        string? validation = ValidateBundle(request);
        if (validation is not null)
        {
            return BadRequest(validation);
        }

        Guid[] componentIds = request.ComponentInventoryItemIds.Distinct().ToArray();
        var components = await db.InventoryItems
            .Include(item => item.Sales)
            .Where(item => componentIds.Contains(item.Id) && item.OrganizationId == currentUser.OrganizationId)
            .ToListAsync(cancellationToken);

        if (components.Count != componentIds.Length)
        {
            return NotFound("One or more bundle components were not found.");
        }

        var blocked = components
            .Where(item => item.IsArchived
                || item.IsBundle
                || item.Status is InventoryStatus.Bundled
                    or InventoryStatus.Sold
                    or InventoryStatus.Packed
                    or InventoryStatus.Shipped
                    or InventoryStatus.Delivered
                    or InventoryStatus.Returned
                    or InventoryStatus.Donated
                    or InventoryStatus.Disposed
                || item.Sales.Any(sale => !ReleasedSaleStatuses.Contains(sale.Status)))
            .Select(item => item.Title)
            .ToArray();
        if (blocked.Length > 0)
        {
            return Conflict($"These items cannot be bundled because they are unavailable, sold, archived, or already bundled: {string.Join(", ", blocked)}.");
        }

        InventoryStatus bundleStatus = request.Status ?? InventoryStatus.ReadyToList;
        if (bundleStatus is InventoryStatus.Bundled
            or InventoryStatus.Sold
            or InventoryStatus.Packed
            or InventoryStatus.Shipped
            or InventoryStatus.Delivered
            or InventoryStatus.Returned
            or InventoryStatus.Donated
            or InventoryStatus.Disposed)
        {
            return BadRequest("A new bundle must start in an unsold listing-prep stage.");
        }

        var bundle = new InventoryItem
        {
            OrganizationId = currentUser.OrganizationId,
            Title = request.Title.Trim(),
            Description = TrimToNull(request.Description),
            Category = TrimToNull(request.Category) ?? components.Select(item => item.Category).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
            EstimatedSalePrice = request.EstimatedSalePrice,
            PurchasePrice = components.Sum(item => item.PurchasePrice ?? 0),
            Status = bundleStatus,
            IsBundle = true,
            PurchasedAt = components
                .Where(item => item.PurchasedAt.HasValue)
                .Select(item => item.PurchasedAt)
                .OrderBy(item => item)
                .FirstOrDefault(),
            Notes = TrimToNull(request.Notes)
        };

        db.InventoryItems.Add(bundle);
        foreach (var component in components.OrderBy(item => item.Title))
        {
            component.Status = InventoryStatus.Bundled;
            db.InventoryBundleComponents.Add(new InventoryBundleComponent
            {
                OrganizationId = currentUser.OrganizationId,
                BundleInventoryItem = bundle,
                ComponentInventoryItemId = component.Id,
                Quantity = Math.Max(1, component.Quantity),
                CostSnapshot = component.PurchasePrice ?? 0,
                TitleSnapshot = component.Title,
            });
        }

        db.AuditLogs.Add(new AuditLog
        {
            OrganizationId = currentUser.OrganizationId,
            ActorUserId = currentUser.UserId,
            EntityName = nameof(InventoryItem),
            Action = "BundleCreated",
            DetailsJson = JsonSerializer.Serialize(new
            {
                BundleTitle = bundle.Title,
                ComponentInventoryItemIds = componentIds,
                ComponentCount = componentIds.Length,
            })
        });

        await db.SaveChangesAsync(cancellationToken);
        await db.Entry(bundle).Collection(item => item.BundleComponents).LoadAsync(cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = bundle.Id }, bundle.ToDto());
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

    // Public so a plain <Image> tag (which can't send auth headers) can load it. Safe: addressed by
    // two unguessable GUIDs (item + photo), and only image bytes are exposed.
    [HttpGet("{id:guid}/photos/{photoId:guid}/thumbnail")]
    [AllowAnonymous]
    [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Client)]
    public async Task<IActionResult> GetPhotoThumbnail(Guid id, Guid photoId, CancellationToken cancellationToken)
    {
        var photo = await FindPhotoUnscoped(id, photoId, cancellationToken);
        return photo is null ? NotFound() : File(photo.ThumbnailBytes, "image/jpeg");
    }

    [HttpGet("{id:guid}/photos/{photoId:guid}/original")]
    [AllowAnonymous]
    [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Client)]
    public async Task<IActionResult> GetPhotoOriginal(Guid id, Guid photoId, CancellationToken cancellationToken)
    {
        var photo = await FindPhotoUnscoped(id, photoId, cancellationToken);
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
        [FromQuery] InventoryLifecycleGroup? lifecycle,
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

        // Order-aware filter: e.g. "Active" excludes Sold/Packed/Shipped/Delivered, so a shipped
        // item never shows up under "not sold".
        if (lifecycle.HasValue)
        {
            var groupStatuses = InventoryLifecycle.InGroup(lifecycle.Value).ToArray();
            query = query.Where(item => groupStatuses.Contains(item.Status));
        }

        var items = await query
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
        var now = DateTimeOffset.UtcNow;
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
        var item = await FindItem(id, cancellationToken);
        if (item is null)
        {
            return NotFound();
        }

        string? deleteBlockReason = await ValidateDeleteOrArchive(item, cancellationToken);
        if (deleteBlockReason is not null)
        {
            return Conflict(deleteBlockReason);
        }

        if (hard)
        {
            string? blockReason = await ValidateHardDelete(item, cancellationToken);
            if (blockReason is not null)
            {
                return Conflict(blockReason);
            }

            await RemoveInventoryItemGraph(item, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return NoContent();
        }

        item.IsArchived = true;
        if (item.IsBundle)
        {
            ReleaseBundleComponents(item);
        }

        db.AuditLogs.Add(new AuditLog
        {
            OrganizationId = currentUser.OrganizationId,
            ActorUserId = currentUser.UserId,
            EntityName = nameof(InventoryItem),
            EntityId = item.Id,
            Action = "InventoryArchived",
            DetailsJson = JsonSerializer.Serialize(new { item.Title, item.Status, item.IsBundle })
        });

        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private async Task<string?> ValidateDeleteOrArchive(InventoryItem item, CancellationToken cancellationToken)
    {
        if (item.Status == InventoryStatus.Listed)
        {
            return "This inventory item is marked Listed. Delist it from every channel and move it out of the Listed stage before deleting or archiving it.";
        }

        bool hasActiveSale = await db.Sales.AnyAsync(
            sale => sale.InventoryItemId == item.Id
                && sale.OrganizationId == currentUser.OrganizationId
                && sale.Status != SaleStatus.Returned
                && sale.Status != SaleStatus.Refunded
                && sale.Status != SaleStatus.Cancelled,
            cancellationToken);
        if (hasActiveSale)
        {
            return "This inventory item has an active sale. Complete, cancel, refund, or return the sale workflow before deleting or archiving it.";
        }

        bool isBundleComponent = await db.InventoryBundleComponents.AnyAsync(
            component => component.ComponentInventoryItemId == item.Id && component.OrganizationId == currentUser.OrganizationId,
            cancellationToken);
        if (isBundleComponent)
        {
            return "This item is inside a bundle. Delete or edit the bundle before deleting or archiving the component item.";
        }

        bool hasLiveChannelListing = await db.ChannelListings.AnyAsync(
            listing => listing.InventoryItemId == item.Id
                && listing.OrganizationId == currentUser.OrganizationId
                && (listing.Status == ChannelListingStatus.Active || listing.Status == ChannelListingStatus.PendingDelist),
            cancellationToken);
        if (hasLiveChannelListing)
        {
            return "This inventory item still has live or pending-delist channel listings. Delist every channel before deleting or archiving it.";
        }

        bool hasActiveListing = await db.Listings.AnyAsync(
            listing => listing.InventoryItemId == item.Id
                && listing.OrganizationId == currentUser.OrganizationId
                && listing.Status == ListingStatus.Active,
            cancellationToken);
        if (hasActiveListing)
        {
            return "This inventory item still has an active marketplace listing. End or delist the listing before deleting or archiving it.";
        }

        bool hasActiveStorefrontListing = await db.StoreListings.AnyAsync(
            listing => listing.InventoryItemId == item.Id
                && listing.OrganizationId == currentUser.OrganizationId
                && (listing.Status == StoreListingStatus.Active || listing.Status == StoreListingStatus.Reserved),
            cancellationToken);
        if (hasActiveStorefrontListing)
        {
            return "This inventory item is still active or reserved on the storefront. Archive the storefront listing before deleting or archiving the item.";
        }

        return null;
    }

    private async Task<string?> ValidateHardDelete(InventoryItem item, CancellationToken cancellationToken)
    {
        bool hasSaleHistory = await db.Sales.AnyAsync(
            sale => sale.InventoryItemId == item.Id && sale.OrganizationId == currentUser.OrganizationId,
            cancellationToken);
        if (hasSaleHistory)
        {
            return "This inventory item has sale history. Archive it instead so financial reporting and corrections remain intact.";
        }

        bool hasOrderHistory = await db.OrderLines.AnyAsync(
            line => line.InventoryItemId == item.Id && line.OrganizationId == currentUser.OrganizationId,
            cancellationToken);
        if (hasOrderHistory)
        {
            return "This inventory item is attached to a customer order. Archive it instead so customer/order history remains intact.";
        }

        bool isBundleComponent = await db.InventoryBundleComponents.AnyAsync(
            component => component.ComponentInventoryItemId == item.Id && component.OrganizationId == currentUser.OrganizationId,
            cancellationToken);
        if (isBundleComponent)
        {
            return "This item is inside a bundle. Delete or edit the bundle before deleting the component item.";
        }

        return null;
    }

    private async Task RemoveInventoryItemGraph(InventoryItem item, CancellationToken cancellationToken)
    {
        ReleaseBundleComponents(item);

        var channelListings = await db.ChannelListings
            .Where(listing => listing.InventoryItemId == item.Id && listing.OrganizationId == currentUser.OrganizationId)
            .ToListAsync(cancellationToken);
        db.ChannelListings.RemoveRange(channelListings);

        var expenses = await db.Expenses
            .Where(expense => expense.InventoryItemId == item.Id && expense.OrganizationId == currentUser.OrganizationId)
            .ToListAsync(cancellationToken);
        db.Expenses.RemoveRange(expenses);

        var observations = await db.MarketObservations
            .Where(observation => observation.InventoryItemId == item.Id && observation.OrganizationId == currentUser.OrganizationId)
            .ToListAsync(cancellationToken);
        foreach (var observation in observations)
        {
            observation.InventoryItemId = null;
        }

        db.InventoryItems.Remove(item);
        db.AuditLogs.Add(new AuditLog
        {
            OrganizationId = currentUser.OrganizationId,
            ActorUserId = currentUser.UserId,
            EntityName = nameof(InventoryItem),
            EntityId = item.Id,
            Action = "InventoryDeleted",
            DetailsJson = JsonSerializer.Serialize(new { item.Title, item.Status, item.IsBundle })
        });
    }

    private static void ReleaseBundleComponents(InventoryItem item)
    {
        if (!item.IsBundle)
        {
            return;
        }

        foreach (var component in item.BundleComponents)
        {
            if (component.ComponentInventoryItem is not null && component.ComponentInventoryItem.Status == InventoryStatus.Bundled)
            {
                component.ComponentInventoryItem.Status = InventoryStatus.ReadyToList;
            }
        }
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

    private static string? ValidateBundle(CreateInventoryBundleRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return "Bundle title is required.";
        }

        if (request.EstimatedSalePrice is < 0)
        {
            return "Estimated sale price cannot be negative.";
        }

        if (request.ComponentInventoryItemIds is null || request.ComponentInventoryItemIds.Distinct().Count() < 2)
        {
            return "Select at least two distinct inventory items to build a bundle.";
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
