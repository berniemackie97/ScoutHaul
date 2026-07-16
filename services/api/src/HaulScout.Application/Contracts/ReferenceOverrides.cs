using HaulScout.Domain;

namespace HaulScout.Application.Contracts;

// An organization's own reference menu overrides.

// ── Org-controlled reference menus ─────────────────────────────────────────
public sealed record OrgReferenceOverrideDto(
    Guid Id,
    string Kind,
    string Value,
    string? ParentValue,
    string? Label,
    bool IsCustom,
    bool IsDisabled);

public sealed record UpsertReferenceOverrideRequest(
    string Kind,
    string Value,
    string? ParentValue,
    string? Label,
    bool IsCustom,
    bool IsDisabled);
