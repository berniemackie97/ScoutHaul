using Microsoft.EntityFrameworkCore;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Mapping;
using HaulScout.Application.Services;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Infrastructure.Channels;

public sealed class EfChannelAccountService(HaulScoutDbContext db) : IChannelAccountService
{
    public async Task<IReadOnlyList<ChannelAccountDto>> ListAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        List<ChannelAccount> accounts = await Query(organizationId)
            .OrderByDescending(item => item.IsActive)
            .ThenBy(item => item.DisplayName)
            .ToListAsync(cancellationToken);

        return accounts.Select(item => item.ToDto()).ToList();
    }

    public async Task<AppResult<ChannelAccountDto>> GetAsync(Guid organizationId, Guid id, CancellationToken cancellationToken)
    {
        ChannelAccount? account = await Query(organizationId)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        return account is null
            ? AppResult<ChannelAccountDto>.NotFound()
            : AppResult<ChannelAccountDto>.Ok(account.ToDto());
    }

    public async Task<AppResult<ChannelAccountDto>> CreateAsync(Guid organizationId, UpsertChannelAccountRequest request, CancellationToken cancellationToken)
    {
        string? validation = Validate(request, out string channel, out string displayName);
        if (validation is not null)
        {
            return AppResult<ChannelAccountDto>.Validation(validation);
        }

        if (await NameTaken(organizationId, displayName, excludeId: null, cancellationToken))
        {
            return AppResult<ChannelAccountDto>.Conflict($"A channel account named \"{displayName}\" already exists.");
        }

        ChannelAccount account = new()
        {
            OrganizationId = organizationId,
            Channel = channel,
            DisplayName = displayName,
            IsNativelySynced = IsNative(channel),
            ConnectedAt = DateTimeOffset.UtcNow,
        };

        Apply(request, account, channel);
        db.ChannelAccounts.Add(account);
        await db.SaveChangesAsync(cancellationToken);

        return AppResult<ChannelAccountDto>.Ok(account.ToDto());
    }

    public async Task<AppResult<ChannelAccountDto>> UpdateAsync(Guid organizationId, Guid id, UpsertChannelAccountRequest request, CancellationToken cancellationToken)
    {
        string? validation = Validate(request, out string channel, out string displayName);
        if (validation is not null)
        {
            return AppResult<ChannelAccountDto>.Validation(validation);
        }

        ChannelAccount? account = await Query(organizationId)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (account is null)
        {
            return AppResult<ChannelAccountDto>.NotFound();
        }

        if (await NameTaken(organizationId, displayName, excludeId: id, cancellationToken))
        {
            return AppResult<ChannelAccountDto>.Conflict($"A channel account named \"{displayName}\" already exists.");
        }

        account.Channel = channel;
        account.DisplayName = displayName;
        account.IsNativelySynced = IsNative(channel);
        Apply(request, account, channel);
        await db.SaveChangesAsync(cancellationToken);

        return AppResult<ChannelAccountDto>.Ok(account.ToDto());
    }

    public async Task<AppResult> DeleteAsync(Guid organizationId, Guid id, CancellationToken cancellationToken)
    {
        ChannelAccount? account = await Query(organizationId)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (account is null)
        {
            return AppResult.NotFound();
        }

        if (account.Listings.Count > 0)
        {
            return AppResult.Conflict("This channel account still has listings. Deactivate it instead of deleting.");
        }

        db.ChannelAccounts.Remove(account);
        await db.SaveChangesAsync(cancellationToken);
        return AppResult.Ok();
    }

    private IQueryable<ChannelAccount> Query(Guid organizationId) =>
        db.ChannelAccounts
            .Include(item => item.Listings)
            .Where(item => item.OrganizationId == organizationId);

    private Task<bool> NameTaken(Guid organizationId, string displayName, Guid? excludeId, CancellationToken cancellationToken) =>
        db.ChannelAccounts.AnyAsync(
            item => item.OrganizationId == organizationId
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

        ChannelFeeProfileDto? profile = SalesChannelCatalog.FindProfile(channel, trimmed);
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

    private static string? TrimToNull(string? value)
    {
        string? trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
