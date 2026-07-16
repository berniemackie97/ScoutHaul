using Microsoft.EntityFrameworkCore;
using static HaulScout.Api.Helpers.StringHelpers;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Api.Services;

/// <summary>
/// An organization's own reference options — adding a custom one or disabling a catalog one. Keyed on
/// kind + value + parent, so the same call both creates and edits.
/// </summary>
public sealed class ReferenceOverrideWorkflowService(HaulScoutDbContext db, ICurrentUserContext currentUser)
{
    private static readonly HashSet<string> Kinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "Category", "Brand", "Model", "Condition", "Color", "SizeSystem", "Size", "StorageLocation", "Country",
    };

    public async Task<IReadOnlyList<OrgReferenceOverrideDto>> ListAsync(CancellationToken cancellationToken)
    {
        List<OrgReferenceOverride> rows = await db.OrgReferenceOverrides
            .Where(item => item.OrganizationId == currentUser.OrganizationId)
            .OrderBy(item => item.Kind).ThenBy(item => item.Value)
            .ToListAsync(cancellationToken);
        return rows.Select(ToDto).ToList();
    }

    /// <summary>Add a custom option or disable a catalog one (keyed on kind + value + parent).</summary>
    public async Task<AppResult<OrgReferenceOverrideDto>> UpsertAsync(UpsertReferenceOverrideRequest request, CancellationToken cancellationToken)
    {
        string? kind = Kinds.Contains(request.Kind?.Trim() ?? "") ? request.Kind!.Trim() : null;
        string? value = TrimToNull(request.Value);
        if (kind is null)
        {
            return AppResult<OrgReferenceOverrideDto>.Validation("Unknown reference kind.");
        }
        if (value is null)
        {
            return AppResult<OrgReferenceOverrideDto>.Validation("A value is required.");
        }
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
        return AppResult<OrgReferenceOverrideDto>.Ok(ToDto(row));
    }

    public async Task<AppResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        OrgReferenceOverride? row = await db.OrgReferenceOverrides.SingleOrDefaultAsync(
            item => item.Id == id && item.OrganizationId == currentUser.OrganizationId, cancellationToken);
        if (row is null)
        {
            return AppResult.NotFound();
        }

        db.OrgReferenceOverrides.Remove(row);
        await db.SaveChangesAsync(cancellationToken);
        return AppResult.Ok();
    }

    private static OrgReferenceOverrideDto ToDto(OrgReferenceOverride item) =>
        new(item.Id, item.Kind, item.Value, item.ParentValue, item.Label, item.IsCustom, item.IsDisabled);
}
