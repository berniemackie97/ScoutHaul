using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Application.Services;
using HaulScout.Domain.Entities;

namespace HaulScout.Application.Storefront;

/// <summary>
/// The organization's storefront identity — most importantly the public store domain the storefront resolves the org
/// by. Because that domain is the resolution key it must be unique across every organization, not just this one.
/// </summary>
public sealed class StoreSettingsWorkflowService(IStoreSettingsStore stores, ICurrentUserContext currentUser)
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
            bool alreadyConnected = await stores.StoreDomainExistsForAnotherOrganizationAsync(
                domain,
                currentUser.OrganizationId,
                cancellationToken);
            if (alreadyConnected)
            {
                return AppResult<StoreSettingsDto>.Conflict("That domain is already connected to another organization.");
            }
        }

        string? timeZoneId = TrimToNull(request.TimeZoneId);
        if (timeZoneId is not null && !IsIanaZone(timeZoneId))
        {
            return AppResult<StoreSettingsDto>.Validation("Choose a time zone from the list, like America/New_York.");
        }

        Organization? org = await LoadAsync(cancellationToken);
        if (org is null)
        {
            return AppResult<StoreSettingsDto>.NotFound();
        }

        org.Name = name;
        org.StoreDomain = domain;
        org.TimeZoneId = timeZoneId ?? org.TimeZoneId;
        await stores.SaveChangesAsync(cancellationToken);
        return AppResult<StoreSettingsDto>.Ok(ToDto(org));
    }

    private Task<Organization?> LoadAsync(CancellationToken cancellationToken) =>
        stores.FindAsync(currentUser.OrganizationId, cancellationToken);

    private static StoreSettingsDto ToDto(Organization org) =>
        new(org.Name, org.StoreDomain, org.StoreDomain is not null, org.TimeZoneId);

    // IANA ids only ("Region/City" or UTC): the browser, Postgres and reports all speak them; Windows ids would not round-trip.
    private static bool IsIanaZone(string id) =>
        id.Length <= 64
        && (id == "UTC" || id.Contains('/', StringComparison.Ordinal))
        && TimeZoneInfo.TryFindSystemTimeZoneById(id, out _);

    private static string? TrimToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
