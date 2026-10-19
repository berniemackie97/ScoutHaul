using HaulScout.Application.Common;
using HaulScout.Application.Security;
using HaulScout.Application.Services;

namespace HaulScout.Application.Appraisal;

public sealed record ChannelAccountRef(string Channel, string? DefaultFeeProfileId);

public interface IChannelEconomicsStore
{
    Task<IReadOnlyList<ChannelAccountRef>> ListActiveChannelsAsync(Guid organizationId, CancellationToken cancellationToken);

    /// <summary>Realised sales since <paramref name="since"/>, totalled per platform.</summary>
    Task<IReadOnlyDictionary<string, ChannelSalesHistory>> SalesHistoryAsync(
        Guid organizationId, DateTimeOffset since, CancellationToken cancellationToken);
}

/// <summary>Whether an item is worth buying at a price, on the channels the business actually sells on.</summary>
public sealed class BuyCheckService(
    IChannelEconomicsStore store,
    IDealAnalyzer analyzer,
    ICurrentUserContext currentUser,
    TimeProvider clock)
{
    private static readonly TimeSpan HistoryWindow = TimeSpan.FromDays(365);
    private const string FallbackChannel = "eBay";

    public async Task<AppResult<BuyCheckDto>> CheckAsync(BuyCheckRequest request, CancellationToken cancellationToken)
    {
        string? invalid = Validate(request);
        if (invalid is not null)
        {
            return AppResult<BuyCheckDto>.Validation(invalid);
        }

        Guid organizationId = currentUser.OrganizationId;
        IReadOnlyList<ChannelAccountRef> accounts = await store.ListActiveChannelsAsync(organizationId, cancellationToken);
        IReadOnlyDictionary<string, ChannelSalesHistory> history =
            await store.SalesHistoryAsync(organizationId, clock.GetUtcNow() - HistoryWindow, cancellationToken);

        bool noChannels = accounts.Count == 0;
        List<ChannelEconomics> channels = (noChannels ? [new ChannelAccountRef(FallbackChannel, null)] : accounts)
            .DistinctBy(account => account.Channel, StringComparer.OrdinalIgnoreCase)
            .Select(account => new ChannelEconomics(
                account.Channel,
                LabelOf(account.Channel),
                account.DefaultFeeProfileId,
                history.GetValueOrDefault(account.Channel)))
            .ToList();

        BuyCheckDto check = BuyCheckCalculator.Check(request, channels, analyzer);
        return AppResult<BuyCheckDto>.Ok(noChannels
            ? check with { Reasons = [.. check.Reasons, "No sales channels are set up, so this is priced as an eBay sale."] }
            : check);
    }

    private static string LabelOf(string channel) =>
        SalesChannelCatalog.Channels.FirstOrDefault(option => string.Equals(option.Value, channel, StringComparison.OrdinalIgnoreCase))?.Label
        ?? channel;

    private static string? Validate(BuyCheckRequest request)
    {
        if (request.ExpectedSalePrice is <= 0 or > 1_000_000)
        {
            return "Give a sale price between $0.01 and $1,000,000.";
        }

        if (request.AskingPrice < 0 || request.ShippingCost < 0 || request.SuppliesCost < 0 || request.MinimumProfit < 0)
        {
            return "Prices and costs cannot be negative.";
        }

        return request.TargetMarginPercent is < 0 or >= 100 ? "A target margin must be between 0 and 99%." : null;
    }
}
