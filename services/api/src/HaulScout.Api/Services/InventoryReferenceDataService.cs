using HaulScout.Application.Contracts;
using HaulScout.Application.Services;
using HaulScout.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HaulScout.Api.Services;

public sealed class InventoryReferenceDataService(HaulScoutDbContext db, ICurrentUserContext currentUser)
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
        Guid organizationId = currentUser.OrganizationId;
        IQueryable<HaulScout.Domain.Entities.InventoryItem> inventoryQuery = db.InventoryItems
            .AsNoTracking()
            .Where(item => item.OrganizationId == organizationId);

        List<string?> storageValues = await inventoryQuery.Select(item => item.StorageLocation).Distinct().ToListAsync(cancellationToken);

        return new InventoryReferenceDataDto(
            InventoryReferenceCatalog.Categories,
            InventoryReferenceCatalog.Brands,
            InventoryReferenceCatalog.Models,
            InventoryReferenceCatalog.Conditions,
            InventoryReferenceCatalog.Colors,
            InventoryReferenceCatalog.SizeSystems,
            InventoryReferenceCatalog.Sizes,
            DynamicWithOther(storageValues),
            InventoryReferenceCatalog.Countries);
    }

    public async Task<(ControlledValues? Values, string? Error)> ResolveAsync(
        UpsertInventoryItemRequest request,
        CancellationToken cancellationToken)
    {
        InventoryReferenceDataDto data = await GetAsync(cancellationToken);
        string? category = ResolveOrOther(data.Categories, request.Category);
        string? brand = ResolveDependentOrOther(data.Brands, request.Brand, category);
        string? model = ResolveDependentOrOther(data.Models, request.Model, brand);
        string? color = ResolveOrOther(data.Colors, request.Color);
        string? condition = ResolveOrOther(data.Conditions, request.Condition);
        string? sizeSystem = ResolveOrOther(data.SizeSystems, request.SizeSystem);
        string? sizeValue = ResolveDependentOrOther(data.Sizes, request.SizeValue, sizeSystem);
        string? storageLocation = ResolveOrClean(data.StorageLocations, request.StorageLocation);
        string? countryOfOrigin = Resolve(data.Countries, request.CountryOfOrigin);

        var fields = new (string Label, string? Supplied, string? Resolved)[]
        {
            ("Country of origin", request.CountryOfOrigin, countryOfOrigin),
        };
        var invalid = fields.FirstOrDefault(field => !string.IsNullOrWhiteSpace(field.Supplied) && field.Resolved is null);
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
