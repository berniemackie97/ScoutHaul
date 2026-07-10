using System.Globalization;
using System.Text;
using HaulScout.Api.Authorization;
using HaulScout.Api.Services;
using HaulScout.Application.Contracts;
using HaulScout.Application.Services;
using HaulScout.Domain;
using HaulScout.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HaulScout.Api.Controllers;

[ApiController]
[Route("api/reports")]
[RequireOrganizationPermission(HaulScoutPermissions.ViewReports)]
public sealed class ReportsController(HaulScoutDbContext db, ICurrentUserContext currentUser) : ControllerBase
{
    private static readonly InventoryStatus[] ReportableInventoryStatuses =
    [
        InventoryStatus.Purchased,
        InventoryStatus.NeedsCleaning,
        InventoryStatus.NeedsTesting,
        InventoryStatus.NeedsRepair,
        InventoryStatus.NeedsPhotos,
        InventoryStatus.ReadyToList,
        InventoryStatus.Listed,
        InventoryStatus.Returned,
    ];

    [HttpGet("overview")]
    public async Task<ActionResult<BusinessReportDto>> Overview(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken cancellationToken)
    {
        if (!TryResolveRange(from, to, out ReportRange range, out string? error))
        {
            return BadRequest(error);
        }

        return await BuildReport(range, cancellationToken);
    }

    [HttpGet("overview.csv")]
    public async Task<IActionResult> OverviewCsv(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken cancellationToken)
    {
        if (!TryResolveRange(from, to, out ReportRange range, out string? error))
        {
            return BadRequest(error);
        }

        BusinessReportDto report = await BuildReport(range, cancellationToken);
        byte[] csv = Encoding.UTF8.GetBytes(ToCsv(report));
        string fileName = $"haulscout-report-{range.From:yyyy-MM-dd}-to-{range.ToInclusive:yyyy-MM-dd}.csv";
        return File(csv, "text/csv; charset=utf-8", fileName);
    }

    private async Task<BusinessReportDto> BuildReport(ReportRange range, CancellationToken cancellationToken)
    {
        Guid organizationId = currentUser.OrganizationId;

        List<ReportSaleInput> sales = await db.Sales
            .AsNoTracking()
            .Where(item => item.OrganizationId == organizationId && item.SoldAt >= range.PreviousFrom && item.SoldAt < range.ToExclusive)
            .Select(item => new ReportSaleInput(
                item.Id,
                item.InventoryItemId,
                item.InventoryItem == null ? "Unknown item" : item.InventoryItem.Title,
                item.Platform,
                item.SoldPrice,
                item.ShippingCharged,
                item.CostOfGoods,
                item.PlatformFees,
                item.PaymentFees,
                item.ShippingCost,
                item.SuppliesCost,
                item.OtherCosts,
                item.NetProfit,
                item.Status,
                item.SoldAt,
                item.SalesTaxCollected,
                item.TaxRemittedByPlatform))
            .ToListAsync(cancellationToken);

        List<ReportExpenseInput> expenses = await db.Expenses
            .AsNoTracking()
            .Where(item => item.OrganizationId == organizationId && item.OccurredAt >= range.PreviousFrom && item.OccurredAt < range.ToExclusive)
            .Select(item => new ReportExpenseInput(item.Category, item.Amount, item.OccurredAt))
            .ToListAsync(cancellationToken);

        List<ReportInventoryInput> inventory = await db.InventoryItems
            .AsNoTracking()
            .Where(item => item.OrganizationId == organizationId && !item.IsArchived && ReportableInventoryStatuses.Contains(item.Status))
            .Select(item => new ReportInventoryInput(
                item.Category ?? "Uncategorized",
                item.Status,
                item.PurchasePrice ?? 0,
                item.EstimatedSalePrice ?? item.SellerAskingPrice ?? 0,
                item.PurchasedAt ?? item.CreatedAt,
                item.PurchasePrice.HasValue,
                item.EstimatedSalePrice > 0 || item.SellerAskingPrice > 0,
                item.StorageLocation != null && item.StorageLocation != "",
                item.Photos.Any()))
            .ToListAsync(cancellationToken);

        return BusinessReportCalculator.Build(
            range.From,
            range.ToExclusive,
            sales.Where(item => item.SoldAt >= range.From).ToArray(),
            sales.Where(item => item.SoldAt >= range.PreviousFrom && item.SoldAt < range.From).ToArray(),
            expenses.Where(item => item.OccurredAt >= range.From).ToArray(),
            expenses.Where(item => item.OccurredAt >= range.PreviousFrom && item.OccurredAt < range.From).ToArray(),
            inventory,
            DateTimeOffset.UtcNow);
    }

