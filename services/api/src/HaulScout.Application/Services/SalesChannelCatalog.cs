using HaulScout.Application.Contracts;

namespace HaulScout.Application.Services;

public static class SalesChannelCatalog
{
    private static ChannelFeeRuleDto Percent(string name, decimal percent, string? notes = null) =>
        new(name, Percent: percent, Notes: notes);

    private static ChannelFeeRuleDto Fixed(string name, decimal amount, string? notes = null) =>
        new(name, FixedAmount: amount, Notes: notes);

    private static ChannelFeeRuleDto Tier(string name, decimal percent, decimal above, decimal? upTo, string? notes = null) =>
        new(name, Percent: percent, AppliesAboveAmount: above, AppliesUpToAmount: upTo, Notes: notes);

    private static ChannelFeeProfileDto NoFees(string id = "no-fee", string label = "No platform fee") =>
        new(id, label, "No automatic fee estimate. Enter actual fees manually when they exist.", true, [], []);

    public static readonly SaleChannelOptionDto[] Channels =
    [
        new("Storefront", "Own storefront", "Owned channels", FeeProfiles:
        [
            new(
                "stripe-standard",
                "Stripe online card",
                "Default card-not-present processor estimate. Actual Stripe/processor fees should be reconciled from payout data.",
                true,
                [],
                [Percent("Processing percent", 2.9m), Fixed("Processing fixed", 0.30m)]),
            new(
                "card-present",
                "Card-present POS",
                "Lower in-person card estimate for local POS transactions.",
                false,
                [],
                [Percent("Card-present processing", 2.7m)]),
            NoFees("cash", "Cash / no processor"),
        ]),
        new("Local POS", "Local / in-person POS", "Owned channels", FeeProfiles:
        [
            NoFees("cash", "Cash"),
            new("card-present", "Card-present payment", "In-person card estimate.", true, [], [Percent("Card-present processing", 2.7m)]),
        ]),
        new("eBay", "eBay", "Marketplaces", FeeProfiles:
        [
            new(
                "standard-marketplace",
                "Standard marketplace",
                "General eBay final-value estimate. Category/store/subscription/top-rated adjustments vary; use actual assessed fees for reconciliation.",
                true,
                [Percent("Final value fee", 13.25m), Fixed("Per-order fee", 0.40m)],
                []),
            new(
                "store-subscriber",
                "Store subscriber",
                "Reduced starter profile for an eBay Store account. Adjust with actual marketplace invoice data.",
                false,
                [Percent("Store final value fee", 12.35m), Fixed("Per-order fee", 0.40m)],
                []),
            new(
                "top-rated-seller",
                "Top Rated Seller discount",
                "Reduced estimate for Top Rated Seller/account discounts. Exact rates depend on category and account standing.",
                false,
                [Percent("Discounted final value fee", 11.90m), Fixed("Per-order fee", 0.40m)],
                []),
            new(
                "tiered-high-value",
                "Tiered high-value sale",
                "Example tiered model: base rate up to $7,500 and lower marginal rate above that. Use actual category rates when reconciling.",
                false,
                [Tier("Base final value tier", 13.25m, 0, 7_500m), Tier("High-value marginal tier", 2.35m, 7_500m, null), Fixed("Per-order fee", 0.40m)],
                []),
        ]),
        new("Facebook Marketplace", "Facebook Marketplace", "Marketplaces", FeeProfiles:
        [
            new("shipped-order", "Shipped marketplace order", "Marketplace selling-fee estimate for shipped orders.", true, [Percent("Selling fee", 5m)], []),
            NoFees("local-pickup", "Local pickup / cash"),
        ]),
        new("Mercari", "Mercari", "Marketplaces", FeeProfiles:
        [
            new("standard", "Standard marketplace", "Starter estimate. Reconcile against the actual Mercari order fee breakdown.", true, [Percent("Marketplace fee", 10m)], [Percent("Payment processing", 2.9m), Fixed("Payment fixed", 0.50m)]),
        ]),
        new("Poshmark", "Poshmark", "Marketplaces", FeeProfiles:
        [
            new("standard", "Standard commission", "Simple commission estimate. Actual small-order minimums should be entered from payout data.", true, [Percent("Commission", 20m)], []),
        ]),
        new("Whatnot", "Whatnot", "Marketplaces", FeeProfiles:
        [
            new("standard", "Standard seller", "Starter marketplace + processing estimate.", true, [Percent("Marketplace fee", 8m)], [Percent("Payment processing", 2.9m), Fixed("Payment fixed", 0.30m)]),
        ]),
        new("Etsy", "Etsy", "Marketplaces", FeeProfiles:
        [
            new("standard", "Standard marketplace", "Starter estimate covering transaction and payment processing. Listing/ad/offsite fees still need actual reconciliation.", true, [Percent("Transaction fee", 6.5m)], [Percent("Payment processing", 3m), Fixed("Payment fixed", 0.45m)]),
        ]),
        new("Amazon", "Amazon", "Marketplaces", FeeProfiles:
        [
            new("referral-only", "Referral fee only", "Referral-fee estimate. FBA, storage, closing, and category fees must be reconciled from Amazon reports.", true, [Percent("Referral fee", 15m)], []),
        ]),
        new("OfferUp", "OfferUp", "Marketplaces", FeeProfiles:
        [
            new("shipped-order", "Shipped order", "Starter service-fee estimate for shipped orders.", true, [Percent("Service fee", 12.9m)], []),
            NoFees("local-pickup", "Local pickup / cash"),
        ]),
        new("Craigslist", "Craigslist", "Marketplaces", FeeProfiles: [NoFees()]),
        new("Auction", "Auction house", "Other channels", FeeProfiles:
        [
            NoFees("manual", "Manual buyer/seller premium"),
            new("commission-10", "10% commission", "Common auction/consignment commission estimate.", false, [Percent("Commission", 10m)], []),
            new("commission-20", "20% commission", "Common auction/consignment commission estimate.", false, [Percent("Commission", 20m)], []),
        ]),
        new("Consignment", "Consignment sale", "Other channels", FeeProfiles:
        [
            NoFees("manual", "Manual split"),
            new("split-70-30", "70/30 split", "30% consignment commission estimate.", false, [Percent("Consignment commission", 30m)], []),
            new("split-60-40", "60/40 split", "40% consignment commission estimate.", false, [Percent("Consignment commission", 40m)], []),
        ]),
        new("Wholesale", "Wholesale", "Other channels", FeeProfiles: [NoFees()]),
        new("Other", "Other controlled channel", "Other channels", FeeProfiles: [NoFees("manual", "Manual fees")]),
    ];

    public static string? CanonicalValue(string? value, IEnumerable<string>? legacyValues = null)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string normalized = value.Trim();
        return Channels.FirstOrDefault(channel => string.Equals(channel.Value, normalized, StringComparison.OrdinalIgnoreCase))?.Value
            ?? legacyValues?.FirstOrDefault(existing => string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase));
    }

    public static ChannelFeeProfileDto? FindProfile(string? channelValue, string? feeProfileId)
    {
        if (string.IsNullOrWhiteSpace(channelValue)) return null;
        var channel = Channels.FirstOrDefault(item => string.Equals(item.Value, channelValue.Trim(), StringComparison.OrdinalIgnoreCase));
        if (channel?.FeeProfiles is null || channel.FeeProfiles.Count == 0) return null;
        if (!string.IsNullOrWhiteSpace(feeProfileId))
        {
            var exact = channel.FeeProfiles.FirstOrDefault(item => string.Equals(item.Id, feeProfileId.Trim(), StringComparison.OrdinalIgnoreCase));
            if (exact is not null) return exact;
        }

        return channel.FeeProfiles.FirstOrDefault(item => item.IsDefault) ?? channel.FeeProfiles[0];
    }
}
