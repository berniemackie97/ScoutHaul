using Microsoft.EntityFrameworkCore;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Application.Services;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Api.Services;

/// <summary>
/// A reporting window and the one before it, which every figure is compared against. Days are the
/// business's own calendar days in <see cref="Zone"/>, not UTC's.
/// </summary>
/// <param name="PreviousFrom">Start of the equal-length window immediately before <paramref name="From"/>.</param>
public readonly record struct ReportRange(
    DateTimeOffset From,
    DateTimeOffset ToExclusive,
    DateTimeOffset PreviousFrom,
    DateOnly ToInclusive,
    DateOnly FromInclusive,
    TimeZoneInfo Zone)
{
    /// <summary>The last <paramref name="days"/> days ending today, inclusive, and the same number before them.</summary>
    public static ReportRange Trailing(DateOnly today, int days, TimeZoneInfo? zone = null) =>
        Between(today.AddDays(1 - days), today, zone ?? TimeZoneInfo.Utc);

    public static ReportRange Between(DateOnly start, DateOnly end, TimeZoneInfo zone)
    {
        int days = end.DayNumber - start.DayNumber + 1;
        return new ReportRange(
            Midnight(start, zone),
            Midnight(end.AddDays(1), zone),
            Midnight(start.AddDays(-days), zone),
            end,
            start,
            zone);
    }

    // UTC instants: Npgsql refuses to write any other offset to a timestamptz.
    private static DateTimeOffset Midnight(DateOnly day, TimeZoneInfo zone)
    {
        DateTime local = day.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
    }
}

/// <summary>
/// Profit and loss for a window — the single place those figures are assembled.
///
/// This lived inside ReportsController, so nothing else could produce the same numbers without
/// copying the queries. The home page therefore computed its own "net profit" with separate SQL
/// sums, and the two pages could disagree about the same business. Both now come from here.
/// </summary>
public sealed class BusinessReportService(HaulScoutDbContext db, ICurrentUserContext currentUser)
{
    /// <summary>Longest window a report may span.</summary>
    public const int MaxRangeDays = 731;

    /// <summary>What a report covers when the caller does not say.</summary>
    public const int DefaultRangeDays = 90;

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

    /// <summary>The organization's calendar, which every report day is counted in.</summary>
    public async Task<TimeZoneInfo> ZoneAsync(CancellationToken cancellationToken) =>
        Organization.ZoneOf(await db.Organizations
            .Where(organization => organization.Id == currentUser.OrganizationId)
            .Select(organization => organization.TimeZoneId)
            .SingleOrDefaultAsync(cancellationToken));

    public static DateOnly Today(TimeZoneInfo zone, DateTimeOffset now) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);

    public static bool TryResolveRange(DateOnly? from, DateOnly? to, TimeZoneInfo zone, out ReportRange range, out string? error) =>
        TryResolveRange(from, to, null, zone, DateTimeOffset.UtcNow, out range, out error);

    /// <param name="preset">7d, 30d, 90d, 12m or ytd, ending today in the business's zone; wins over explicit dates.</param>
    public static bool TryResolveRange(
        DateOnly? from,
        DateOnly? to,
        string? preset,
        TimeZoneInfo zone,
        DateTimeOffset now,
        out ReportRange range,
        out string? error)
    {
        DateOnly today = Today(zone, now);
        if (!string.IsNullOrWhiteSpace(preset))
        {
            DateOnly? presetStart = preset.Trim().ToLowerInvariant() switch
            {
                "7d" => today.AddDays(-6),
                "30d" => today.AddDays(-29),
                "90d" => today.AddDays(-89),
                "12m" => today.AddYears(-1).AddDays(1),
                "ytd" => new DateOnly(today.Year, 1, 1),
                _ => null,
            };
            if (presetStart is null)
            {
                range = default;
                error = "Unknown report range. Use 7d, 30d, 90d, 12m or ytd.";
                return false;
            }

            from = presetStart;
            to = today;
        }

        DateOnly end = to ?? today;
        DateOnly start = from ?? end.AddDays(1 - DefaultRangeDays);
        int days = end.DayNumber - start.DayNumber + 1;

        switch (days)
        {
            case <= 0:
                range = default;
                error = "The report start date must be on or before the end date.";
                return false;
            case > MaxRangeDays:
                range = default;
                error = "Report ranges are limited to 731 days.";
                return false;
        }

        range = ReportRange.Between(start, end, zone);
        error = null;
        return true;
    }

    public async Task<BusinessReportDto> BuildAsync(ReportRange range, CancellationToken cancellationToken)
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
                item.TaxRemittedByPlatform,
                item.OrderId))
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
            DateTimeOffset.UtcNow,
            range.Zone);
    }
}
