using Microsoft.AspNetCore.Mvc;
using HaulScout.Api.Authorization;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Application.Storefront;
using HaulScout.Domain;

namespace HaulScout.Api.Controllers;

/// <summary>
/// Operator-facing shelf management. Publishing IS listing and unpublishing IS unlisting — the workflow keeps the
/// item and its store listing pointing the same way; presentation (title, price, copy, category) stays editable
/// here after the fact. Public catalog reads are the separate anonymous surface.
/// </summary>
[ApiController]
[Route("api/store")]
[RequireOrganizationPermission(HaulScoutPermissions.ManageListings)]
public sealed class StoreController(StorePublishingWorkflowService publishing) : ControllerBase
{
    [HttpGet("categories")]
    public async Task<ActionResult<IReadOnlyList<StoreCategoryDto>>> ListCategories(CancellationToken cancellationToken) =>
        Ok(await publishing.ListCategoriesAsync(cancellationToken));

    [HttpPost("categories")]
    public async Task<ActionResult<StoreCategoryDto>> CreateCategory(UpsertStoreCategoryRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await publishing.CreateCategoryAsync(request, cancellationToken));

    [HttpGet("listings")]
    public async Task<ActionResult<IReadOnlyList<StoreListingDto>>> ListListings(
        [FromQuery] StoreListingStatus? status,
        CancellationToken cancellationToken) =>
        Ok(await publishing.ListListingsAsync(status, cancellationToken));

    [HttpGet("listings/{id:guid}")]
    public async Task<ActionResult<StoreListingDto>> GetListing(Guid id, CancellationToken cancellationToken) =>
        ToActionResult(await publishing.GetListingAsync(id, cancellationToken));

    [HttpPost("listings")]
    public async Task<ActionResult<StoreListingDto>> Publish(PublishListingRequest request, CancellationToken cancellationToken)
    {
        AppResult<StoreListingDto> result = await publishing.PublishAsync(request, cancellationToken);
        return result.Status == AppResultStatus.Ok && result.Value is not null
            ? CreatedAtAction(nameof(GetListing), new { id = result.Value.Id }, result.Value)
            : ToActionResult(result);
    }

    [HttpPut("listings/{id:guid}")]
    public async Task<ActionResult<StoreListingDto>> UpdateListing(Guid id, UpdateStoreListingRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await publishing.UpdateListingAsync(id, request, cancellationToken));

    [HttpPost("listings/{id:guid}/unpublish")]
    public async Task<ActionResult<StoreListingDto>> Unpublish(Guid id, CancellationToken cancellationToken) =>
        ToActionResult(await publishing.UnpublishAsync(id, cancellationToken));

    /// <summary>The catalog-wide sweep of the shelf rule: backfill for pre-existing rows, repair for drift.</summary>
    [HttpPost("reconcile")]
    public async Task<ActionResult<ReconcileStoreCatalogResultDto>> Reconcile(CancellationToken cancellationToken) =>
        Ok(await publishing.ReconcileAsync(cancellationToken));

    private ActionResult<T> ToActionResult<T>(AppResult<T> result) where T : class =>
        result.Status switch
        {
            AppResultStatus.Ok when result.Value is not null => result.Value,
            AppResultStatus.NotFound => NotFound(result.Message),
            AppResultStatus.Conflict => Conflict(result.Message),
            AppResultStatus.Validation => BadRequest(result.Message),
            _ => BadRequest(result.Message),
        };
}
