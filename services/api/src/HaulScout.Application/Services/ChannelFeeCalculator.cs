using HaulScout.Application.Contracts;

namespace HaulScout.Application.Services;

/// <summary>
/// Estimates marketplace fees from named rule profiles. This is deliberately not the source of truth for
/// profit: actual platform/payment fees are recorded on each sale and can be corrected after payout.
/// </summary>
public static class ChannelFeeCalculator
{
    public static ChannelFeeEstimateDto Estimate(string platform, ChannelFeeProfileDto profile, decimal soldPrice)
    {
        decimal normalizedPrice = Math.Max(0, soldPrice);
        decimal platformFees = Estimate(profile.PlatformRules, normalizedPrice);
        decimal paymentFees = Estimate(profile.PaymentRules, normalizedPrice);

        return new ChannelFeeEstimateDto(
            platform,
            profile.Id,
            profile.Label,
            normalizedPrice,
            platformFees,
            paymentFees,
            Round(platformFees + paymentFees),
            "Estimate only. Record the actual assessed fees from the marketplace/order payout when available.");
    }

    public static decimal Estimate(IReadOnlyList<ChannelFeeRuleDto>? rules, decimal soldPrice)
    {
        if (rules is null || rules.Count == 0) return 0;
        decimal total = 0;
        foreach (var rule in rules)
        {
            total += Estimate(rule, soldPrice);
        }

        return Round(total);
    }

    public static decimal Estimate(ChannelFeeRuleDto rule, decimal soldPrice)
    {
        decimal price = Math.Max(0, soldPrice);
        decimal above = Math.Max(0, rule.AppliesAboveAmount ?? 0);
        decimal upTo = rule.AppliesUpToAmount ?? price;
        decimal basis = Math.Max(0, Math.Min(price, upTo) - above);
        if (basis <= 0 && rule.Percent > 0)
        {
            return 0;
        }

        decimal fee = basis * Math.Max(0, rule.Percent) / 100m + Math.Max(0, rule.FixedAmount);
        if (rule.MinimumFee.HasValue)
        {
            fee = Math.Max(fee, rule.MinimumFee.Value);
        }

        if (rule.MaximumFee.HasValue)
        {
            fee = Math.Min(fee, rule.MaximumFee.Value);
        }

        return Round(fee);
    }

    private static decimal Round(decimal value) => decimal.Round(Math.Max(0, value), 2, MidpointRounding.AwayFromZero);
}
