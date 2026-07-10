using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HaulScout.Api.Authorization;
using HaulScout.Api.Services;
using HaulScout.Application.Contracts;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;
using static HaulScout.Api.Helpers.StringHelpers;

namespace HaulScout.Api.Controllers;

[ApiController]
[Route("api/reference-overrides")]
[RequireOrganizationPermission(HaulScoutPermissions.ManageInventory)]
public sealed class ReferenceOverridesController(HaulScoutDbContext db, ICurrentUserContext currentUser) : ControllerBase
{
    private static readonly HashSet<string> Kinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "Category", "Brand", "Model", "Condition", "Color", "SizeSystem", "Size", "StorageLocation", "Country",
    };

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<OrgReferenceOverrideDto>>> List(CancellationToken cancellationToken)
    {
        List<OrgReferenceOverride> rows = await db.OrgReferenceOverrides
            .Where(item => item.OrganizationId == currentUser.OrganizationId)
            .OrderBy(item => item.Kind).ThenBy(item => item.Value)
            .ToListAsync(cancellationToken);
        return rows.Select(ToDto).ToList();
    }

    /// <summary>Add a custom option or disable a catalog one (keyed on kind + value + parent).</summary>
    [HttpPost]
    public async Task<ActionResult<OrgReferenceOverrideDto>> Upsert(UpsertReferenceOverrideRequest request, CancellationToken cancellationToken)
    {
        string? kind = Kinds.Contains(request.Kind?.Trim() ?? "") ? request.Kind!.Trim() : null;
        string? value = TrimToNull(request.Value);
        if (kind is null) return BadRequest("Unknown reference kind.");
        if (value is null) return BadRequest("A value is required.");
        string? parent = TrimToNull(request.ParentValue);

        OrgReferenceOverride? row = await db.OrgReferenceOverrides.SingleOrDefaultAsync(
            item => item.OrganizationId == currentUser.OrganizationId
                && item.Kind == kind
                && item.Value == value
                && item.ParentValue == parent,
            cancellationToken);
        if (row is null)
        {
            row = new OrgReferenceOverride { OrganizationId = currentUser.OrganizationId, Kind = kind, Value = value, ParentValue = parent };
            db.OrgReferenceOverrides.Add(row);
        }

        row.Label = TrimToNull(request.Label);
        row.IsCustom = request.IsCustom;
        row.IsDisabled = request.IsDisabled;
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(row);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        OrgReferenceOverride? row = await db.OrgReferenceOverrides.SingleOrDefaultAsync(
            item => item.Id == id && item.OrganizationId == currentUser.OrganizationId, cancellationToken);
        if (row is null) return NotFound();
        db.OrgReferenceOverrides.Remove(row);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private static OrgReferenceOverrideDto ToDto(OrgReferenceOverride item) =>
        new(item.Id, item.Kind, item.Value, item.ParentValue, item.Label, item.IsCustom, item.IsDisabled);
}