    private static bool TryResolveRange(DateOnly? from, DateOnly? to, out ReportRange range, out string? error)
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
        DateOnly end = to ?? today;
        DateOnly start = from ?? end.AddDays(-89);
        int days = end.DayNumber - start.DayNumber + 1;

        switch (days)
        {
            case <= 0:
                range = default;
                error = "The report start date must be on or before the end date.";
                return false;
            case > 731:
                range = default;
                error = "Report ranges are limited to 731 days.";
                return false;
        }

        DateTimeOffset fromValue = new(start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        DateTimeOffset toExclusive = new(end.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        range = new ReportRange(fromValue, toExclusive, fromValue.AddDays(-days), end);
        error = null;
        return true;
    }

    private static string ToCsv(BusinessReportDto report)
    {
        StringBuilder csv = new();
        csv.AppendLine("HaulScout Business Report");
        csv.AppendLine($"From,{Csv(report.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))}");
        csv.AppendLine($"To,{Csv(report.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))}");
        csv.AppendLine();
        csv.AppendLine("Metric,Value");
        csv.AppendLine($"Sales count,{report.Kpis.SalesCount}");
        csv.AppendLine($"Gross sales,{Money(report.Kpis.GrossSales)}");
        csv.AppendLine($"Cost of goods,{Money(report.Kpis.CostOfGoods)}");
        csv.AppendLine($"Gross profit,{Money(report.Kpis.GrossProfit)}");
        csv.AppendLine($"Selling costs,{Money(report.Kpis.SaleCosts)}");
        csv.AppendLine($"Realized net profit,{Money(report.Kpis.RealizedNetProfit)}");
        csv.AppendLine($"Recorded operating expenses,{Money(report.Kpis.OperatingExpenses)}");
        csv.AppendLine($"Inventory spend,{Money(report.Kpis.InventorySpend)}");
        csv.AppendLine();
        csv.AppendLine("Period,Sales count,Gross sales,Net profit,Operating expenses,Inventory spend");
        foreach (ReportSeriesPointDto point in report.Series)
        {
            csv.AppendLine($"{Csv(point.Label)},{point.SalesCount},{Money(point.GrossSales)},{Money(point.NetProfit)},{Money(point.OperatingExpenses)},{Money(point.InventorySpend)}");
        }
        csv.AppendLine();
        csv.AppendLine("Platform,Sales count,Gross sales,Net profit");
        foreach (ReportDimensionDto platform in report.Platforms)
        {
            csv.AppendLine($"{Csv(platform.Name)},{platform.Count},{Money(platform.Amount)},{Money(platform.SecondaryAmount)}");
        }
        csv.AppendLine();
        csv.AppendLine("Top item,Platform,Sold price,Purchase cost,Net profit,Margin,Sold at");
        foreach (ReportSaleRowDto sale in report.TopSales)
        {
            csv.AppendLine($"{Csv(sale.Title)},{Csv(sale.Platform)},{Money(sale.SoldPrice)},{Money(sale.PurchaseCost)},{Money(sale.NetProfit)},{sale.MarginPercent.ToString("0.00", CultureInfo.InvariantCulture)}%,{sale.SoldAt:O}");
        }
        return csv.ToString();
    }

    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
    private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

    private readonly record struct ReportRange(DateTimeOffset From, DateTimeOffset ToExclusive, DateTimeOffset PreviousFrom, DateOnly ToInclusive);
}
