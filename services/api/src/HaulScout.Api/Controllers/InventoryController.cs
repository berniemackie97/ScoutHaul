using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HaulScout.Api.Authorization;
using HaulScout.Api.Services;
using HaulScout.Application.Inventory;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Domain;

namespace HaulScout.Api.Controllers;

[ApiController]
[Route("api/inventory")]
[RequireOrganizationPermission(HaulScoutPermissions.ManageInventory)]
public sealed class InventoryController(
    InventoryReferenceDataService referenceData,
    InventoryBundleWorkflowService bundles,
    InventoryDeletionWorkflowService deletion,
    InventoryUpsertWorkflowService upserts,
    InventoryQueryService queries,
    InventoryPhotoWorkflowService photos,
    InventoryIssueWorkflowService issues,
    InventoryStatusWorkflowService statuses) : HaulScoutControllerBase
{
    public sealed class UploadInventoryPhotoForm
    {
        public IFormFile? File { get; set; }
        public bool IsHero { get; set; }
    }

    [HttpPost]
    public async Task<ActionResult<InventoryItemDto>> Create(
        UpsertInventoryItemRequest request,
        CancellationToken cancellationToken)
    {
        AppResult<InventoryItemDto> result = await upserts.CreateAsync(request, cancellationToken);
        return ToActionResult(result, value => CreatedAtAction(nameof(Get), new { id = value.Id }, value));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<InventoryItemDto>> Update(
        Guid id,
        UpsertInventoryItemRequest request,
        CancellationToken cancellationToken)
    {
        AppResult<InventoryItemDto> result = await upserts.UpdateAsync(id, request, cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<InventoryItemDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        InventoryItemDto? item = await queries.GetAsync(id, cancellationToken);
        return item is null ? NotFound() : item;
    }

    [HttpGet("reference-data")]
    public Task<InventoryReferenceDataDto> ReferenceData(CancellationToken cancellationToken) =>
        referenceData.GetAsync(cancellationToken);

    [HttpPost("bundles")]
    public async Task<ActionResult<InventoryItemDto>> CreateBundle(
        CreateInventoryBundleRequest request,
        CancellationToken cancellationToken)
    {
        AppResult<InventoryItemDto> result = await bundles.CreateAsync(request, cancellationToken);
        return ToActionResult(result, value => CreatedAtAction(nameof(Get), new { id = value.Id }, value));
    }

    [HttpPost("{id:guid}/photos")]
    [RequestSizeLimit(10_000_000)]
    public async Task<ActionResult<InventoryPhotoUploadResultDto>> UploadPhoto(
        Guid id,
        [FromForm] UploadInventoryPhotoForm request,
        CancellationToken cancellationToken)
    {
        // The web boundary ends here: IFormFile is adapted into the framework-free PhotoUpload the use case takes.
        PhotoUpload? upload = request.File is null
            ? null
            : new PhotoUpload(request.File.OpenReadStream(), request.File.FileName, request.File.ContentType, request.File.Length);

        AppResult<InventoryPhotoUploadResultDto> result = await photos.UploadAsync(id, upload, request.IsHero, cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>Attach a photo by URL — for images that live on a live listing rather than the device.</summary>
    [HttpPost("{id:guid}/photos/from-url")]
    public async Task<ActionResult<InventoryPhotoUploadResultDto>> UploadPhotoFromUrl(
        Guid id,
        ImportInventoryPhotoRequest request,
        CancellationToken cancellationToken)
    {
        AppResult<InventoryPhotoUploadResultDto> result =
            await photos.UploadFromUrlAsync(id, request.Url, request.IsHero, cancellationToken);
        return ToActionResult(result);
    }

    [HttpDelete("{id:guid}/photos")]
    public async Task<ActionResult<InventoryPhotoUploadResultDto>> DeletePhoto(
        Guid id,
        [FromQuery] string url,
        CancellationToken cancellationToken)
    {
        AppResult<InventoryPhotoUploadResultDto> result = await photos.DeleteByUrlAsync(id, url, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPatch("{id:guid}/photos/hero")]
    public async Task<ActionResult<InventoryPhotoUploadResultDto>> SetHeroPhoto(
        Guid id,
        [FromQuery] string url,
        CancellationToken cancellationToken)
    {
        AppResult<InventoryPhotoUploadResultDto> result = await photos.SetHeroAsync(id, url, cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet("{id:guid}/photos/{photoId:guid}/thumbnail")]
    [AllowAnonymous]
    [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Client)]
    public async Task<IActionResult> GetPhotoThumbnail(Guid id, Guid photoId, CancellationToken cancellationToken)
    {
        PhotoBytes? photo = await photos.GetThumbnailAsync(id, photoId, cancellationToken);
        return photo is null ? NotFound() : File(photo.Bytes, photo.ContentType);
    }

    [HttpGet("{id:guid}/photos/{photoId:guid}/original")]
    [AllowAnonymous]
    [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Client)]
    public async Task<IActionResult> GetPhotoOriginal(Guid id, Guid photoId, CancellationToken cancellationToken)
    {
        PhotoBytes? photo = await photos.GetOriginalAsync(id, photoId, cancellationToken);
        return photo is null ? NotFound() : File(photo.Bytes, photo.ContentType);
    }

    [HttpDelete("{id:guid}/photos/{photoId:guid}")]
    public async Task<ActionResult<InventoryPhotoUploadResultDto>> DeletePhotoById(
        Guid id,
        Guid photoId,
        CancellationToken cancellationToken)
    {
        AppResult<InventoryPhotoUploadResultDto> result = await photos.DeleteByIdAsync(id, photoId, cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<InventoryItemDto>>> List(
        [FromQuery] InventoryStatus? status,
        [FromQuery] InventoryLifecycleGroup? lifecycle,
        [FromQuery] bool includeArchived,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<InventoryItemDto> items = await queries.ListAsync(status, lifecycle, includeArchived, cancellationToken);
        return items.ToArray();
    }

    [HttpGet("browse")]
    public async Task<ActionResult<InventoryListDto>> Browse(
        [FromQuery] InventoryStatus? status,
        [FromQuery] InventoryLifecycleGroup? lifecycle,
        [FromQuery] bool includeArchived,
        [FromQuery] string? search,
        [FromQuery] InventoryAttention? attention,
        [FromQuery] string? sort,
        [FromQuery] bool desc = true,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] InventoryOwnershipType? ownership = null,
        [FromQuery] bool needsBenchWork = false,
        CancellationToken cancellationToken = default)
    {
        InventoryListDto result = await queries.BrowseAsync(
            status, lifecycle, includeArchived, search, attention, sort, desc, page, pageSize, ownership, needsBenchWork, cancellationToken);
        return result;
    }

    [HttpGet("{id:guid}/issues")]
    public async Task<ActionResult<IReadOnlyList<InventoryIssueDto>>> ListIssues(Guid id, CancellationToken cancellationToken)
    {
        AppResult<IReadOnlyList<InventoryIssueDto>> result = await issues.ListAsync(id, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("{id:guid}/issues")]
    public async Task<ActionResult<InventoryIssueDto>> CreateIssue(
        Guid id,
        UpsertInventoryIssueRequest request,
        CancellationToken cancellationToken)
    {
        AppResult<InventoryIssueDto> result = await issues.CreateAsync(id, request, cancellationToken);
        return ToActionResult(result, value => CreatedAtAction(nameof(ListIssues), new { id }, value));
    }

    [HttpPut("{id:guid}/issues/{issueId:guid}")]
    public async Task<ActionResult<InventoryIssueDto>> UpdateIssue(
        Guid id,
        Guid issueId,
        UpsertInventoryIssueRequest request,
        CancellationToken cancellationToken)
    {
        AppResult<InventoryIssueDto> result = await issues.UpdateAsync(id, issueId, request, cancellationToken);
        return ToActionResult(result);
    }

    [HttpDelete("{id:guid}/issues/{issueId:guid}")]
    public async Task<IActionResult> DeleteIssue(Guid id, Guid issueId, CancellationToken cancellationToken)
    {
        AppResult result = await issues.DeleteAsync(id, issueId, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<InventoryItemDto>> ChangeStatus(
        Guid id,
        ChangeInventoryStatusRequest request,
        CancellationToken cancellationToken)
    {
        AppResult<InventoryItemDto> result = await statuses.ChangeStatusAsync(id, request, cancellationToken);
        return ToActionResult(result);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(
        Guid id,
        [FromQuery] bool hard = false,
        CancellationToken cancellationToken = default)
    {
        AppResult result = await deletion.DeleteAsync(id, hard, cancellationToken);
        return ToActionResult(result);
    }


}
