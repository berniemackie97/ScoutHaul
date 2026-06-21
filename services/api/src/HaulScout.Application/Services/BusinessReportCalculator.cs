using HaulScout.Application.Contracts;
using HaulScout.Domain;

namespace HaulScout.Application.Services;

public sealed record ReportSaleInput(
    Guid Id,
    Guid InventoryItemId,
    string InventoryItemTitle,
    string Platform,
    decimal SoldPrice,
    decimal PurchasePrice,
    decimal PlatformFees,
    decimal PaymentFees,
    decimal ShippingCost,
    decimal SuppliesCost,
    decimal OtherCosts,
    decimal NetProfit,
    SaleStatus Status,
    DateTimeOffset SoldAt);

public sealed record ReportExpenseInput(ExpenseCategory Category, decimal Amount, DateTimeOffset OccurredAt);

public sealed record ReportInventoryInput(
    string Category,
    InventoryStatus Status,
    decimal PurchasePrice,
    decimal EstimatedSalePrice,
    DateTimeOffset AcquiredAt,
    bool HasCost,
    bool HasEstimatedValue,
    bool HasStorageLocation,
    bool HasPhoto);

public static class BusinessReportCalculator
{
    public static BusinessReportDto Build(
        DateTimeOffset from,
        DateTimeOffset toExclusive,
        IReadOnlyList<ReportSaleInput> currentSales,
        IReadOnlyList<ReportSaleInput> previousSales,
        IReadOnlyList<ReportExpenseInput> currentExpenses,
        IReadOnlyList<ReportExpenseInput> previousExpenses,
        IReadOnlyList<ReportInventoryInput> inventory,
        DateTimeOffset generatedAt)
    {
        if (toExclusive <= from)
        {
            throw new ArgumentException("Report end must be after report start.");
        }

        ReportKpiDto current = CalculateKpis(currentSales, currentExpenses);
        ReportKpiDto previous = CalculateKpis(previousSales, previousExpenses);
        string granularity = SelectGranularity(from, toExclusive);

        var salesByPeriod = currentSales
            .Where(sale => SaleWorkflow.IsRealized(sale.Status))
            .GroupBy(sale => BucketStart(sale.SoldAt, granularity))
            .ToDictionary(group => group.Key, group => group.ToArray());
        var expensesByPeriod = currentExpenses
            .GroupBy(expense => BucketStart(expense.OccurredAt, granularity))
            .ToDictionary(group => group.Key, group => group.ToArray());

        var series = new List<ReportSeriesPointDto>();
        for (DateTimeOffset cursor = BucketStart(from, granularity); cursor < toExclusive; cursor = NextBucket(cursor, granularity))
        {
            ReportSaleInput[] sales = salesByPeriod.GetValueOrDefault(cursor) ?? [];
            ReportExpenseInput[] expenses = expensesByPeriod.GetValueOrDefault(cursor) ?? [];
            series.Add(new ReportSeriesPointDto(
                cursor,
                BucketLabel(cursor, granularity),
                sales.Sum(item => item.SoldPrice),
                sales.Sum(item => item.NetProfit),
                expenses.Where(item => item.Category != ExpenseCategory.InventoryPurchase).Sum(item => item.Amount),
                expenses.Where(item => item.Category == ExpenseCategory.InventoryPurchase).Sum(item => item.Amount),
                sales.Length));
        }

        ReportDimensionDto[] platforms = currentSales
            .Where(sale => SaleWorkflow.IsRealized(sale.Status))
            .GroupBy(sale => string.IsNullOrWhiteSpace(sale.Platform) ? "Unspecified" : sale.Platform.Trim())
            .Select(group => new ReportDimensionDto(group.Key, group.Count(), group.Sum(item => item.SoldPrice), group.Sum(item => item.NetProfit)))
            .OrderByDescending(item => item.Amount)
            .ToArray();

        ReportDimensionDto[] expenseCategories = currentExpenses
            .GroupBy(expense => expense.Category.ToString())
            .Select(group => new ReportDimensionDto(group.Key, group.Count(), group.Sum(item => item.Amount), 0))
            .OrderByDescending(item => item.Amount)
            .ToArray();

        ReportDimensionDto[] inventoryCategories = inventory
            .GroupBy(item => string.IsNullOrWhiteSpace(item.Category) ? "Uncategorized" : item.Category.Trim())
            .Select(group => new ReportDimensionDto(group.Key, group.Count(), group.Sum(item => item.PurchasePrice), group.Sum(item => item.EstimatedSalePrice)))
            .OrderByDescending(item => item.SecondaryAmount)
            .ToArray();

        ReportDimensionDto[] inventoryStatuses = inventory
            .GroupBy(item => item.Status.ToString())
            .Select(group => new ReportDimensionDto(group.Key, group.Count(), group.Sum(item => item.PurchasePrice), group.Sum(item => item.EstimatedSalePrice)))
            .OrderByDescending(item => item.Count)
            .ToArray();

        var ageNow = generatedAt;
        ReportAgingBucketDto[] aging =
        [
            Aging("0–30 days", inventory, ageNow, 0, 30),
            Aging("31–60 days", inventory, ageNow, 31, 60),
            Aging("61–90 days", inventory, ageNow, 61, 90),
            Aging("91–180 days", inventory, ageNow, 91, 180),
            Aging("181+ days", inventory, ageNow, 181, int.MaxValue),
        ];

        ReportSaleRowDto[] topSales = currentSales
            .Where(sale => SaleWorkflow.IsRealized(sale.Status))
            .OrderByDescending(sale => sale.NetProfit)
            .Take(10)
            .Select(ToSaleRow)
            .ToArray();

        int staleCount = inventory.Count(item => (ageNow - item.AcquiredAt).TotalDays > 60 && item.Status != InventoryStatus.Listed);
        var inventorySnapshot = new ReportInventorySnapshotDto(
            inventory.Count,
            inventory.Sum(item => item.PurchasePrice),
            inventory.Sum(item => item.EstimatedSalePrice),
            inventory.Count(item => InventoryStatusRules.IsUnlistedInventory(item.Status)),
            staleCount,
            inventory.Count(item => !item.HasCost),
            inventory.Count(item => !item.HasEstimatedValue),
            inventory.Count(item => !item.HasStorageLocation),
            inventory.Count(item => !item.HasPhoto));

        var comparison = new ReportComparisonDto(
            Change(current.GrossSales, previous.GrossSales),
            Change(current.RealizedNetProfit, previous.RealizedNetProfit),
            Change(current.SalesCount, previous.SalesCount),
            Change(current.AverageSaleValue, previous.AverageSaleValue),
            Change(current.OperatingExpenses, previous.OperatingExpenses));

        return new BusinessReportDto(
            from,
            toExclusive.AddTicks(-1),
            granularity,
            generatedAt,
            current,
            comparison,
            inventorySnapshot,
            series,
            platforms,
            expenseCategories,
            inventoryCategories,
            inventoryStatuses,
            aging,
            topSales);
    }

