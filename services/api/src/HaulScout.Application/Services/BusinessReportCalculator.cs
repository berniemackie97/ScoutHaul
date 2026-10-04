using System.Globalization;
using HaulScout.Application.Contracts;
using HaulScout.Domain;

namespace HaulScout.Application.Services;

public sealed record ReportSaleInput(
    Guid Id,
    Guid InventoryItemId,
    string InventoryItemTitle,
    string Platform,
    decimal SoldPrice,
    decimal ShippingCharged,
    decimal PurchasePrice,
    decimal PlatformFees,
    decimal PaymentFees,
    decimal ShippingCost,
    decimal SuppliesCost,
    decimal OtherCosts,
    decimal NetProfit,
    SaleStatus Status,
    DateTimeOffset SoldAt,
    decimal SalesTaxCollected = 0,
    bool TaxRemittedByPlatform = true,
    Guid? OrderId = null);

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
        DateTimeOffset generatedAt,
        TimeZoneInfo? zone = null)
    {
        // Days, weeks and months are the business's own, not UTC's: an evening sale in Los Angeles is that day's sale.
        zone ??= TimeZoneInfo.Utc;
        if (toExclusive <= from)
        {
            throw new ArgumentException("Report end must be after report start.");
        }

        ReportKpiDto current = CalculateKpis(currentSales, currentExpenses);
        ReportKpiDto previous = CalculateKpis(previousSales, previousExpenses);
        string granularity = SelectGranularity(from, toExclusive);

        Dictionary<DateTime, ReportSaleInput[]> salesByPeriod = currentSales
            .Where(sale => SaleWorkflow.IsRealized(sale.Status))
            .GroupBy(sale => BucketStart(sale.SoldAt, granularity, zone))
            .ToDictionary(group => group.Key, group => group.ToArray());
        Dictionary<DateTime, ReportExpenseInput[]> expensesByPeriod = currentExpenses
            .GroupBy(expense => BucketStart(expense.OccurredAt, granularity, zone))
            .ToDictionary(group => group.Key, group => group.ToArray());

        List<ReportSeriesPointDto> series = [];
        for (DateTime cursor = BucketStart(from, granularity, zone); AtZone(cursor, zone) < toExclusive; cursor = NextBucket(cursor, granularity))
        {
            ReportSaleInput[] sales = salesByPeriod.GetValueOrDefault(cursor) ?? [];
            ReportExpenseInput[] expenses = expensesByPeriod.GetValueOrDefault(cursor) ?? [];
            series.Add(new ReportSeriesPointDto(
                AtZone(cursor, zone),
                BucketLabel(cursor, granularity),
                sales.Sum(SaleRevenue),
                sales.Sum(item => item.NetProfit),
                expenses.Where(item => item.Category != ExpenseCategory.InventoryPurchase).Sum(item => item.Amount),
                expenses.Where(item => item.Category == ExpenseCategory.InventoryPurchase).Sum(item => item.Amount),
                sales.Length));
        }

        ReportDimensionDto[] platforms = currentSales
            .Where(sale => SaleWorkflow.IsRealized(sale.Status))
            .GroupBy(sale => string.IsNullOrWhiteSpace(sale.Platform) ? "Unspecified" : sale.Platform.Trim())
            .Select(group => new ReportDimensionDto(group.Key, group.Count(), group.Sum(SaleRevenue), group.Sum(item => item.NetProfit)))
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

        DateTimeOffset ageNow = generatedAt;
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

        ReportSaleRowDto[] losingSales = currentSales
            .Where(sale => SaleWorkflow.IsRealized(sale.Status) && sale.NetProfit < 0)
            .OrderBy(sale => sale.NetProfit)
            .Take(10)
            .Select(ToSaleRow)
            .ToArray();

        int staleCount = inventory.Count(item => (ageNow - item.AcquiredAt).TotalDays > 60 && item.Status != InventoryStatus.Listed);
        ReportInventorySnapshotDto inventorySnapshot = new(
            inventory.Count,
            inventory.Sum(item => item.PurchasePrice),
            inventory.Sum(item => item.EstimatedSalePrice),
            inventory.Count(item => InventoryStatusRules.IsUnlistedInventory(item.Status)),
            staleCount,
            inventory.Count(item => !item.HasCost),
            inventory.Count(item => !item.HasEstimatedValue),
            inventory.Count(item => !item.HasStorageLocation),
            inventory.Count(item => !item.HasPhoto));

        ReportComparisonDto comparison = new(
            Change(current.GrossSales, previous.GrossSales),
            Change(current.RealizedNetProfit, previous.RealizedNetProfit),
            Change(current.SalesCount, previous.SalesCount),
            Change(current.AverageSaleValue, previous.AverageSaleValue),
            Change(current.OperatingExpenses, previous.OperatingExpenses));

        return new BusinessReportDto(
            TimeZoneInfo.ConvertTime(from, zone),
            TimeZoneInfo.ConvertTime(toExclusive.AddTicks(-1), zone),
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
            topSales,
            previous,
            losingSales,
            zone.Id);
    }

    private static ReportKpiDto CalculateKpis(IReadOnlyList<ReportSaleInput> sales, IReadOnlyList<ReportExpenseInput> expenses)
    {
        ReportSaleInput[] realized = sales.Where(sale => SaleWorkflow.IsRealized(sale.Status)).ToArray();
        decimal grossSales = realized.Sum(SaleRevenue);
        decimal costOfGoods = realized.Sum(item => item.PurchasePrice);
        decimal saleCosts = realized.Sum(item => item.PlatformFees + item.PaymentFees + item.ShippingCost + item.SuppliesCost + item.OtherCosts);
        decimal netProfit = realized.Sum(item => item.NetProfit);
        decimal operatingExpenses = expenses.Where(item => item.Category != ExpenseCategory.InventoryPurchase).Sum(item => item.Amount);
        decimal inventorySpend = expenses.Where(item => item.Category == ExpenseCategory.InventoryPurchase).Sum(item => item.Amount);
        // Sales tax we collected and must remit ourselves (marketplace-remitted tax is pass-through, excluded).
        // It is a liability we hold, never revenue or profit — reported separately, not netted into income.
        decimal salesTaxOwed = realized.Sum(item => item.TaxRemittedByPlatform ? 0m : Math.Max(0, item.SalesTaxCollected));
        // True business net income for the period: realized profit on goods, less operating overhead.
        decimal netIncomeAfterExpenses = netProfit - operatingExpenses;
        decimal platformFees = realized.Sum(item => item.PlatformFees);
        decimal paymentFees = realized.Sum(item => item.PaymentFees);
        decimal shippingCosts = realized.Sum(item => item.ShippingCost);
        decimal suppliesAndOther = realized.Sum(item => item.SuppliesCost + item.OtherCosts);
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
            grossSales == 0 ? 0 : netProfit / grossSales * 100,
            salesTaxOwed,
            netIncomeAfterExpenses,
            realized.Sum(item => item.SoldPrice),
            realized.Sum(item => item.ShippingCharged),
            platformFees,
            paymentFees,
            shippingCosts,
            suppliesAndOther,
            grossSales - costOfGoods - saleCosts - netProfit);
    }

    private static ReportSaleRowDto ToSaleRow(ReportSaleInput sale)
    {
        decimal revenue = SaleRevenue(sale);
        decimal margin = revenue == 0 ? 0 : sale.NetProfit / revenue * 100;
        return new ReportSaleRowDto(sale.Id, sale.InventoryItemId, sale.InventoryItemTitle, sale.Platform, sale.SoldPrice, sale.PurchasePrice, sale.NetProfit, margin, sale.SoldAt, sale.OrderId);
    }

    private static decimal SaleRevenue(ReportSaleInput sale) => sale.SoldPrice + sale.ShippingCharged;

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

    /// <summary>The bucket's first local day, in the business's zone.</summary>
    private static DateTime BucketStart(DateTimeOffset value, string granularity, TimeZoneInfo zone)
    {
        DateTime local = TimeZoneInfo.ConvertTime(value, zone).Date;
        if (granularity == "month") return new DateTime(local.Year, local.Month, 1);
        if (granularity == "week") return local.AddDays(-(((int)local.DayOfWeek + 6) % 7));
        return local;
    }

    private static DateTime NextBucket(DateTime value, string granularity) => granularity switch
    {
        "month" => value.AddMonths(1),
        "week" => value.AddDays(7),
        _ => value.AddDays(1)
    };

    /// <summary>Local midnight as an instant, with that day's offset so DST weeks stay whole.</summary>
    private static DateTimeOffset AtZone(DateTime local, TimeZoneInfo zone) => new(local, zone.GetUtcOffset(local));

    private static string BucketLabel(DateTime value, string granularity) => granularity switch
    {
        "month" => value.ToString("MMM yyyy", CultureInfo.InvariantCulture),
        "week" => string.Create(CultureInfo.InvariantCulture, $"Week of {value:MMM d}"),
        _ => value.ToString("MMM d", CultureInfo.InvariantCulture)
    };
}
