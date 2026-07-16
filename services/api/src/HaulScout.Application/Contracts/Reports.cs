using HaulScout.Domain;

namespace HaulScout.Application.Contracts;

// Dashboard and business reporting.

public sealed record DashboardSummaryDto(
    int CurrentInventoryCount,
    int ActiveListingsCount,
    decimal TotalInventoryCost,
    decimal EstimatedInventoryValue,
    decimal GrossSales,
    decimal NetProfit,
    decimal CashDeployed,
    int UnlistedInventoryCount,
    int StaleInventoryCount);

public sealed record ReportKpiDto(
    int SalesCount,
    int ReturnedSalesCount,
    decimal GrossSales,
    decimal CostOfGoods,
    decimal GrossProfit,
    decimal SaleCosts,
    decimal RealizedNetProfit,
    decimal OperatingExpenses,
    decimal InventorySpend,
    decimal AverageSaleValue,
    decimal NetMarginPercent,
    decimal SalesTaxOwed,
    decimal NetIncomeAfterExpenses);

public sealed record ReportChangeDto(decimal Current, decimal Previous, decimal? PercentChange);
public sealed record ReportComparisonDto(ReportChangeDto GrossSales, ReportChangeDto NetProfit, ReportChangeDto SalesCount, ReportChangeDto AverageSaleValue, ReportChangeDto OperatingExpenses);
public sealed record ReportSeriesPointDto(DateTimeOffset PeriodStart, string Label, decimal GrossSales, decimal NetProfit, decimal OperatingExpenses, decimal InventorySpend, int SalesCount);
public sealed record ReportDimensionDto(string Name, int Count, decimal Amount, decimal SecondaryAmount);
public sealed record ReportAgingBucketDto(string Label, int Count, decimal Cost, decimal EstimatedValue);
public sealed record ReportSaleRowDto(Guid SaleId, Guid InventoryItemId, string Title, string Platform, decimal SoldPrice, decimal PurchaseCost, decimal NetProfit, decimal MarginPercent, DateTimeOffset SoldAt);
public sealed record ReportInventorySnapshotDto(int ItemCount, decimal Cost, decimal EstimatedValue, int UnlistedCount, int StaleCount, int MissingCostCount, int MissingValueCount, int MissingLocationCount, int MissingPhotoCount);
public sealed record BusinessReportDto(
    DateTimeOffset From,
    DateTimeOffset To,
    string Granularity,
    DateTimeOffset GeneratedAt,
    ReportKpiDto Kpis,
    ReportComparisonDto Comparison,
    ReportInventorySnapshotDto Inventory,
    IReadOnlyList<ReportSeriesPointDto> Series,
    IReadOnlyList<ReportDimensionDto> Platforms,
    IReadOnlyList<ReportDimensionDto> ExpenseCategories,
    IReadOnlyList<ReportDimensionDto> InventoryCategories,
    IReadOnlyList<ReportDimensionDto> InventoryStatuses,
    IReadOnlyList<ReportAgingBucketDto> Aging,
    IReadOnlyList<ReportSaleRowDto> TopSales);
