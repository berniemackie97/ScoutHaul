using HaulScout.Api.Services;
using HaulScout.Application.Contracts;
using HaulScout.Application.Services;
using HaulScout.Domain;

namespace HaulScout.Tests;

public sealed class BusinessReportCalculatorTests
{
    private static readonly DateTimeOffset From = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ToExclusive = new(2026, 6, 4, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Build_calculates_auditable_sales_expense_and_inventory_metrics()
    {
        BusinessReportDto report = BusinessReportCalculator.Build(
            From,
            ToExclusive,
            [
                Sale("Camera", "Storefront", 200, 80, 10, 5, 8, 2, 0, 95, SaleStatus.Delivered, From.AddHours(12)),
                Sale("Returned item", "eBay", 100, 40, 8, 3, 4, 0, 0, 45, SaleStatus.Returned, From.AddDays(1)),
            ],
            [Sale("Previous", "Storefront", 100, 30, 5, 3, 2, 0, 0, 60, SaleStatus.Delivered, From.AddDays(-2))],
            [
                new ReportExpenseInput(ExpenseCategory.InventoryPurchase, 75, From),
                new ReportExpenseInput(ExpenseCategory.Fuel, 20, From.AddDays(1)),
            ],
            [new ReportExpenseInput(ExpenseCategory.Fuel, 10, From.AddDays(-2))],
            [new ReportInventoryInput("Cameras", InventoryStatus.ReadyToList, 80, 200, From.AddDays(-20), true, true, false, false)],
            From.AddDays(3));

        Assert.Equal(1, report.Kpis.SalesCount);
        Assert.Equal(1, report.Kpis.ReturnedSalesCount);
        Assert.Equal(200, report.Kpis.GrossSales);
        Assert.Equal(80, report.Kpis.CostOfGoods);
        Assert.Equal(120, report.Kpis.GrossProfit);
        Assert.Equal(25, report.Kpis.SaleCosts);
        Assert.Equal(95, report.Kpis.RealizedNetProfit);
        Assert.Equal(20, report.Kpis.OperatingExpenses);
        Assert.Equal(75, report.Kpis.InventorySpend);
        Assert.Equal(100, report.Comparison.GrossSales.PercentChange);
        Assert.Equal(1, report.Inventory.MissingLocationCount);
        Assert.Equal(1, report.Inventory.MissingPhotoCount);
        Assert.Equal(3, report.Series.Count);
        Assert.Single(report.Platforms);
        Assert.Equal("Storefront", report.Platforms[0].Name);
        Assert.Single(report.TopSales);
    }

    [Fact]
    public void Build_fills_empty_periods_and_uses_weekly_granularity_for_quarter_range()
    {
        DateTimeOffset from = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset to = from.AddDays(90);
        BusinessReportDto report = BusinessReportCalculator.Build(from, to, [], [], [], [], [], to);

        Assert.Equal("week", report.Granularity);
        Assert.True(report.Series.Count >= 13);
        Assert.All(report.Series, point => Assert.Equal(0, point.GrossSales));
    }

    [Fact]
    public void Build_marks_aged_and_incomplete_inventory_without_fabricating_values()
    {
        BusinessReportDto report = BusinessReportCalculator.Build(
            From,
            ToExclusive,
            [], [], [], [],
            [new ReportInventoryInput("", InventoryStatus.NeedsRepair, 0, 0, From.AddDays(-200), false, false, false, false)],
            From.AddDays(3));

        Assert.Equal(1, report.Inventory.StaleCount);
        Assert.Equal(1, report.Inventory.MissingCostCount);
        Assert.Equal(1, report.Inventory.MissingValueCount);
        Assert.Equal(0, report.Inventory.Cost);
        Assert.Equal(1, report.Aging.Single(bucket => bucket.Label == "181+ days").Count);
    }

    [Fact]
    public void Build_tracks_seller_owed_sales_tax_and_net_income_after_operating_expenses()
    {
        BusinessReportDto report = BusinessReportCalculator.Build(
            From,
            ToExclusive,
            [
                // Marketplace remits the tax → pass-through, we owe nothing.
                Sale("Marketplace sale", "eBay", 100, 40, 10, 3, 5, 0, 0, 42, SaleStatus.Delivered, From.AddHours(1), salesTaxCollected: 8, taxRemittedByPlatform: true),
                // We collected the tax on our storefront → we owe it to the state.
                Sale("Storefront sale", "Storefront", 200, 80, 20, 6, 8, 0, 0, 86, SaleStatus.Delivered, From.AddHours(2), salesTaxCollected: 15, taxRemittedByPlatform: false),
            ],
            [],
            [new ReportExpenseInput(ExpenseCategory.Fuel, 30, From.AddHours(3))],
            [],
            [],
            From.AddDays(3));

        Assert.Equal(15, report.Kpis.SalesTaxOwed);            // only the storefront sale is seller-owed
        Assert.Equal(128, report.Kpis.RealizedNetProfit);      // 42 + 86, tax excluded from both
        Assert.Equal(98, report.Kpis.NetIncomeAfterExpenses);  // 128 realized profit − 30 operating expenses
    }

    [Fact]
    public void Days_are_the_business_days_in_its_own_time_zone()
    {
        // 8pm in Los Angeles on June 1 is 3am UTC on June 2. It is June 1's sale.
        TimeZoneInfo la = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
        ReportRange range = ReportRange.Between(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 3), la);
        Assert.Equal(new DateTimeOffset(2026, 6, 1, 7, 0, 0, TimeSpan.Zero), range.From);
        Assert.Equal(TimeSpan.Zero, range.From.Offset);

        BusinessReportDto report = BusinessReportCalculator.Build(
            range.From,
            range.ToExclusive,
            [Sale("Lamp", "eBay", 50, 10, 5, 0, 0, 0, 0, 35, SaleStatus.Delivered, new DateTimeOffset(2026, 6, 2, 3, 0, 0, TimeSpan.Zero))],
            [], [], [], [],
            range.ToExclusive,
            la);

        Assert.Equal(3, report.Series.Count);
        Assert.Equal("Jun 1", report.Series[0].Label);
        Assert.Equal(50, report.Series[0].GrossSales);
        Assert.Equal(new DateOnly(2026, 6, 3), DateOnly.FromDateTime(report.To.DateTime));
        Assert.Equal("America/Los_Angeles", report.TimeZone);
    }

