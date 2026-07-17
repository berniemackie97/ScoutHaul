using Microsoft.AspNetCore.Mvc;
using HaulScout.Api.Authorization;
using HaulScout.Api.Services;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;

namespace HaulScout.Api.Controllers;

[ApiController]
[Route("api/reference-overrides")]
[RequireOrganizationPermission(HaulScoutPermissions.ManageInventory)]
public sealed class ReferenceOverridesController(ReferenceOverrideWorkflowService overrides) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<OrgReferenceOverrideDto>>> List(CancellationToken cancellationToken) =>
        Ok(await overrides.ListAsync(cancellationToken));

    /// <summary>Add a custom option or disable a catalog one (keyed on kind + value + parent).</summary>
    [HttpPost]
    public async Task<ActionResult<OrgReferenceOverrideDto>> Upsert(UpsertReferenceOverrideRequest request, CancellationToken cancellationToken)
    {
        AppResult<OrgReferenceOverrideDto> result = await overrides.UpsertAsync(request, cancellationToken);
        return result.Status switch
        {
            AppResultStatus.Ok when result.Value is not null => result.Value,
            AppResultStatus.NotFound => NotFound(result.Message),
            AppResultStatus.Conflict => Conflict(result.Message),
            _ => BadRequest(result.Message),
        };
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        AppResult result = await overrides.DeleteAsync(id, cancellationToken);
        return result.Status switch
        {
            AppResultStatus.Ok => NoContent(),
            // A message-less NotFound stays a bare 404 — adding a null body would change the response shape.
            AppResultStatus.NotFound => result.Message is null ? NotFound() : NotFound(result.Message),
            AppResultStatus.Conflict => Conflict(result.Message),
            _ => BadRequest(result.Message),
        };
    }
}
