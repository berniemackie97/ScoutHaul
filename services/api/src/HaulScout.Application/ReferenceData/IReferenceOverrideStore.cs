using HaulScout.Domain.Entities;

namespace HaulScout.Application.ReferenceData;

public interface IReferenceOverrideStore
{
    Task<IReadOnlyList<OrgReferenceOverride>> ListAsync(Guid organizationId, CancellationToken cancellationToken);

    Task<OrgReferenceOverride?> FindByKeyAsync(
        Guid organizationId,
        string kind,
        string value,
        string? parentValue,
        CancellationToken cancellationToken);

    Task<OrgReferenceOverride?> FindAsync(Guid organizationId, Guid id, CancellationToken cancellationToken);

    Task AddAsync(OrgReferenceOverride row, CancellationToken cancellationToken);

    void Remove(OrgReferenceOverride row);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
