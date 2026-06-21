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
        var report = BusinessReportCalculator.Build(
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
        var report = BusinessReportCalculator.Build(from, to, [], [], [], [], [], to);

        Assert.Equal("week", report.Granularity);
        Assert.True(report.Series.Count >= 13);
        Assert.All(report.Series, point => Assert.Equal(0, point.GrossSales));
    }

    [Fact]
    public void Build_marks_aged_and_incomplete_inventory_without_fabricating_values()
    {
        var report = BusinessReportCalculator.Build(
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
        DateTimeOffset soldAt) =>
        new(Guid.NewGuid(), Guid.NewGuid(), title, platform, sold, purchase, platformFees, paymentFees, shipping, supplies, other, net, status, soldAt);
}
