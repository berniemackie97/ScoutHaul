using Microsoft.Extensions.Time.Testing;
using HaulScout.Application.Appraisal;
using HaulScout.Application.Common;
using HaulScout.Application.Security;
using HaulScout.Application.Services;
using HaulScout.Domain;

namespace HaulScout.Tests;

public sealed class BuyCheckTests
{
    private static readonly ChannelEconomics EbayWithHistory = new(
        "eBay", "eBay", "store-subscriber",
        new ChannelSalesHistory(Sales: 10, SoldTotal: 1000m, FeeTotal: 120m, ShippingSamples: 10, NetShippingTotal: -5m, SuppliesTotal: 0m));

    private static readonly ChannelEconomics PoshmarkNew = new("Poshmark", "Poshmark", null, null);

    [Fact]
    public void A_channel_with_enough_sales_uses_its_own_fee_rate_and_its_buyers_paid_shipping()
    {
        BuyCheckDto check = Check(new BuyCheckRequest(100m), EbayWithHistory);

        Assert.Equal(12m, check.Channel.Fees);
        Assert.Equal("12% from your last 10 eBay sales", check.Channel.FeesFrom);
        Assert.Equal(0m, check.Channel.Shipping);
        Assert.Equal("Buyers paid shipping on your eBay sales", check.Channel.ShippingFrom);
        Assert.Equal(88m, check.Channel.NetProceeds);
        Assert.Equal(68m, check.MaxBuyPrice);
    }

    [Fact]
    public void A_channel_without_history_uses_its_fee_profile()
    {
        BuyCheckDto check = Check(new BuyCheckRequest(100m, Channel: "Poshmark"), EbayWithHistory, PoshmarkNew);

        decimal expected = ChannelFeeCalculator.Estimate("Poshmark", SalesChannelCatalog.FindProfile("Poshmark", null)!, 100m).EstimatedTotalFees;
        Assert.Equal("Poshmark", check.Channel.Channel);
        Assert.Equal(expected, check.Channel.Fees);
        Assert.EndsWith("rates", check.Channel.FeesFrom, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_a_chosen_channel_it_prices_where_the_business_sells_most_not_where_fees_are_lowest()
    {
        ChannelEconomics vintedNoFees = new("Vinted", "Vinted", "seller-free", new ChannelSalesHistory(2, 40m, 0m, 0, 0m, 0m));

        BuyCheckDto check = Check(new BuyCheckRequest(100m), vintedNoFees, PoshmarkNew, EbayWithHistory);

        Assert.Equal("eBay", check.Channel.Channel);
        Assert.Equal(["Vinted", "eBay", "Poshmark"], check.Channels.Select(channel => channel.Channel).ToArray());
    }

    [Fact]
    public void With_no_sales_anywhere_the_best_net_is_the_default()
    {
        BuyCheckDto check = Check(new BuyCheckRequest(100m), PoshmarkNew, new ChannelEconomics("Depop", "Depop", null, null));

        Assert.Equal(check.Channels[0].Channel, check.Channel.Channel);
    }

    [Theory]
    [InlineData(40, RecommendationDecisionType.Buy, "At $40.00 you clear $48.00 selling on eBay.")]
    [InlineData(75, RecommendationDecisionType.Negotiate, "At $75.00 you clear $13.00. Offer $68.00 or less.")]
    [InlineData(95, RecommendationDecisionType.Pass, "At $95.00 you lose $7.00 after fees.")]
    public void The_asking_price_decides_and_says_why(int asking, RecommendationDecisionType decision, string reason)
    {
        BuyCheckDto check = Check(new BuyCheckRequest(100m, AskingPrice: asking), EbayWithHistory);

        Assert.Equal(decision, check.Decision);
        Assert.Equal(reason, check.Reasons[0]);
    }

    [Fact]
    public void Without_an_asking_price_it_says_what_to_pay_at_most()
    {
        BuyCheckDto check = Check(new BuyCheckRequest(100m), EbayWithHistory);

        Assert.Equal(RecommendationDecisionType.ResearchRequired, check.Decision);
        Assert.Equal("Pay up to $68.00 to clear your minimum of $20.00.", check.Reasons[0]);
        Assert.Null(check.ProfitAtAsking);
    }

    [Fact]
    public void Entered_shipping_and_a_price_not_backed_by_sales_are_both_respected()
    {
        BuyCheckDto check = Check(new BuyCheckRequest(100m, ShippingCost: 9m, Basis: PriceBasis.Estimate), EbayWithHistory);

        Assert.Equal(9m, check.Channel.Shipping);
        Assert.Equal("As you entered", check.Channel.ShippingFrom);
        Assert.Contains("The sale price is not backed by sales. Check comps before paying much.", check.Reasons);
    }

    [Fact]
    public async Task With_no_channels_set_up_it_prices_an_ebay_sale_and_says_so()
    {
        BuyCheckService service = new(new FakeStore([]), new DealAnalyzer(), new StubUser(), new FakeTimeProvider(DateTimeOffset.UtcNow));

        BuyCheckDto check = (await service.CheckAsync(new BuyCheckRequest(50m), CancellationToken.None)).Value!;

        Assert.Equal("eBay", check.Channel.Channel);
        Assert.Contains("No sales channels are set up, so this is priced as an eBay sale.", check.Reasons);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task A_sale_price_of_nothing_is_refused(int price)
    {
        BuyCheckService service = new(new FakeStore([]), new DealAnalyzer(), new StubUser(), new FakeTimeProvider(DateTimeOffset.UtcNow));

        AppResult<BuyCheckDto> result = await service.CheckAsync(new BuyCheckRequest(price), CancellationToken.None);

        Assert.Equal(AppResultStatus.Validation, result.Status);
    }

    [Fact]
    public async Task Two_accounts_on_one_channel_are_priced_once()
    {
        FakeStore store = new([new ChannelAccountRef("eBay", null), new ChannelAccountRef("ebay", "store-subscriber")]);
        BuyCheckService service = new(store, new DealAnalyzer(), new StubUser(), new FakeTimeProvider(DateTimeOffset.UtcNow));

        BuyCheckDto check = (await service.CheckAsync(new BuyCheckRequest(50m), CancellationToken.None)).Value!;

        Assert.Single(check.Channels);
    }

    private static BuyCheckDto Check(BuyCheckRequest request, params ChannelEconomics[] channels) =>
        BuyCheckCalculator.Check(request, channels, new DealAnalyzer());

    private sealed class FakeStore(IReadOnlyList<ChannelAccountRef> accounts) : IChannelEconomicsStore
    {
        public Task<IReadOnlyList<ChannelAccountRef>> ListActiveChannelsAsync(Guid organizationId, CancellationToken cancellationToken) =>
            Task.FromResult(accounts);

        public Task<IReadOnlyDictionary<string, ChannelSalesHistory>> SalesHistoryAsync(
            Guid organizationId, DateTimeOffset since, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, ChannelSalesHistory>>(new Dictionary<string, ChannelSalesHistory>());
    }

    private sealed class StubUser : ICurrentUserContext
    {
        public Guid OrganizationId { get; } = Guid.NewGuid();
        public Guid? ActiveOrganizationId => OrganizationId;
        public Guid UserId => Guid.Empty;
        public Task ResolveAsync() => Task.CompletedTask;
    }
}
