using Microsoft.EntityFrameworkCore;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Services;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;
using static HaulScout.Api.Helpers.StringHelpers;

namespace HaulScout.Api.Services;

/// <summary>
/// The organization's storefront identity — most importantly the public store domain the storefront resolves the org
/// by. Because that domain is the resolution key it must be unique across every organization, not just this one.
/// </summary>
public sealed class StoreSettingsWorkflowService(HaulScoutDbContext db, ICurrentUserContext currentUser)
{
    public async Task<AppResult<StoreSettingsDto>> GetAsync(CancellationToken cancellationToken)
    {
        Organization? org = await LoadAsync(cancellationToken);
        return org is null ? AppResult<StoreSettingsDto>.NotFound() : AppResult<StoreSettingsDto>.Ok(ToDto(org));
    }

    public async Task<AppResult<StoreSettingsDto>> UpdateAsync(UpdateStoreSettingsRequest request, CancellationToken cancellationToken)
    {
        string? name = TrimToNull(request.Name);
        if (name is null)
        {
            return AppResult<StoreSettingsDto>.Validation("Store name is required.");
        }

        string? domain = null;
        if (TrimToNull(request.StoreDomain) is string rawDomain)
        {
            domain = StoreDomainNormalizer.Normalize(rawDomain);
            if (domain is null)
            {
                return AppResult<StoreSettingsDto>.Validation(
                    "Enter a valid domain like overhaultrading.com — no http://, no path, no spaces.");
            }

            // The domain is the storefront's resolution key, so it must be unique across ALL
            // organizations, not just this one. Bypass the tenant filter for the check.
            bool alreadyConnected = await db.Organizations
                .IgnoreQueryFilters()
                .AnyAsync(o => o.StoreDomain == domain && o.Id != currentUser.OrganizationId, cancellationToken);
            if (alreadyConnected)
            {
                return AppResult<StoreSettingsDto>.Conflict("That domain is already connected to another organization.");
            }
        }

        Organization? org = await LoadAsync(cancellationToken);
        if (org is null)
        {
            return AppResult<StoreSettingsDto>.NotFound();
        }

        org.Name = name;
        org.StoreDomain = domain;
        await db.SaveChangesAsync(cancellationToken);
        return AppResult<StoreSettingsDto>.Ok(ToDto(org));
    }

    private Task<Organization?> LoadAsync(CancellationToken cancellationToken) =>
        db.Organizations.FirstOrDefaultAsync(o => o.Id == currentUser.OrganizationId, cancellationToken);

    private static StoreSettingsDto ToDto(Organization org) =>
        new(org.Name, org.StoreDomain, org.StoreDomain is not null);
}
