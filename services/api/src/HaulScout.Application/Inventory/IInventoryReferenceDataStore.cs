using HaulScout.Domain.Entities;

namespace HaulScout.Application.Inventory;

public interface IInventoryReferenceDataStore
{
    Task<IReadOnlyList<string?>> ListStorageLocationValuesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<OrgReferenceOverride>> ListOverridesAsync(CancellationToken cancellationToken);
}
