using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using HaulScout.Application.Contracts;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Domain.Tax;

namespace HaulScout.Application.Tax;

public sealed record SalesTaxPeriodInput(
    SalesTaxRegistration Registration,
    SalesTaxPeriod Period,
    TimeZoneInfo Zone,
    IReadOnlyList<SalesTaxSaleRow> Sales,
    IReadOnlyList<SalesTaxRefundRow> Refunds,
    IReadOnlyList<SalesTaxUseEntry> UseEntries,
    IReadOnlyList<SalesTaxAdjustment> Adjustments,
    IReadOnlyDictionary<Guid, SalesTaxSaleDecision> Decisions,
    decimal DeductionsCarriedIn,
    decimal CreditsCarriedIn,
    Dr15Timing Timing);

public sealed record SalesTaxPeriodComputation(
    Dr15Result Result,
    IReadOnlyList<SalesTaxInvoiceDto> Invoices,
    IReadOnlyList<SalesTaxReportedSaleDto> ReportedSales,
    IReadOnlyList<SalesTaxExcludedChannelDto> ExcludedChannels,
    IReadOnlyList<SalesTaxExcludedSaleDto> ExcludedSales,
    IReadOnlyList<SalesTaxUseLineDto> UseTax,
    IReadOnlyList<SalesTaxRefundLineDto> Refunds,
    IReadOnlyList<SalesTaxAdjustmentDto> Adjustments,
    SalesTaxCarryDto Carry,
    IReadOnlyList<SalesTaxIssueDto> Issues,
    string Fingerprint);

/// <summary>
/// Turns one period's stored rows into DR-15 inputs: which sales the business reports, where each was delivered, how
/// sales group into invoices, and what fed Lines 6, 8 and 9. The arithmetic itself is <see cref="Dr15Calculator"/>.
/// </summary>
public static class SalesTaxPeriodBuilder
{
    public const string ExclusionBasis = "Collected and remitted by a registered marketplace provider; excluded under s. 212.05965(4)(a), F.S.";

    public static DateOnly LocalDate(DateTimeOffset instant, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);

    public static DateTimeOffset StartOfDayUtc(DateOnly day, TimeZoneInfo zone)
    {
        DateTime local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
    }

