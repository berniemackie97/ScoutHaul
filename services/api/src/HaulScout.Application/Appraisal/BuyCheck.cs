using System.Globalization;
using HaulScout.Application.Contracts;
using HaulScout.Application.Services;
using HaulScout.Domain;

namespace HaulScout.Application.Appraisal;

public sealed record BuyCheckRequest(
    decimal ExpectedSalePrice,
    decimal? AskingPrice = null,
    string? Channel = null,
    decimal? ShippingCost = null,
    decimal? SuppliesCost = null,
    decimal MinimumProfit = 20m,
    decimal? TargetMarginPercent = null,
    int? Confidence = null,
    PriceBasis? Basis = null);

public sealed record BuyChannelDto(
    string Channel,
    string Label,
    decimal Fees,
    string FeesFrom,
    decimal Shipping,
    string ShippingFrom,
    decimal Supplies,
    decimal NetProceeds,
    decimal MaxBuyPrice);

public sealed record BuyCheckDto(
    decimal ExpectedSalePrice,
    decimal? AskingPrice,
    BuyChannelDto Channel,
    IReadOnlyList<BuyChannelDto> Channels,
    decimal MaxBuyPrice,
    decimal? ProfitAtAsking,
    decimal? MarginAtAsking,
    RecommendationDecisionType Decision,
    IReadOnlyList<string> Reasons);

/// <summary>A channel the business sells on, with what its own sales there have cost.</summary>
public sealed record ChannelEconomics(string Channel, string Label, string? FeeProfileId, ChannelSalesHistory? History);

public sealed record ChannelSalesHistory(
    int Sales,
    decimal SoldTotal,
    decimal FeeTotal,
    int ShippingSamples,
    decimal NetShippingTotal,
    decimal SuppliesTotal);

/// <summary>
/// What an item nets on each channel and the most to pay for it. A channel's own fee rate is used
/// once the business has enough sales there to know it; until then, the channel's fee profile.
/// </summary>
public static class BuyCheckCalculator
{
    public const int HistoryNeeded = 5;

    public static BuyCheckDto Check(BuyCheckRequest request, IReadOnlyList<ChannelEconomics> channels, IDealAnalyzer analyzer)
    {
        decimal price = request.ExpectedSalePrice;
        List<(BuyChannelDto Channel, DealAnalysisResult Analysis)> priced = channels
            .Select(channel => Price(request, channel, analyzer))
            .ToList();

        // Without a choice, price it where the business actually sells: a channel with no fees but
        // cheaper buyers would otherwise always look best at the same sale price.
        (BuyChannelDto chosen, DealAnalysisResult analysis) = priced
            .FirstOrDefault(entry => string.Equals(entry.Channel.Channel, request.Channel, StringComparison.OrdinalIgnoreCase));
        if (chosen is null)
        {
            (chosen, analysis) = priced
                .OrderByDescending(entry => SalesOn(channels, entry.Channel.Channel))
                .ThenByDescending(entry => entry.Channel.NetProceeds)
                .First();
        }

        decimal? profit = request.AskingPrice is { } asking ? Round(chosen.NetProceeds - asking) : null;
        decimal? margin = profit is { } made && price > 0 ? Math.Round(made / price * 100, 1) : null;
        RecommendationDecisionType decision = request.AskingPrice is null ? RecommendationDecisionType.ResearchRequired : analysis.Decision;

        return new BuyCheckDto(
            price,
            request.AskingPrice,
            chosen,
            priced.Select(entry => entry.Channel).OrderByDescending(channel => channel.NetProceeds).ToArray(),
            chosen.MaxBuyPrice,
            profit,
            margin,
            decision,
            Reasons(request, chosen, profit, decision));
    }

    private static int SalesOn(IReadOnlyList<ChannelEconomics> channels, string channel) =>
        channels.FirstOrDefault(economics => economics.Channel == channel)?.History?.Sales ?? 0;

