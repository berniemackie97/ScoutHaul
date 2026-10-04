using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using HaulScout.Api.Authorization;
using HaulScout.Api.Services;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;

namespace HaulScout.Api.Controllers;

[ApiController]
[Route("api/reports")]
[RequireOrganizationPermission(HaulScoutPermissions.ViewReports)]
public sealed class ReportsController(BusinessReportService reports) : HaulScoutControllerBase
{
    [HttpGet("overview")]
    public async Task<ActionResult<BusinessReportDto>> Overview(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? preset,
        CancellationToken cancellationToken)
    {
        TimeZoneInfo zone = await reports.ZoneAsync(cancellationToken);
        if (!BusinessReportService.TryResolveRange(from, to, preset, zone, DateTimeOffset.UtcNow, out ReportRange range, out string? error))
        {
            return Invalid(error ?? "The report could not be produced.");
        }

        return await reports.BuildAsync(range, cancellationToken);
    }

    [HttpGet("overview.csv")]
    public async Task<IActionResult> OverviewCsv(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? preset,
        CancellationToken cancellationToken)
    {
        TimeZoneInfo zone = await reports.ZoneAsync(cancellationToken);
        if (!BusinessReportService.TryResolveRange(from, to, preset, zone, DateTimeOffset.UtcNow, out ReportRange range, out string? error))
        {
            return Invalid(error ?? "The report could not be produced.");
        }

        BusinessReportDto report = await reports.BuildAsync(range, cancellationToken);
        byte[] csv = Encoding.UTF8.GetBytes(ToCsv(report));
        string fileName = $"haulscout-report-{range.FromInclusive:yyyy-MM-dd}-to-{range.ToInclusive:yyyy-MM-dd}.csv";
        return File(csv, "text/csv; charset=utf-8", fileName);
    }

    private static string ToCsv(BusinessReportDto report)
    {
        StringBuilder csv = new();
        csv.AppendLine("HaulScout Business Report");
        csv.AppendLine($"From,{Csv(report.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))}");
        csv.AppendLine($"To,{Csv(report.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))}");
        csv.AppendLine($"Time zone,{Csv(report.TimeZone ?? "UTC")}");
        csv.AppendLine();
        csv.AppendLine("Metric,Value");
        csv.AppendLine($"Sales count,{report.Kpis.SalesCount}");
        csv.AppendLine($"Gross sales,{Money(report.Kpis.GrossSales)}");
        csv.AppendLine($"Cost of goods,{Money(report.Kpis.CostOfGoods)}");
        csv.AppendLine($"Gross profit,{Money(report.Kpis.GrossProfit)}");
        csv.AppendLine($"Selling costs,{Money(report.Kpis.SaleCosts)}");
        csv.AppendLine($"Platform fees,{Money(report.Kpis.PlatformFees)}");
        csv.AppendLine($"Payment fees,{Money(report.Kpis.PaymentFees)}");
        csv.AppendLine($"Shipping labels,{Money(report.Kpis.ShippingCosts)}");
        csv.AppendLine($"Supplies and other,{Money(report.Kpis.SuppliesAndOther)}");
        csv.AppendLine($"Refunds and adjustments,{Money(report.Kpis.RefundsAndAdjustments)}");
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
    // A leading = + - @ makes a spreadsheet run the cell as a formula; item titles come from marketplaces.
    private static string Csv(string value) =>
        $"\"{(value.Length > 0 && "=+-@\t\r".Contains(value[0]) ? "'" + value : value).Replace("\"", "\"\"")}\"";

}
