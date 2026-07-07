using HaulScout.Domain.Common;

namespace HaulScout.Domain.Entities;

/// <summary>
/// Per-organization control over a reference dropdown (categories, brands, storage locations, colors…).
/// A row either adds a custom value (<see cref="IsCustom"/>) or hides a catalog value (<see cref="IsDisabled"/>).
/// Disabling never deletes anything on the backend — existing items keep their value; it's only removed from
/// the menus operators pick from. <see cref="Kind"/> matches the reference kinds ("Brand", "StorageLocation", …).
/// </summary>
public sealed class OrgReferenceOverride : EntityBase, IOrganizationScoped
{
    public Guid OrganizationId { get; set; }
    public Organization? Organization { get; set; }

    public required string Kind { get; set; }
    public required string Value { get; set; }

    /// <summary>Scoping parent for dependent kinds (brand→category, model→brand, size→size system). Null for flat kinds.</summary>
    public string? ParentValue { get; set; }

    public string? Label { get; set; }

    /// <summary>True when the org added this value (it is not in the shared catalog).</summary>
    public bool IsCustom { get; set; }

    /// <summary>True when the value is hidden from this org's menus.</summary>
    public bool IsDisabled { get; set; }

    public int SortOrder { get; set; }
}
