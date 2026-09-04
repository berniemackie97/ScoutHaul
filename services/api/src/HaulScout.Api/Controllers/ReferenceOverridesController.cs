using Microsoft.AspNetCore.Mvc;
using HaulScout.Api.Authorization;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.ReferenceData;
using HaulScout.Application.Security;

namespace HaulScout.Api.Controllers;

[ApiController]
[Route("api/reference-overrides")]
[RequireOrganizationPermission(HaulScoutPermissions.ManageInventory)]
public sealed class ReferenceOverridesController(ReferenceOverrideWorkflowService overrides) : HaulScoutControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<OrgReferenceOverrideDto>>> List(CancellationToken cancellationToken) =>
        Ok(await overrides.ListAsync(cancellationToken));

    /// <summary>Add a custom option or disable a catalog one (keyed on kind + value + parent).</summary>
    [HttpPost]
    public async Task<ActionResult<OrgReferenceOverrideDto>> Upsert(UpsertReferenceOverrideRequest request, CancellationToken cancellationToken)
    {
        AppResult<OrgReferenceOverrideDto> result = await overrides.UpsertAsync(request, cancellationToken);
        return ToActionResult(result);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        AppResult result = await overrides.DeleteAsync(id, cancellationToken);
        return ToActionResult(result);
    }
}
