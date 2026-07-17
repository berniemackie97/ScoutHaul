using Microsoft.AspNetCore.Mvc;
using HaulScout.Api.Authorization;
using HaulScout.Api.Services;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;

namespace HaulScout.Api.Controllers;

/// <summary>
/// Operator-facing store connection. Reads and updates the organization's storefront identity —
/// most importantly the public <c>StoreDomain</c> the storefront resolves the org by. This is the
/// enterprise replacement for setting the domain via SQL: authenticated, permission-checked,
/// org-scoped, validated, and globally unique.
/// </summary>
[ApiController]
[Route("api/store/settings")]
[RequireOrganizationPermission(HaulScoutPermissions.ManageOrganization)]
public sealed class StoreSettingsController(StoreSettingsWorkflowService settings) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<StoreSettingsDto>> Get(CancellationToken cancellationToken) =>
        ToActionResult(await settings.GetAsync(cancellationToken));

    [HttpPut]
    public async Task<ActionResult<StoreSettingsDto>> Update(
        UpdateStoreSettingsRequest request,
        CancellationToken cancellationToken) =>
        ToActionResult(await settings.UpdateAsync(request, cancellationToken));

    private ActionResult<T> ToActionResult<T>(AppResult<T> result) where T : class =>
        result.Status switch
        {
            AppResultStatus.Ok when result.Value is not null => result.Value,
            AppResultStatus.NotFound => result.Message is null ? NotFound() : NotFound(result.Message),
            AppResultStatus.Conflict => Conflict(result.Message),
            AppResultStatus.Validation => BadRequest(result.Message),
            _ => BadRequest(result.Message),
        };
}
