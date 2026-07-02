using HaulScout.Application.Contracts;
using HaulScout.Application.Services;

namespace HaulScout.Tests;

public class ChannelFeeCalculatorTests
{
    [Fact]
    public void Ebay_standard_profile_estimates_percent_plus_order_fee()
    {
        var profile = SalesChannelCatalog.FindProfile("eBay", "standard-marketplace");

        Assert.NotNull(profile);
        var estimate = ChannelFeeCalculator.Estimate("eBay", profile, 100m);

        Assert.Equal(13.25m, estimate.EstimatedPlatformFees - 0.40m);
        Assert.Equal(13.65m, estimate.EstimatedPlatformFees);
        Assert.Equal(0m, estimate.EstimatedPaymentFees);
    }

    [Fact]
    public void Storefront_payment_profile_estimates_processor_fee()
    {
        var profile = SalesChannelCatalog.FindProfile("Storefront", "stripe-standard");

        Assert.NotNull(profile);
        var estimate = ChannelFeeCalculator.Estimate("Storefront", profile, 100m);

        Assert.Equal(0m, estimate.EstimatedPlatformFees);
        Assert.Equal(3.20m, estimate.EstimatedPaymentFees);
    }

    [Fact]
    public void Tiered_rules_only_charge_the_matching_price_segment()
    {
        var rules = new[]
        {
            new ChannelFeeRuleDto("base", Percent: 10m, AppliesAboveAmount: 0, AppliesUpToAmount: 100m),
            new ChannelFeeRuleDto("marginal", Percent: 5m, AppliesAboveAmount: 100m),
        };

        decimal fee = ChannelFeeCalculator.Estimate(rules, 150m);

        Assert.Equal(12.50m, fee);
    }

    [Fact]
    public void Catalog_publishes_multiple_profiles_for_variable_channels()
    {
        var ebay = SalesChannelCatalog.Channels.Single(channel => channel.Value == "eBay");

        Assert.Contains(ebay.FeeProfiles!, profile => profile.Id == "standard-marketplace");
        Assert.Contains(ebay.FeeProfiles!, profile => profile.Id == "store-subscriber");
        Assert.Contains(ebay.FeeProfiles!, profile => profile.Id == "top-rated-seller");
    }
}