    [Theory]
    [InlineData("7d", "2026-09-28")]
    [InlineData("30d", "2026-09-05")]
    [InlineData("ytd", "2026-01-01")]
    [InlineData("12m", "2025-10-05")]
    public void Presets_end_today_in_the_business_zone(string preset, string expectedStart)
    {
        // 02:00 UTC on Oct 5 is still Oct 4 in Los Angeles.
        TimeZoneInfo la = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
        DateTimeOffset now = new(2026, 10, 5, 2, 0, 0, TimeSpan.Zero);

        Assert.True(BusinessReportService.TryResolveRange(null, null, preset, la, now, out ReportRange range, out _));
        Assert.Equal(DateOnly.Parse(expectedStart), range.FromInclusive);
        Assert.Equal(new DateOnly(2026, 10, 4), range.ToInclusive);
        Assert.False(BusinessReportService.TryResolveRange(null, null, "forever", la, now, out _, out _));
    }

    [Fact]
    public void The_cost_breakdown_reconciles_gross_sales_to_net_profit()
    {
        // The partly refunded sale's net is $20 lower than its costs explain; the residual is the refund.
        BusinessReportDto report = BusinessReportCalculator.Build(
            From,
            ToExclusive,
            [
                Sale("Camera", "eBay", 200, 80, 26, 0, 12, 3, 1, 88, SaleStatus.Delivered, From.AddHours(5), shippingCharged: 10),
                Sale("Lens", "Depop", 60, 20, 6, 2, 0, 0, 0, 12, SaleStatus.PartiallyRefunded, From.AddHours(6)),
            ],
            [], [], [], [], ToExclusive);

        ReportKpiDto kpis = report.Kpis;
        Assert.Equal(260, kpis.ItemSales);
        Assert.Equal(10, kpis.BuyerShipping);
        Assert.Equal(kpis.GrossSales, kpis.ItemSales + kpis.BuyerShipping);
        Assert.Equal(20, kpis.RefundsAndAdjustments);
        Assert.Equal(
            kpis.RealizedNetProfit,
            kpis.GrossSales - kpis.CostOfGoods - kpis.PlatformFees - kpis.PaymentFees - kpis.ShippingCosts - kpis.SuppliesAndOther - kpis.RefundsAndAdjustments);
        Assert.NotNull(report.PreviousKpis);
    }

    [Fact]
    public void Sales_that_lost_money_are_listed_worst_first()
    {
        BusinessReportDto report = BusinessReportCalculator.Build(
            From,
            ToExclusive,
            [
                Sale("Small loss", "eBay", 10, 12, 1, 0, 0, 0, 0, -3, SaleStatus.Delivered, From.AddHours(1)),
                Sale("Big loss", "eBay", 10, 25, 1, 0, 0, 0, 0, -16, SaleStatus.Delivered, From.AddHours(2)),
                Sale("Winner", "eBay", 90, 10, 9, 0, 0, 0, 0, 71, SaleStatus.Delivered, From.AddHours(3)),
            ],
            [], [], [], [], ToExclusive);

        Assert.Equal(["Big loss", "Small loss"], report.LosingSales!.Select(sale => sale.Title));
    }

    private static ReportSaleInput Sale(
        string title,
        string platform,
        decimal sold,
        decimal purchase,
        decimal platformFees,
        decimal paymentFees,
        decimal shipping,
        decimal supplies,
        decimal other,
        decimal net,
        SaleStatus status,
        DateTimeOffset soldAt,
        decimal salesTaxCollected = 0,
        bool taxRemittedByPlatform = true,
        decimal shippingCharged = 0) =>
        new(Guid.NewGuid(), Guid.NewGuid(), title, platform, sold, shippingCharged, purchase, platformFees, paymentFees, shipping, supplies, other, net, status, soldAt, salesTaxCollected, taxRemittedByPlatform);
}