    public static SalesTaxPeriodComputation Compute(SalesTaxPeriodInput input)
    {
        SalesTaxRegistration registration = input.Registration;
        SalesTaxPeriod period = input.Period;
        string businessCounty = registration.County;
        var issues = new List<SalesTaxIssueDto>();

        List<(SalesTaxSaleRow Row, DateOnly Date)> inPeriod = input.Sales
            .Select(row => (Row: row, Date: LocalDate(row.SoldAt, input.Zone)))
            .Where(item => period.Contains(item.Date) && item.Row.Status != SaleStatus.Cancelled)
            .OrderBy(item => item.Date).ThenBy(item => item.Row.SaleId)
            .ToList();

        var reported = new List<SalesTaxReportedSaleDto>();
        var excluded = new List<SalesTaxExcludedSaleDto>();
        foreach ((SalesTaxSaleRow row, DateOnly date) in inPeriod)
        {
            SalesTaxCollector collector = FloridaMarketplaceProviders.CollectorFor(row.Platform);
            if (row.TaxRemittedByPlatform)
            {
                excluded.Add(new SalesTaxExcludedSaleDto(row.SaleId, date, row.Platform, row.ItemTitle, row.SoldPrice, row.ShippingCharged, row.SalesTaxCollected));
                if (collector == SalesTaxCollector.Business)
                {
                    issues.Add(new(Dr15IssueSeverity.Blocking, "collector_mismatch",
                        $"{row.Platform} does not collect Florida tax, but this sale is marked as collected by the platform. Report it, or record why the platform collected.",
                        $"sale:{row.SaleId}", [row.SaleId]));
                }
                else if (collector == SalesTaxCollector.Unknown)
                {
                    issues.Add(new(Dr15IssueSeverity.Warning, "collector_unknown",
                        $"\"{row.Platform}\" is not a known marketplace provider. Confirm it collected Florida tax before leaving this sale off the return.",
                        $"sale:{row.SaleId}", [row.SaleId]));
                }

                continue;
            }

            if (collector == SalesTaxCollector.Marketplace)
            {
                issues.Add(new(Dr15IssueSeverity.Warning, "reported_marketplace_sale",
                    $"{row.Platform} collects Florida tax as a marketplace provider, yet this sale is set to be reported by the business. Reporting it too would pay the tax twice.",
                    $"sale:{row.SaleId}", [row.SaleId]));
            }

            if (date < registration.RegisteredOn)
            {
                issues.Add(new(Dr15IssueSeverity.Info, "sale_before_registration",
                    $"Sold {date:MM/dd/yyyy}, before the registration date; it is still reported in this period.",
                    $"sale:{row.SaleId}", [row.SaleId]));
            }

            input.Decisions.TryGetValue(row.SaleId, out SalesTaxSaleDecision? decision);
            (Dr15Destination destination, string? county, Dr15Exemption exemption, string source) = Resolve(row, decision, businessCounty);
            decimal discountShare = row.OrderLinesTotal > 0 && row.OrderDiscountTotal > 0
                ? FloridaTaxMath.Cents(row.OrderDiscountTotal * row.SoldPrice / row.OrderLinesTotal)
                : 0m;

            reported.Add(new SalesTaxReportedSaleDto(
                row.SaleId,
                InvoiceKey(row, decision),
                date,
                row.Platform,
                row.ItemTitle,
                row.OrderNumber,
                row.OrderId,
                row.SoldPrice - discountShare,
                row.ShippingCharged,
                row.SalesTaxCollected,
                destination,
                county,
                source,
                exemption,
                decision is not null));
        }

        List<IGrouping<string, SalesTaxReportedSaleDto>> groups = reported.GroupBy(sale => sale.InvoiceKey).ToList();
        List<Dr15SaleInput> saleInputs = groups.Select(group =>
        {
            SalesTaxReportedSaleDto first = group.First();
            return new Dr15SaleInput(
                group.Key,
                group.Min(sale => sale.Date),
                group.Select(sale => sale.ItemAmount).ToList(),
                group.Sum(sale => sale.ShippingCharged),
                group.Sum(sale => sale.TaxCharged),
                first.Destination,
                first.County,
                first.Exemption,
                ShippingTaxable: !registration.DeliveryChargesOptional);
        }).ToList();

        List<SalesTaxUseEntry> useEntries = input.UseEntries
            .Where(entry => period.Contains(entry.OccurredOn))
            .OrderBy(entry => entry.OccurredOn).ThenBy(entry => entry.Id)
            .ToList();
        List<Dr15UseTaxInput> useInputs = useEntries
            .Select(entry => new Dr15UseTaxInput($"use:{entry.Id}", entry.OccurredOn, entry.Amount, entry.County ?? businessCounty, entry.OtherStateTaxPaid))
            .ToList();

        List<SalesTaxRefundLineDto> refunds = input.Refunds
            .Select(refund => (Refund: refund, Date: LocalDate(refund.RefundedAt, input.Zone)))
            .Where(item => period.Contains(item.Date) && !item.Refund.SaleTaxRemittedByPlatform && item.Refund.TaxRefunded > 0)
            .OrderBy(item => item.Date)
            .Select(item => new SalesTaxRefundLineDto(item.Refund.RefundId, item.Refund.SaleId, item.Date, item.Refund.Platform, item.Refund.TaxRefunded))
            .ToList();

        List<SalesTaxAdjustment> adjustments = input.Adjustments
            .Where(adjustment => adjustment.PeriodStart == period.Start && adjustment.PeriodEnd == period.End)
            .OrderBy(adjustment => adjustment.CreatedAt)
            .ToList();
        decimal Sum(SalesTaxAdjustmentKind kind) => adjustments.Where(adjustment => adjustment.Kind == kind).Sum(adjustment => adjustment.Amount);

        var dr15Adjustments = new Dr15Adjustments(
            TaxRefundedToBuyers: refunds.Sum(refund => refund.TaxRefunded),
            OtherDeductions: Sum(SalesTaxAdjustmentKind.LawfulDeduction),
            DeductionsCarriedIn: input.DeductionsCarriedIn,
            EstimatedTaxPaidAndCredits: Sum(SalesTaxAdjustmentKind.CreditOrEstimatedTaxPaid),
            CreditsCarriedIn: input.CreditsCarriedIn,
            EstimatedTaxDueCurrentMonth: Sum(SalesTaxAdjustmentKind.EstimatedTaxDue));

        Dr15Result result = Dr15Calculator.Compute(new Dr15Request(period, businessCounty, saleInputs, useInputs, dr15Adjustments, input.Timing));

        Dictionary<string, IReadOnlyList<Guid>> saleIdsByInvoice = groups.ToDictionary(group => group.Key, group => (IReadOnlyList<Guid>)group.Select(sale => sale.SaleId).ToList());
        issues.AddRange(result.Issues.Select(issue => new SalesTaxIssueDto(
            issue.Severity,
            issue.Code,
            issue.Message,
            issue.Reference,
            issue.Reference is not null && saleIdsByInvoice.TryGetValue(issue.Reference, out IReadOnlyList<Guid>? ids) ? ids : [])));

        foreach (SalesTaxReportedSaleDto sale in reported.Where(sale => sale.DestinationSource == AssumedInPerson && !sale.HasDecision))
        {
            issues.Add(new(Dr15IssueSeverity.Info, "destination_assumed",
                $"Sold in person on {sale.Channel}; taxed at the business county ({businessCounty}). Record a decision if it changed hands elsewhere.",
                sale.InvoiceKey, [sale.SaleId]));
        }

        List<SalesTaxInvoiceDto> invoices = result.Sales.Select(sale =>
        {
            Dr15SaleInput source = saleInputs.First(input => input.Reference == sale.Reference);
            return new SalesTaxInvoiceDto(
                sale.Reference,
                source.Date,
                saleIdsByInvoice[sale.Reference],
                sale.Gross,
                sale.Exempt,
                sale.Taxable,
                sale.County,
                sale.SurtaxRate,
                sale.ComputedTax,
                sale.SurtaxPortion,
                sale.TaxCharged,
                sale.TaxDue,
                sale.OverItemCap,
                sale.SurtaxClass,
                sale.Treatment);
        }).ToList();

        List<SalesTaxExcludedChannelDto> excludedChannels = excluded
            .GroupBy(sale => sale.Channel, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new SalesTaxExcludedChannelDto(
                group.Key,
                group.Count(),
                group.Sum(sale => sale.ItemAmount + sale.ShippingCharged),
                group.Sum(sale => sale.TaxCollectedByMarketplace),
                FloridaMarketplaceProviders.CollectorFor(group.Key) == SalesTaxCollector.Marketplace
                    ? ExclusionBasis
                    : "Marked as collected by the platform at checkout; confirm before filing."))
            .ToList();

        List<SalesTaxUseLineDto> useLines = useEntries.Zip(result.UseTax, (entry, line) => new SalesTaxUseLineDto(ToDto(entry), line)).ToList();
        var carry = new SalesTaxCarryDto(input.DeductionsCarriedIn, input.CreditsCarriedIn, result.DeductionsCarriedOut, result.CreditsCarriedOut);

        string fingerprint = Fingerprint(registration, period, reported, excluded, useEntries, refunds, adjustments, carry);

        return new SalesTaxPeriodComputation(
            result,
            invoices,
            reported,
            excludedChannels,
            excluded,
            useLines,
            refunds,
            adjustments.Select(adjustment => new SalesTaxAdjustmentDto(adjustment.Id, adjustment.Kind, adjustment.Amount, adjustment.Description)).ToList(),
            carry,
            issues
                .OrderBy(issue => issue.Severity)
                .ThenBy(issue => issue.Code, StringComparer.Ordinal)
                .ToList(),
            fingerprint);
    }

