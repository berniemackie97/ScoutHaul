using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Domain.Entities;

namespace HaulScout.Application.ReferenceData;

/// <summary>
/// An organization's own reference options — adding a custom one or disabling a catalog one. Keyed on
/// kind + value + parent, so the same call both creates and edits.
/// </summary>
public sealed class ReferenceOverrideWorkflowService(IReferenceOverrideStore overrides, ICurrentUserContext currentUser)
{
    private static readonly HashSet<string> Kinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "Category", "Brand", "Model", "Condition", "Color", "SizeSystem", "Size", "StorageLocation", "Country",
    };

    public async Task<IReadOnlyList<OrgReferenceOverrideDto>> ListAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<OrgReferenceOverride> rows = await overrides.ListAsync(currentUser.OrganizationId, cancellationToken);
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

        OrgReferenceOverride? row = await overrides.FindByKeyAsync(currentUser.OrganizationId, kind, value, parent, cancellationToken);
        if (row is null)
        {
            row = new OrgReferenceOverride { OrganizationId = currentUser.OrganizationId, Kind = kind, Value = value, ParentValue = parent };
            await overrides.AddAsync(row, cancellationToken);
        }

        row.Label = TrimToNull(request.Label);
        row.IsCustom = request.IsCustom;
        row.IsDisabled = request.IsDisabled;
        await overrides.SaveChangesAsync(cancellationToken);
        return AppResult<OrgReferenceOverrideDto>.Ok(ToDto(row));
    }

    public async Task<AppResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        OrgReferenceOverride? row = await overrides.FindAsync(currentUser.OrganizationId, id, cancellationToken);
        if (row is null)
        {
            return AppResult.NotFound();
        }

        overrides.Remove(row);
        await overrides.SaveChangesAsync(cancellationToken);
        return AppResult.Ok();
    }

    private static OrgReferenceOverrideDto ToDto(OrgReferenceOverride item) =>
        new(item.Id, item.Kind, item.Value, item.ParentValue, item.Label, item.IsCustom, item.IsDisabled);

    private static string? TrimToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
