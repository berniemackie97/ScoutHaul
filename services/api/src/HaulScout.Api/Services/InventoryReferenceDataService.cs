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
        IQueryable<HaulScout.Domain.Entities.ProductIdentity> productQuery = db.ProductIdentities
            .AsNoTracking()
            .Where(item => item.OrganizationId == organizationId);

        List<string?> inventoryCategories = await inventoryQuery.Select(item => item.Category).Distinct().ToListAsync(cancellationToken);
        List<string?> productCategories = await productQuery.Select(item => item.Category).Distinct().ToListAsync(cancellationToken);
        List<string?> inventoryBrands = await inventoryQuery.Select(item => item.Brand).Distinct().ToListAsync(cancellationToken);
        List<string?> productBrands = await productQuery.Select(item => item.Brand).Distinct().ToListAsync(cancellationToken);
        var inventoryModels = await inventoryQuery.Select(item => new { item.Model, item.Brand }).Distinct().ToListAsync(cancellationToken);
        var productModels = await productQuery.Select(item => new { item.Model, item.Brand }).Distinct().ToListAsync(cancellationToken);
        List<string?> colors = await inventoryQuery.Select(item => item.Color).Distinct().ToListAsync(cancellationToken);
        List<string?> conditions = await inventoryQuery.Select(item => item.Condition).Distinct().ToListAsync(cancellationToken);
        List<string?> sizeSystems = await inventoryQuery.Select(item => item.SizeSystem).Distinct().ToListAsync(cancellationToken);
        var sizes = await inventoryQuery.Select(item => new { item.SizeValue, item.SizeSystem }).Distinct().ToListAsync(cancellationToken);
        List<string?> storageValues = await inventoryQuery.Select(item => item.StorageLocation).Distinct().ToListAsync(cancellationToken);
        List<string?> countries = await inventoryQuery.Select(item => item.CountryOfOrigin).Distinct().ToListAsync(cancellationToken);

        InventoryReferenceOptionDto[] brands = MergeBrands(
            InventoryReferenceCatalog.Brands,
            inventoryBrands.Concat(productBrands));
        InventoryReferenceOptionDto[] models = DynamicWithParent(
            inventoryModels.Select(item => (item.Model, item.Brand))
                .Concat(productModels.Select(item => (item.Model, item.Brand))));
        InventoryReferenceOptionDto[] storageLocations = Dynamic(storageValues);

        return new InventoryReferenceDataDto(
            MergeFixed(InventoryReferenceCatalog.Categories, inventoryCategories.Concat(productCategories)),
            brands,
            models,
            MergeFixed(InventoryReferenceCatalog.Conditions, conditions),
            MergeFixed(InventoryReferenceCatalog.Colors, colors),
            MergeFixed(InventoryReferenceCatalog.SizeSystems, sizeSystems),
            MergeFixedWithParent(InventoryReferenceCatalog.Sizes, sizes.Select(item => (item.SizeValue, item.SizeSystem))),
            storageLocations,
            MergeFixed(InventoryReferenceCatalog.Countries, countries.Where(value => value?.Trim().Length == 2)));
    }

    public async Task<(ControlledValues? Values, string? Error)> ResolveAsync(
        UpsertInventoryItemRequest request,
        CancellationToken cancellationToken)
    {
        InventoryReferenceDataDto data = await GetAsync(cancellationToken);
        string? category = Resolve(data.Categories, request.Category);
        string? brand = Resolve(data.Brands, request.Brand);
        string? model = ResolveDependent(data.Models, request.Model, brand);
        string? color = Resolve(data.Colors, request.Color);
        string? condition = Resolve(data.Conditions, request.Condition);
        string? sizeSystem = Resolve(data.SizeSystems, request.SizeSystem);
        string? sizeValue = ResolveDependent(data.Sizes, request.SizeValue, sizeSystem);
        string? storageLocation = Resolve(data.StorageLocations, request.StorageLocation);
        string? countryOfOrigin = Resolve(data.Countries, request.CountryOfOrigin);

        var fields = new (string Label, string? Supplied, string? Resolved)[]
        {
            ("Category", request.Category, category),
            ("Brand", request.Brand, brand),
            ("Model", request.Model, model),
            ("Color", request.Color, color),
            ("Condition", request.Condition, condition),
            ("Size system", request.SizeSystem, sizeSystem),
            ("Size", request.SizeValue, sizeValue),
            ("Storage location", request.StorageLocation, storageLocation),
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

    private static InventoryReferenceOptionDto[] MergeFixed(
        IReadOnlyList<InventoryReferenceOptionDto> fixedOptions,
        IEnumerable<string?> existingValues)
    {
        var options = fixedOptions.ToList();
        foreach (string value in Clean(existingValues))
        {
            if (options.All(option => !string.Equals(option.Value, value, StringComparison.OrdinalIgnoreCase)))
            {
                options.Add(Legacy(value));
            }
        }
        return options.ToArray();
    }

    private static InventoryReferenceOptionDto[] MergeFixedWithParent(
        IReadOnlyList<InventoryReferenceOptionDto> fixedOptions,
        IEnumerable<(string? Value, string? Parent)> existingValues)
    {
        var options = fixedOptions.ToList();
        foreach ((string? rawValue, string? rawParent) in existingValues)
        {
            string? value = Clean(rawValue);
            if (value is null) continue;
            string? parent = Clean(rawParent);
            if (options.All(option =>
                !string.Equals(option.Value, value, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(option.ParentValue, parent, StringComparison.OrdinalIgnoreCase)))
            {
                options.Add(Legacy(value, parent));
            }
        }
        return options.ToArray();
    }

    // Curated brands (category-scoped via ParentValue) plus any brand already used in this org, added as a
    // universal option (no parent) so it shows under every category and stays resolvable.
    private static InventoryReferenceOptionDto[] MergeBrands(
        IReadOnlyList<InventoryReferenceOptionDto> fixedBrands,
        IEnumerable<string?> existingValues)
    {
        var options = fixedBrands.ToList();
        var known = new HashSet<string>(fixedBrands.Select(option => option.Value), StringComparer.OrdinalIgnoreCase);
        foreach (string value in Clean(existingValues))
        {
            if (known.Add(value))
            {
                options.Add(new InventoryReferenceOptionDto(value, value));
            }
        }
        return options.ToArray();
    }

    private static InventoryReferenceOptionDto[] Dynamic(IEnumerable<string?> values) =>
        Clean(values)
            .Select(value => new InventoryReferenceOptionDto(value, value))
            .ToArray();

    private static InventoryReferenceOptionDto[] DynamicWithParent(IEnumerable<(string? Value, string? Parent)> values) =>
        values
            .Select(item => (Value: Clean(item.Value), Parent: Clean(item.Parent)))
            .Where(item => item.Value is not null)
            .DistinctBy(item => $"{item.Parent?.ToUpperInvariant()}\u001f{item.Value!.ToUpperInvariant()}")
            .OrderBy(item => item.Parent)
            .ThenBy(item => item.Value)
            .Select(item => new InventoryReferenceOptionDto(item.Value!, item.Value!, ParentValue: item.Parent))
            .ToArray();

    private static IEnumerable<string> Clean(IEnumerable<string?> values) =>
        values
            .Select(Clean)
            .Where(value => value is not null)
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static InventoryReferenceOptionDto Legacy(string value, string? parent = null) =>
        new(value, $"{value} (legacy — correct this)", "Legacy data", parent, true);
}