    public static SalesTaxUseEntryDto ToDto(SalesTaxUseEntry entry) => new(
        entry.Id, entry.OccurredOn, entry.Description, entry.Amount, entry.County, entry.OtherStateTaxPaid, entry.ExpenseId, entry.InventoryItemId, entry.Notes);

    private const string AssumedInPerson = "Sold in person; assumed business county";

    private static string InvoiceKey(SalesTaxSaleRow row, SalesTaxSaleDecision? decision) =>
        decision is null && row.OrderId is Guid orderId ? $"order:{orderId}" : $"sale:{row.SaleId}";

    private static (Dr15Destination Destination, string? County, Dr15Exemption Exemption, string Source) Resolve(
        SalesTaxSaleRow row,
        SalesTaxSaleDecision? decision,
        string businessCounty)
    {
        if (decision is not null)
        {
            return (decision.Destination, FloridaSurtax.Canonical(decision.County), decision.Exemption, "Recorded decision");
        }

        if (row.IsLocalPickup)
        {
            return (Dr15Destination.FloridaCounty, businessCounty, Dr15Exemption.None, "Local pickup at the business");
        }

        string? country = row.ShipToCountry?.Trim();
        if (country is { Length: > 0 } && !IsUnitedStates(country))
        {
            return (Dr15Destination.OutsideFlorida, null, Dr15Exemption.None, $"Shipped to {country}");
        }

        string? region = row.ShipToRegion?.Trim();
        if (region is { Length: > 0 })
        {
            return IsFlorida(region)
                ? (Dr15Destination.Unknown, null, Dr15Exemption.None, "Shipped within Florida; county not captured")
                : (Dr15Destination.OutsideFlorida, null, Dr15Exemption.None, $"Shipped to {region}");
        }

        return FloridaMarketplaceProviders.IsInPerson(row.Platform)
            ? (Dr15Destination.FloridaCounty, businessCounty, Dr15Exemption.None, AssumedInPerson)
            : (Dr15Destination.Unknown, null, Dr15Exemption.None, "No delivery address");
    }

