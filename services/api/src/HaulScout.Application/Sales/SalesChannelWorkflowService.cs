using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Services;

namespace HaulScout.Application.Sales;

/// <summary>
/// Read-side orchestration for sale channel reference data and sale-entry helpers.
/// Controllers should not know how legacy channels, fee profiles, and sale candidates are discovered.
/// </summary>
public sealed class SalesChannelWorkflowService(ISalesChannelStore salesChannels)
{
    public async Task<SalesReferenceDataDto> ReferenceDataAsync(CancellationToken cancellationToken)
    {
        List<string> existing = await ExistingChannelsAsync(cancellationToken);
        List<SaleChannelOptionDto> channels = SalesChannelCatalog.Channels.ToList();
        channels.AddRange(existing
            .Where(value => channels.All(option => !string.Equals(option.Value, value, StringComparison.OrdinalIgnoreCase)))
            .Select(value => new SaleChannelOptionDto(value, $"{value} (legacy — standardize)", "Legacy data", true, [])));
        return new SalesReferenceDataDto(channels);
    }

    public async Task<string?> CanonicalChannelAsync(string? channel, CancellationToken cancellationToken) =>
        SalesChannelCatalog.CanonicalValue(channel, await ExistingChannelsAsync(cancellationToken));

    public async Task<AppResult<ChannelFeeEstimateDto>> EstimateFeesAsync(
        EstimateChannelFeesRequest request,
        CancellationToken cancellationToken)
    {
        string? channel = await CanonicalChannelAsync(request.Platform, cancellationToken);
        if (channel is null)
        {
            return AppResult<ChannelFeeEstimateDto>.Validation("Select a controlled sales channel.");
        }

        if (request.SoldPrice < 0)
        {
            return AppResult<ChannelFeeEstimateDto>.Validation("Sold price cannot be negative.");
        }

        ChannelFeeProfileDto? profile = SalesChannelCatalog.FindProfile(channel, request.FeeProfileId);
        if (profile is null
            || (!string.IsNullOrWhiteSpace(request.FeeProfileId)
                && !string.Equals(profile.Id, request.FeeProfileId.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            return AppResult<ChannelFeeEstimateDto>.Validation("Select a valid fee profile for this sales channel.");
        }

        return AppResult<ChannelFeeEstimateDto>.Ok(ChannelFeeCalculator.Estimate(channel, profile, request.SoldPrice));
    }

    private const int MostCandidates = 500;

    /// <summary>
    /// Items a sale can be recorded against, most recently touched first. A search reaches every one of them; without
    /// it the list stops at <paramref name="take"/>, so a picker must search rather than scroll.
    /// </summary>
    public async Task<IReadOnlyList<SaleCandidateDto>> CandidatesAsync(string? search, int? take, CancellationToken cancellationToken) =>
        await salesChannels.ListSaleCandidatesAsync(
            string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
            Math.Clamp(take ?? MostCandidates, 1, MostCandidates),
            cancellationToken);

    public async Task<List<string>> ExistingChannelsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<string> channels = await salesChannels.ListExistingChannelsAsync(cancellationToken);

        return channels
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
