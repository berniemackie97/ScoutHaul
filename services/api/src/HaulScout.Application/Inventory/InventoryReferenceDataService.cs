using HaulScout.Application.Contracts;
using HaulScout.Application.Services;
using HaulScout.Domain.Entities;

namespace HaulScout.Application.Inventory;

public sealed class InventoryReferenceDataService(IInventoryReferenceDataStore referenceData)
{
    public sealed record ControlledValues(
        string? Category,
        string? Brand,
        string? Model,
        string? Color,
        string? Condition,
        string? SizeSystem,
        string? SizeValue,
        string? StorageLocation);

    public async Task<InventoryReferenceDataDto> GetAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<string?> storageValues = await referenceData.ListStorageLocationValuesAsync(cancellationToken);
        IReadOnlyList<OrgReferenceOverride> overrides = await referenceData.ListOverridesAsync(cancellationToken);

        return new InventoryReferenceDataDto(
            ApplyOverrides("Category", InventoryReferenceCatalog.Categories, overrides),
            ApplyOverrides("Brand", InventoryReferenceCatalog.Brands, overrides),
            ApplyOverrides("Model", InventoryReferenceCatalog.Models, overrides),
            ApplyOverrides("Condition", InventoryReferenceCatalog.Conditions, overrides),
            ApplyOverrides("Color", InventoryReferenceCatalog.Colors, overrides),
            ApplyOverrides("SizeSystem", InventoryReferenceCatalog.SizeSystems, overrides),
            ApplyOverrides("Size", InventoryReferenceCatalog.Sizes, overrides),
            ApplyOverrides("StorageLocation", DynamicWithOther(storageValues), overrides),
            ApplyOverrides("Country", InventoryReferenceCatalog.Countries, overrides));
    }

    private static string OverrideKey(string? value, string? parent) =>
        $"{parent?.ToLowerInvariant()}\u001f{value?.ToLowerInvariant()}";

    // Adds an org's custom values for a kind and removes the ones it has disabled. Never mutates the catalog.
    private static InventoryReferenceOptionDto[] ApplyOverrides(
        string kind,
        IReadOnlyList<InventoryReferenceOptionDto> options,
        IReadOnlyList<OrgReferenceOverride> overrides)
    {
        List<OrgReferenceOverride> forKind = overrides.Where(item => string.Equals(item.Kind, kind, StringComparison.OrdinalIgnoreCase)).ToList();
        if (forKind.Count == 0)
        {
            return options as InventoryReferenceOptionDto[] ?? options.ToArray();
        }

        HashSet<string> disabled = forKind.Where(item => item.IsDisabled).Select(item => OverrideKey(item.Value, item.ParentValue)).ToHashSet();
        List<InventoryReferenceOptionDto> result = options.Where(option => !disabled.Contains(OverrideKey(option.Value, option.ParentValue))).ToList();
        foreach (OrgReferenceOverride addition in forKind.Where(item => item.IsCustom && !item.IsDisabled))
        {
            if (result.All(option => OverrideKey(option.Value, option.ParentValue) != OverrideKey(addition.Value, addition.ParentValue)))
            {
                result.Add(new InventoryReferenceOptionDto(addition.Value, addition.Label ?? addition.Value, addition.ParentValue, addition.ParentValue));
            }
        }

        return result.ToArray();
    }

    public async Task<(ControlledValues? Values, string? Error)> ResolveAsync(
        UpsertInventoryItemRequest request,
        CancellationToken cancellationToken)
    {
        InventoryReferenceDataDto data = await GetAsync(cancellationToken);
        string? category = ResolveOrOther(data.Categories, request.Category);
        string? brand = ResolveDependent(data.Brands, request.Brand, category) ?? CleanOperatorValue(request.Brand);
        string? model = ResolveDependent(data.Models, request.Model, brand) ?? CleanOperatorValue(request.Model);
        string? color = ResolveOrOther(data.Colors, request.Color);
        string? condition = ResolveOrOther(data.Conditions, request.Condition);
        string? sizeSystem = ResolveOrOther(data.SizeSystems, request.SizeSystem);
        string? sizeValue = ResolveDependentOrOther(data.Sizes, request.SizeValue, sizeSystem);
        string? storageLocation = ResolveOrClean(data.StorageLocations, request.StorageLocation);
        string? countryOfOrigin = Resolve(data.Countries, request.CountryOfOrigin);

        (string Label, string? Supplied, string? Resolved)[] fields =
        [
            ("Country of origin", request.CountryOfOrigin, countryOfOrigin)
        ];
        (string Label, string? Supplied, string? Resolved) invalid = fields.FirstOrDefault(field => !string.IsNullOrWhiteSpace(field.Supplied) && field.Resolved is null);
        if (invalid != default)
        {
            return (null, $"{invalid.Label} must be selected from the organization's controlled inventory options.");
        }

        return (new ControlledValues(category, brand, model, color, condition, sizeSystem, sizeValue, storageLocation), null);
    }

    private static string? Resolve(IReadOnlyList<InventoryReferenceOptionDto> options, string? supplied)
    {
        if (string.IsNullOrWhiteSpace(supplied)) return null;
        string value = supplied.Trim();
        return options.FirstOrDefault(option => string.Equals(option.Value, value, StringComparison.OrdinalIgnoreCase))?.Value;
    }

    private static string? ResolveOrOther(IReadOnlyList<InventoryReferenceOptionDto> options, string? supplied)
    {
        if (string.IsNullOrWhiteSpace(supplied)) return null;
        string value = supplied.Trim();
        return options.FirstOrDefault(option => string.Equals(option.Value, value, StringComparison.OrdinalIgnoreCase))?.Value
            ?? options.FirstOrDefault(option => string.Equals(option.Value, "Other", StringComparison.OrdinalIgnoreCase))?.Value;
    }

    private static string? ResolveDependent(
        IReadOnlyList<InventoryReferenceOptionDto> options,
        string? supplied,
        string? parent)
    {
        if (string.IsNullOrWhiteSpace(supplied)) return null;
        string value = supplied.Trim();
        return options.FirstOrDefault(option =>
            string.Equals(option.Value, value, StringComparison.OrdinalIgnoreCase)
            && (string.IsNullOrWhiteSpace(option.ParentValue)
                || string.Equals(option.ParentValue, parent, StringComparison.OrdinalIgnoreCase)))?.Value;
    }

    private static string? ResolveDependentOrOther(
        IReadOnlyList<InventoryReferenceOptionDto> options,
        string? supplied,
        string? parent)
    {
        if (string.IsNullOrWhiteSpace(supplied)) return null;
        string value = supplied.Trim();
        return options.FirstOrDefault(option =>
            string.Equals(option.Value, value, StringComparison.OrdinalIgnoreCase)
            && (string.IsNullOrWhiteSpace(option.ParentValue)
                || string.Equals(option.ParentValue, parent, StringComparison.OrdinalIgnoreCase)))?.Value
            ?? options.FirstOrDefault(option =>
                string.Equals(option.Value, "Other", StringComparison.OrdinalIgnoreCase)
                && (string.IsNullOrWhiteSpace(option.ParentValue)
                    || string.Equals(option.ParentValue, parent, StringComparison.OrdinalIgnoreCase)))?.Value
            ?? "Other";
    }

    private static string? ResolveOrClean(IReadOnlyList<InventoryReferenceOptionDto> options, string? supplied)
    {
        if (string.IsNullOrWhiteSpace(supplied)) return null;
        string value = supplied.Trim();
        return options.FirstOrDefault(option => string.Equals(option.Value, value, StringComparison.OrdinalIgnoreCase))?.Value
            ?? NormalizeOperatorValue(value);
    }

    private static string? CleanOperatorValue(string? supplied) =>
        string.IsNullOrWhiteSpace(supplied) ? null : NormalizeOperatorValue(supplied.Trim());

    private static string NormalizeOperatorValue(string value) =>
        string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static InventoryReferenceOptionDto[] DynamicWithOther(IEnumerable<string?> values) =>
        Clean(values)
            .Append("Other")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(value => new InventoryReferenceOptionDto(value, value))
            .ToArray();

    private static IEnumerable<string> Clean(IEnumerable<string?> values) =>
        values
            .Select(Clean)
            .Where(value => value is not null)
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

}