    private static (BuyChannelDto Channel, DealAnalysisResult Analysis) Price(BuyCheckRequest request, ChannelEconomics channel, IDealAnalyzer analyzer)
    {
        decimal price = request.ExpectedSalePrice;
        ChannelSalesHistory? history = channel.History is { Sales: >= HistoryNeeded, SoldTotal: > 0 } known ? known : null;

        (decimal fees, string feesFrom) = history is not null
            ? (price * history.FeeTotal / history.SoldTotal,
                $"{Percent(history.FeeTotal / history.SoldTotal)} from your last {history.Sales} {channel.Label} sales")
            : ProfileFees(channel, price);

        (decimal shipping, string shippingFrom) = Shipping(request, channel);

        decimal supplies = request.SuppliesCost
            ?? (channel.History is { Sales: > 0 } withSupplies ? withSupplies.SuppliesTotal / withSupplies.Sales : 0m);

        DealAnalysisResult analysis = analyzer.Analyze(new DealAnalysisInput(
            ExpectedSalePrice: price,
            SellerAskingPrice: request.AskingPrice,
            PurchasePrice: null,
            Platform: channel.Channel,
            PlatformFeePercent: price > 0 ? fees / price * 100 : 0,
            PaymentFeePercent: 0,
            FixedPaymentFee: 0,
            EstimatedShippingCostPaidBySeller: shipping,
            SuppliesCost: supplies,
            RiskReserve: 0,
            DesiredMinimumProfit: request.MinimumProfit,
            TargetMarginPercent: request.TargetMarginPercent,
            ConfidenceScore: request.Confidence,
            PricingBasis: BasisOf(request.Basis)));

        return (new BuyChannelDto(
            channel.Channel,
            channel.Label,
            Round(fees),
            feesFrom,
            Round(shipping),
            shippingFrom,
            Round(supplies),
            Round(price - fees - shipping - supplies),
            analysis.RecommendedMaxBuyPrice ?? 0), analysis);
    }

    // What the seller pays to ship beyond what the buyer paid for shipping.
    private static (decimal Shipping, string From) Shipping(BuyCheckRequest request, ChannelEconomics channel)
    {
        if (request.ShippingCost is { } entered)
        {
            return (entered, "As you entered");
        }

        if (channel.History is not { ShippingSamples: >= HistoryNeeded } history)
        {
            return (0, "Not included");
        }

        decimal net = Math.Max(0, history.NetShippingTotal / history.ShippingSamples);
        return net < 0.5m
            ? (0, $"Buyers paid shipping on your {channel.Label} sales")
            : (net, $"Average on your last {history.ShippingSamples} {channel.Label} sales");
    }

    private static (decimal Fees, string From) ProfileFees(ChannelEconomics channel, decimal price)
    {
        ChannelFeeProfileDto? profile = SalesChannelCatalog.FindProfile(channel.Channel, channel.FeeProfileId);
        if (profile is null)
        {
            return (0, "No fees on this channel");
        }

        decimal fees = ChannelFeeCalculator.Estimate(channel.Channel, profile, price).EstimatedTotalFees;
        return (fees, $"{profile.Label} rates");
    }

    private static IReadOnlyList<string> Reasons(BuyCheckRequest request, BuyChannelDto channel, decimal? profit, RecommendationDecisionType decision)
    {
        List<string> reasons = [];
        if (request.AskingPrice is { } asking && profit is { } made)
        {
            reasons.Add(decision switch
            {
                RecommendationDecisionType.Buy => $"At {Money(asking)} you clear {Money(made)} selling on {channel.Label}.",
                RecommendationDecisionType.Negotiate => $"At {Money(asking)} you clear {Money(made)}. Offer {Money(channel.MaxBuyPrice)} or less.",
                RecommendationDecisionType.Pass => made > 0
                    ? $"At {Money(asking)} you clear only {Money(made)}. It's worth {Money(channel.MaxBuyPrice)} at most."
                    : $"At {Money(asking)} you lose {Money(-made)} after fees.",
                _ => $"At {Money(asking)} you clear {Money(made)}, but the price behind it is not solid enough to decide on.",
            });
        }
        else
        {
            reasons.Add($"Pay up to {Money(channel.MaxBuyPrice)} to clear your minimum of {Money(request.MinimumProfit)}.");
        }

        if (request.Basis is PriceBasis.Estimate or PriceBasis.Asking or PriceBasis.None)
        {
            reasons.Add("The sale price is not backed by sales. Check comps before paying much.");
        }

        return reasons;
    }

    private static PricingBasisType BasisOf(PriceBasis? basis) => basis switch
    {
        PriceBasis.Sales => PricingBasisType.LiveValidated,
        PriceBasis.Guide => PricingBasisType.CachedRecent,
        PriceBasis.None => PricingBasisType.Unavailable,
        _ => PricingBasisType.ManualEstimate,
    };

    private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string Money(decimal value) => value.ToString("$#,0.00", CultureInfo.InvariantCulture);

    private static string Percent(decimal share) => (share * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%";
}