    private static ReportKpiDto CalculateKpis(IReadOnlyList<ReportSaleInput> sales, IReadOnlyList<ReportExpenseInput> expenses)
    {
        ReportSaleInput[] realized = sales.Where(sale => SaleWorkflow.IsRealized(sale.Status)).ToArray();
        decimal grossSales = realized.Sum(item => item.SoldPrice);
        decimal costOfGoods = realized.Sum(item => item.PurchasePrice);
        decimal saleCosts = realized.Sum(item => item.PlatformFees + item.PaymentFees + item.ShippingCost + item.SuppliesCost + item.OtherCosts);
        decimal netProfit = realized.Sum(item => item.NetProfit);
        decimal operatingExpenses = expenses.Where(item => item.Category != ExpenseCategory.InventoryPurchase).Sum(item => item.Amount);
        decimal inventorySpend = expenses.Where(item => item.Category == ExpenseCategory.InventoryPurchase).Sum(item => item.Amount);
        return new ReportKpiDto(
            realized.Length,
            sales.Count(item => item.Status is SaleStatus.Returned or SaleStatus.Refunded),
            grossSales,
            costOfGoods,
            grossSales - costOfGoods,
            saleCosts,
            netProfit,
            operatingExpenses,
            inventorySpend,
            realized.Length == 0 ? 0 : grossSales / realized.Length,
            grossSales == 0 ? 0 : netProfit / grossSales * 100);
    }

    private static ReportSaleRowDto ToSaleRow(ReportSaleInput sale)
    {
        decimal margin = sale.SoldPrice == 0 ? 0 : sale.NetProfit / sale.SoldPrice * 100;
        return new ReportSaleRowDto(sale.Id, sale.InventoryItemId, sale.InventoryItemTitle, sale.Platform, sale.SoldPrice, sale.PurchasePrice, sale.NetProfit, margin, sale.SoldAt);
    }

    private static ReportAgingBucketDto Aging(string label, IReadOnlyList<ReportInventoryInput> inventory, DateTimeOffset now, int minDays, int maxDays)
    {
        ReportInventoryInput[] items = inventory.Where(item =>
        {
            int days = Math.Max(0, (int)(now - item.AcquiredAt).TotalDays);
            return days >= minDays && days <= maxDays;
        }).ToArray();
        return new ReportAgingBucketDto(label, items.Length, items.Sum(item => item.PurchasePrice), items.Sum(item => item.EstimatedSalePrice));
    }

    private static ReportChangeDto Change(decimal current, decimal previous) =>
        new(current, previous, previous == 0 ? null : (current - previous) / Math.Abs(previous) * 100);

    private static string SelectGranularity(DateTimeOffset from, DateTimeOffset to) => (to - from).TotalDays switch
    {
        <= 62 => "day",
        <= 186 => "week",
        _ => "month"
    };

    private static DateTimeOffset BucketStart(DateTimeOffset value, string granularity)
    {
        var utc = value.ToUniversalTime();
        if (granularity == "month") return new DateTimeOffset(utc.Year, utc.Month, 1, 0, 0, 0, TimeSpan.Zero);
        if (granularity == "week")
        {
            int daysSinceMonday = ((int)utc.DayOfWeek + 6) % 7;
            return new DateTimeOffset(utc.Date.AddDays(-daysSinceMonday), TimeSpan.Zero);
        }
        return new DateTimeOffset(utc.Date, TimeSpan.Zero);
    }

    private static DateTimeOffset NextBucket(DateTimeOffset value, string granularity) => granularity switch
    {
        "month" => value.AddMonths(1),
        "week" => value.AddDays(7),
        _ => value.AddDays(1)
    };

    private static string BucketLabel(DateTimeOffset value, string granularity) => granularity switch
    {
        "month" => value.ToString("MMM yyyy"),
        "week" => $"Week of {value:MMM d}",
        _ => value.ToString("MMM d")
    };
}
