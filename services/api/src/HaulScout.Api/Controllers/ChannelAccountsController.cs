using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HaulScout.Api.Authorization;
using HaulScout.Api.Mapping;
using HaulScout.Api.Services;
using HaulScout.Application.Contracts;
using HaulScout.Application.Services;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;
using static HaulScout.Api.Helpers.StringHelpers;

namespace HaulScout.Api.Controllers;

[ApiController]
[Route("api/channel-accounts")]
[RequireOrganizationPermission(HaulScoutPermissions.ManageListings)]
public sealed class ChannelAccountsController(HaulScoutDbContext db, ICurrentUserContext currentUser) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ChannelAccountDto>>> List(CancellationToken cancellationToken)
    {
        var accounts = await db.ChannelAccounts
            .Include(item => item.Listings)
            .Where(item => item.OrganizationId == currentUser.OrganizationId)
            .OrderByDescending(item => item.IsActive)
            .ThenBy(item => item.DisplayName)
            .ToListAsync(cancellationToken);

        return accounts.Select(item => item.ToDto()).ToList();
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ChannelAccountDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var account = await db.ChannelAccounts
            .Include(item => item.Listings)
            .SingleOrDefaultAsync(item => item.Id == id && item.OrganizationId == currentUser.OrganizationId, cancellationToken);

        return account is null ? NotFound() : account.ToDto();
    }

    [HttpPost]
    public async Task<ActionResult<ChannelAccountDto>> Create(UpsertChannelAccountRequest request, CancellationToken cancellationToken)
    {
        string? validation = Validate(request, out string channel, out string displayName);
        if (validation is not null)
        {
            return BadRequest(validation);
        }

        if (await NameTaken(displayName, excludeId: null, cancellationToken))
        {
            return Conflict($"A channel account named \"{displayName}\" already exists.");
        }

        var account = new ChannelAccount
        {
            OrganizationId = currentUser.OrganizationId,
            Channel = channel,
            DisplayName = displayName,
            IsNativelySynced = IsNative(channel),
            ConnectedAt = DateTimeOffset.UtcNow,
        };
        Apply(request, account, channel);
        db.ChannelAccounts.Add(account);
        await db.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = account.Id }, account.ToDto());
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ChannelAccountDto>> Update(Guid id, UpsertChannelAccountRequest request, CancellationToken cancellationToken)
    {
        string? validation = Validate(request, out string channel, out string displayName);
        if (validation is not null)
        {
            return BadRequest(validation);
        }

        var account = await db.ChannelAccounts
            .Include(item => item.Listings)
            .SingleOrDefaultAsync(item => item.Id == id && item.OrganizationId == currentUser.OrganizationId, cancellationToken);
        if (account is null)
        {
            return NotFound();
        }

        if (await NameTaken(displayName, excludeId: id, cancellationToken))
        {
            return Conflict($"A channel account named \"{displayName}\" already exists.");
        }

        account.Channel = channel;
        account.DisplayName = displayName;
        account.IsNativelySynced = IsNative(channel);
        Apply(request, account, channel);
        await db.SaveChangesAsync(cancellationToken);

        return account.ToDto();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var account = await db.ChannelAccounts
            .Include(item => item.Listings)
            .SingleOrDefaultAsync(item => item.Id == id && item.OrganizationId == currentUser.OrganizationId, cancellationToken);
        if (account is null)
        {
            return NotFound();
        }

        // Don't destroy sync history: an account that still holds listings must be deactivated, not deleted.
        if (account.Listings.Count > 0)
        {
            return Conflict("This channel account still has listings. Deactivate it instead of deleting.");
        }

        db.ChannelAccounts.Remove(account);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private Task<bool> NameTaken(string displayName, Guid? excludeId, CancellationToken cancellationToken) =>
        db.ChannelAccounts.AnyAsync(
            item => item.OrganizationId == currentUser.OrganizationId
                && item.DisplayName == displayName
                && (!excludeId.HasValue || item.Id != excludeId.Value),
            cancellationToken);

    private static bool IsNative(string channel) => string.Equals(channel, "Storefront", StringComparison.OrdinalIgnoreCase);

    private static void Apply(UpsertChannelAccountRequest request, ChannelAccount account, string channel)
    {
        account.ExternalAccountId = TrimToNull(request.ExternalAccountId);
        account.DefaultFeeProfileId = ResolveFeeProfileId(channel, request.DefaultFeeProfileId);
        account.IsActive = request.IsActive;
        account.Notes = TrimToNull(request.Notes);
    }

    private static string? ResolveFeeProfileId(string channel, string? requested)
    {
        string? trimmed = TrimToNull(requested);
        if (trimmed is null)
        {
            return null;
        }

        // Only accept a fee profile that actually belongs to this channel in the catalog.
        var profile = SalesChannelCatalog.FindProfile(channel, trimmed);
        return string.Equals(profile?.Id, trimmed, StringComparison.OrdinalIgnoreCase) ? profile!.Id : null;
    }

    private static string? Validate(UpsertChannelAccountRequest request, out string channel, out string displayName)
    {
        channel = string.Empty;
        displayName = request.DisplayName?.Trim() ?? string.Empty;

        string? canonical = SalesChannelCatalog.CanonicalValue(request.Channel);
        if (canonical is null)
        {
            return "Unknown sales channel.";
        }

        channel = canonical;
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return "A display name is required.";
        }

        return null;
    }
}
