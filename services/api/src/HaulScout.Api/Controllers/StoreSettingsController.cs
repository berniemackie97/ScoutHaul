using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HaulScout.Api.Authorization;
using HaulScout.Api.Services;
using HaulScout.Application.Contracts;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;
using static HaulScout.Api.Helpers.StringHelpers;

namespace HaulScout.Api.Controllers;

/// <summary>
/// Operator-facing store connection. Reads and updates the organization's storefront identity —
/// most importantly the public <c>StoreDomain</c> the storefront resolves the org by. This is the
/// enterprise replacement for setting the domain via SQL: authenticated, permission-checked,
/// org-scoped, validated, and globally unique.
/// </summary>
[ApiController]
[Route("api/store/settings")]
[RequireOrganizationPermission(HaulScoutPermissions.ManageOrganization)]
public sealed partial class StoreSettingsController(
    HaulScoutDbContext db,
    ICurrentUserContext currentUser) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<StoreSettingsDto>> Get(CancellationToken cancellationToken)
    {
        Organization? org = await db.Organizations
            .FirstOrDefaultAsync(o => o.Id == currentUser.OrganizationId, cancellationToken);
        return org is null ? NotFound() : ToDto(org);
    }

    [HttpPut]
    public async Task<ActionResult<StoreSettingsDto>> Update(
        UpdateStoreSettingsRequest request,
        CancellationToken cancellationToken)
    {
        string? name = TrimToNull(request.Name);
        if (name is null)
        {
            return BadRequest("Store name is required.");
        }

        string? domain = null;
        if (TrimToNull(request.StoreDomain) is string rawDomain)
        {
            domain = NormalizeStoreDomain(rawDomain);
            if (domain is null)
            {
                return BadRequest("Enter a valid domain like overhaultrading.com — no http://, no path, no spaces.");
            }

            // The domain is the storefront's resolution key, so it must be unique across ALL
            // organizations, not just this one. Bypass the tenant filter for the check.
            bool alreadyConnected = await db.Organizations
                .IgnoreQueryFilters()
                .AnyAsync(o => o.StoreDomain == domain && o.Id != currentUser.OrganizationId, cancellationToken);
            if (alreadyConnected)
            {
                return Conflict("That domain is already connected to another organization.");
            }
        }

        Organization? org = await db.Organizations
            .FirstOrDefaultAsync(o => o.Id == currentUser.OrganizationId, cancellationToken);
        if (org is null)
        {
            return NotFound();
        }

        org.Name = name;
        org.StoreDomain = domain;
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(org);
    }

    private static StoreSettingsDto ToDto(Organization org) =>
        new(org.Name, org.StoreDomain, org.StoreDomain is not null);

    /// <summary>
    /// Normalizes operator input to a bare lowercase host (strips scheme, <c>www.</c>, port, path,
    /// and trailing dot) and validates it as a DNS hostname. Returns null when the input is not a
    /// valid domain.
    /// </summary>
    private static string? NormalizeStoreDomain(string input)
    {
        string value = input.Trim().ToLowerInvariant();
        value = SchemePrefix().Replace(value, "");   // strip http(s)://
        value = value.Split('/')[0];                  // drop any path
        value = value.Split('?')[0].Split('#')[0];    // drop query / fragment
        value = value.Split(':')[0];                  // drop port
        if (value.StartsWith("www.", StringComparison.Ordinal))
        {
            value = value[4..];
        }
        value = value.TrimEnd('.');
        return HostName().IsMatch(value) ? value : null;
    }

    [GeneratedRegex(@"^https?://")]
    private static partial Regex SchemePrefix();

    // RFC-1123-style hostname: dot-separated labels of alphanumerics and hyphens (no leading or
    // trailing hyphen), a TLD of at least two letters, and a total length of at most 253 chars.
    [GeneratedRegex(@"^(?=.{1,253}$)([a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}$")]
    private static partial Regex HostName();
}