    private static bool IsUnitedStates(string country) =>
        country.Equals("US", StringComparison.OrdinalIgnoreCase)
        || country.Equals("USA", StringComparison.OrdinalIgnoreCase)
        || country.Equals("United States", StringComparison.OrdinalIgnoreCase);

    private static bool IsFlorida(string region) =>
        region.Equals("FL", StringComparison.OrdinalIgnoreCase) || region.Equals("Florida", StringComparison.OrdinalIgnoreCase);

    /// <summary>Every input that can change what the return says, and nothing that cannot (filing date, fees, notes).</summary>
    private static string Fingerprint(
        SalesTaxRegistration registration,
        SalesTaxPeriod period,
        IReadOnlyList<SalesTaxReportedSaleDto> reported,
        IReadOnlyList<SalesTaxExcludedSaleDto> excluded,
        IReadOnlyList<SalesTaxUseEntry> useEntries,
        IReadOnlyList<SalesTaxRefundLineDto> refunds,
        IReadOnlyList<SalesTaxAdjustment> adjustments,
        SalesTaxCarryDto carry)
    {
        var text = new StringBuilder();
        void Line(params object?[] parts) => text.AppendLine(string.Join('|', parts.Select(part => Convert.ToString(part, CultureInfo.InvariantCulture))));

        Line("v1", period.Key, registration.County, registration.DeliveryChargesOptional, FloridaSurtax.TableSource);
        foreach (SalesTaxReportedSaleDto sale in reported.OrderBy(sale => sale.SaleId))
        {
            Line("s", sale.SaleId, sale.Date, sale.InvoiceKey, sale.ItemAmount, sale.ShippingCharged, sale.TaxCharged, sale.Destination, sale.County, sale.Exemption);
        }

        foreach (SalesTaxExcludedSaleDto sale in excluded.OrderBy(sale => sale.SaleId))
        {
            Line("x", sale.SaleId, sale.Date);
        }

        foreach (SalesTaxUseEntry entry in useEntries.OrderBy(entry => entry.Id))
        {
            Line("u", entry.Id, entry.OccurredOn, entry.Amount, entry.County, entry.OtherStateTaxPaid);
        }

        foreach (SalesTaxRefundLineDto refund in refunds.OrderBy(refund => refund.RefundId))
        {
            Line("r", refund.RefundId, refund.Date, refund.TaxRefunded);
        }

        foreach (SalesTaxAdjustment adjustment in adjustments.OrderBy(adjustment => adjustment.Id))
        {
            Line("a", adjustment.Id, adjustment.Kind, adjustment.Amount);
        }

        Line("c", carry.DeductionsCarriedIn, carry.CreditsCarriedIn);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }
}
