namespace HaulScout.Domain.Tax;

public static class FloridaTaxMath
{
    /// <summary>DR-15N: carry to the third decimal and round up when it is greater than 4 — half-up at the cent.</summary>
    public static decimal Cents(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}

public enum Dr15Destination
{
    FloridaCounty,
    OutsideFlorida,
    Unknown,
}

public enum Dr15Exemption
{
    None,
    Resale,
    ExemptOrganization,
    Other,
}

public enum Dr15SurtaxClass
{
    None,
    BusinessCountyRate,
    DifferentRate,
    NotSubjectToSurtax,
}

public enum Dr15IssueSeverity
{
    Blocking,
    Warning,
    Info,
}

public sealed record Dr15Issue(Dr15IssueSeverity Severity, string Code, string Message, string? Reference = null);

/// <summary>One invoice the business is the dealer on. Items are separate so the $5,000 surtax cap applies per item.</summary>
public sealed record Dr15SaleInput(
    string Reference,
    DateOnly Date,
    IReadOnlyList<decimal> ItemAmounts,
    decimal ShippingCharged,
    decimal TaxCharged,
    Dr15Destination Destination,
    string? County,
    Dr15Exemption Exemption,
    bool ShippingTaxable);

public sealed record Dr15SaleResult(
    string Reference,
    decimal Gross,
    decimal Exempt,
    decimal Taxable,
    string? County,
    decimal SurtaxRate,
    decimal ComputedTax,
    decimal SurtaxPortion,
    decimal TaxCharged,
    decimal TaxDue,
    decimal OverItemCap,
    Dr15SurtaxClass SurtaxClass,
    string Treatment);

public sealed record Dr15UseTaxInput(string Reference, DateOnly Date, decimal Amount, string County, decimal OtherStateTaxPaid);

public sealed record Dr15UseTaxResult(
    string Reference,
    decimal Taxable,
    string County,
    decimal SurtaxRate,
    decimal Tax,
    decimal SurtaxPortion,
    decimal OtherStateCredit,
    decimal OverItemCap,
    Dr15SurtaxClass SurtaxClass);

public sealed record Dr15Adjustments(
    decimal TaxRefundedToBuyers = 0m,
    decimal OtherDeductions = 0m,
    decimal DeductionsCarriedIn = 0m,
    decimal EstimatedTaxPaidAndCredits = 0m,
    decimal CreditsCarriedIn = 0m,
    decimal EstimatedTaxDueCurrentMonth = 0m);

/// <summary>When and how the return is (or would be) filed and paid. A null PaidOn means paid when filed.</summary>
public sealed record Dr15Timing(DateOnly FiledOn, DateOnly? PaidOn, bool Electronic, bool DonateCollectionAllowance = false);

public sealed record Dr15Request(
    SalesTaxPeriod Period,
    string BusinessCounty,
    IReadOnlyList<Dr15SaleInput> Sales,
    IReadOnlyList<Dr15UseTaxInput> UseTax,
    Dr15Adjustments Adjustments,
    Dr15Timing Timing);

public sealed record Dr15Row(decimal? Gross, decimal? Exempt, decimal Taxable, decimal TaxDue);

public sealed record Dr15Lines(
    Dr15Row LineA,
    Dr15Row LineB,
    Dr15Row LineC,
    Dr15Row LineD,
    Dr15Row LineE,
    decimal Line5,
    decimal Line6,
    decimal Line7,
    decimal Line8,
    decimal Line9,
    decimal Line10,
    decimal Line11,
    decimal Line12,
    decimal Line13,
    decimal Line14,
    decimal Line15a,
    decimal Line15b,
    decimal Line15c,
    decimal Line15d,
    decimal Line16,
    decimal Line17,
    decimal Line18,
    decimal Line19,
    decimal Line20,
    decimal Line21,
    decimal SurtaxRate);

public sealed record Dr15Timeliness(
    bool ReturnLate,
    bool PaymentLate,
    bool CollectionAllowanceEarned,
    DateOnly PaidOn,
    FloridaInterest.Result Interest);

public sealed record Dr15Result(
    Dr15Lines Lines,
    IReadOnlyList<Dr15SaleResult> Sales,
    IReadOnlyList<Dr15UseTaxResult> UseTax,
    Dr15Timeliness Timeliness,
    decimal DeductionsCarriedOut,
    decimal CreditsCarriedOut,
    IReadOnlyList<Dr15Issue> Issues)
{
    public bool HasBlockingIssues => Issues.Any(issue => issue.Severity == Dr15IssueSeverity.Blocking);
}

/// <summary>
/// Florida DR-15 (R. 01/25) from resolved transactions, following DR-15N (R. 10/25) line by line. Pure: the caller
/// decides what is a reportable sale and where it was delivered; this decides what the return says.
/// </summary>
public static class Dr15Calculator
{
    public const decimal CollectionAllowanceRate = 0.025m;
    public const decimal CollectionAllowanceBase = 1200m;
    public const decimal CollectionAllowanceMax = 30m;
    public const decimal PenaltyRate = 0.10m;
    public const decimal MinimumPenalty = 50m;

    public static Dr15Result Compute(Dr15Request request)
    {
        string businessCounty = FloridaSurtax.Canonical(request.BusinessCounty)
                                ?? throw new ArgumentException($"Unknown Florida county '{request.BusinessCounty}'.", nameof(request));
        var issues = new List<Dr15Issue>();
        decimal businessRate = FloridaSurtax.RateOn(businessCounty, request.Period.End);

        if (!FloridaSurtax.Covers(request.Period.Start) || !FloridaSurtax.Covers(request.Period.End))
        {
            issues.Add(new(Dr15IssueSeverity.Warning, "surtax_table_out_of_range",
                $"Surtax rates are loaded from {FloridaSurtax.TableSource}. Check DR-15DSS for {request.Period.Start.Year} before filing."));
        }

        List<Dr15SaleResult> sales = request.Sales.Select(sale => ComputeSale(sale, businessCounty, issues)).ToList();
        List<Dr15UseTaxResult> useTax = request.UseTax.Select(entry => ComputeUseTax(entry, businessCounty, issues)).ToList();

        var lineA = new Dr15Row(
            sales.Sum(sale => sale.Gross),
            sales.Sum(sale => sale.Exempt),
            sales.Sum(sale => sale.Taxable),
            sales.Sum(sale => sale.TaxDue));
        var lineB = new Dr15Row(null, null, useTax.Sum(entry => entry.Taxable), useTax.Sum(entry => entry.Tax));
        var zero = new Dr15Row(0m, 0m, 0m, 0m);

        decimal line5 = lineA.TaxDue + lineB.TaxDue;

        Dr15Adjustments adjustments = request.Adjustments;
        decimal otherStateCredits = useTax.Sum(entry => entry.OtherStateCredit);
        decimal deductionsAvailable = adjustments.TaxRefundedToBuyers + adjustments.OtherDeductions + adjustments.DeductionsCarriedIn + otherStateCredits;
        decimal line6 = Math.Min(deductionsAvailable, line5);
        if (deductionsAvailable > line5)
        {
            issues.Add(new(Dr15IssueSeverity.Info, "deductions_capped",
                $"Lawful deductions of {deductionsAvailable:0.00} exceed tax due; {deductionsAvailable - line5:0.00} carries to the next return (DR-15N, Line 6)."));
        }

        decimal line7 = line5 - line6;
        decimal creditsAvailable = adjustments.EstimatedTaxPaidAndCredits + adjustments.CreditsCarriedIn;
        decimal line8 = Math.Min(creditsAvailable, line7);
        if (creditsAvailable > line7)
        {
            issues.Add(new(Dr15IssueSeverity.Info, "credits_capped",
                $"Estimated tax and credit memos of {creditsAvailable:0.00} exceed net tax due; {creditsAvailable - line7:0.00} carries to the next return (DR-15N, Line 8)."));
        }

        decimal line9 = adjustments.EstimatedTaxDueCurrentMonth;
        decimal line10 = Math.Max(0m, line7 - line8 + line9);

        Dr15Timeliness timeliness = Timeliness(request.Period, request.Timing, line10);
        decimal line11 = timeliness.CollectionAllowanceEarned && !request.Timing.DonateCollectionAllowance
            ? Math.Min(CollectionAllowanceMax, FloridaTaxMath.Cents(Math.Min(line10, CollectionAllowanceBase) * CollectionAllowanceRate))
            : 0m;
        bool late = timeliness.ReturnLate || timeliness.PaymentLate;
        decimal line12 = late ? Math.Max(MinimumPenalty, FloridaTaxMath.Cents(line10 * PenaltyRate)) : 0m;
        decimal line13 = timeliness.PaymentLate ? timeliness.Interest.Amount : 0m;
        decimal line14 = late ? line10 + line12 + line13 : line10 - line11;

        if (timeliness.Interest.Estimated)
        {
            issues.Add(new(Dr15IssueSeverity.Warning, "interest_rate_estimated",
                $"Interest after {FloridaInterest.Intervals[^1].To:MM/dd/yyyy} uses the last published rate; eServices computes the exact amount."));
        }

        var lines = new Dr15Lines(
            lineA, lineB, zero, zero, zero,
            line5, line6, line7, line8, line9, line10, line11, line12, line13, line14,
            Line15a: sales.Sum(sale => sale.OverItemCap) + useTax.Sum(entry => entry.OverItemCap),
            Line15b: sales.Where(sale => sale.SurtaxClass == Dr15SurtaxClass.NotSubjectToSurtax).Sum(sale => sale.Taxable)
                     + useTax.Where(entry => entry.SurtaxClass == Dr15SurtaxClass.NotSubjectToSurtax).Sum(entry => entry.Taxable),
            Line15c: sales.Where(sale => sale.SurtaxClass == Dr15SurtaxClass.DifferentRate).Sum(sale => sale.Taxable - sale.OverItemCap)
                     + useTax.Where(entry => entry.SurtaxClass == Dr15SurtaxClass.DifferentRate).Sum(entry => entry.Taxable - entry.OverItemCap),
            Line15d: sales.Sum(sale => sale.SurtaxPortion) + useTax.Sum(entry => entry.SurtaxPortion),
            Line16: 0m, Line17: 0m, Line18: 0m, Line19: 0m, Line20: 0m, Line21: 0m,
            SurtaxRate: businessRate);

        return new Dr15Result(
            lines,
            sales,
            useTax,
            timeliness,
            DeductionsCarriedOut: deductionsAvailable - line6,
            CreditsCarriedOut: creditsAvailable - line8,
            issues);
    }

    /// <summary>
    /// The return is late after the late-after date; an electronic payment is late after 5 p.m. ET on the business
    /// day before it. The allowance needs both on time and both electronic (DR-15N, Lines 11–13).
    /// </summary>
    public static Dr15Timeliness Timeliness(SalesTaxPeriod period, Dr15Timing timing, decimal amountDue)
    {
        DateOnly paidOn = timing.PaidOn ?? timing.FiledOn;
        bool returnLate = timing.FiledOn > period.LateAfter;
        DateOnly paymentDeadline = timing.Electronic ? period.ElectronicPaymentDeadline : period.LateAfter;
        bool paymentLate = amountDue > 0 && paidOn > paymentDeadline;
        FloridaInterest.Result interest = paymentLate ? FloridaInterest.Compute(amountDue, period.LateAfter, paidOn) : new(0m, 0, []);
        bool allowance = timing.Electronic && !returnLate && !paymentLate;
        return new Dr15Timeliness(returnLate, paymentLate, allowance, paidOn, interest);
    }

    private static Dr15SaleResult ComputeSale(Dr15SaleInput sale, string businessCounty, List<Dr15Issue> issues)
    {
        decimal items = sale.ItemAmounts.Sum();
        decimal gross = items + sale.ShippingCharged;

        if (sale.ItemAmounts.Any(amount => amount < 0) || sale.ShippingCharged < 0 || sale.TaxCharged < 0)
        {
            issues.Add(new(Dr15IssueSeverity.Blocking, "negative_amount", "A sale has a negative amount; correct the sale before filing.", sale.Reference));
        }

        if (sale.Exemption != Dr15Exemption.None || sale.Destination == Dr15Destination.OutsideFlorida)
        {
            if (sale.TaxCharged > 0)
            {
                issues.Add(new(Dr15IssueSeverity.Warning, "tax_charged_on_exempt_sale",
                    $"{sale.TaxCharged:0.00} tax was charged on a sale treated as exempt. If it was Florida tax it is owed (s. 213.756, F.S.) unless refunded to the buyer.",
                    sale.Reference));
            }

            string treatment = sale.Exemption switch
            {
                Dr15Exemption.Resale => "Exempt: sale for resale",
                Dr15Exemption.ExemptOrganization => "Exempt: exempt organization (DR-14)",
                Dr15Exemption.Other => "Exempt: other",
                _ => "Exempt: delivered outside Florida",
            };
            return new Dr15SaleResult(sale.Reference, gross, gross, 0m, sale.County, 0m, 0m, 0m, sale.TaxCharged, 0m, 0m, Dr15SurtaxClass.None, treatment);
        }

        string? county = FloridaSurtax.Canonical(sale.County);
        if (sale.Destination == Dr15Destination.Unknown || county is null)
        {
            issues.Add(new(Dr15IssueSeverity.Blocking, "destination_unknown",
                "A Florida-taxable sale has no delivery county. Choose where it was delivered so the surtax is right.", sale.Reference));
            county = businessCounty;
        }

        decimal shippingTaxable = sale.ShippingTaxable ? sale.ShippingCharged : 0m;
        decimal taxable = items + shippingTaxable;
        decimal surtaxRate = FloridaSurtax.RateOn(county, sale.Date);
        decimal surtaxBase = sale.ItemAmounts.Sum(amount => Math.Min(amount, FloridaSurtax.ItemCap)) + shippingTaxable;
        decimal overCap = sale.ItemAmounts.Sum(amount => Math.Max(0m, amount - FloridaSurtax.ItemCap));
        decimal surtax = FloridaTaxMath.Cents(surtaxBase * surtaxRate);
        decimal computed = FloridaTaxMath.Cents(taxable * FloridaSurtax.StateRate + surtaxBase * surtaxRate);
        decimal due = Math.Max(computed, sale.TaxCharged);

        if (sale.TaxCharged < computed)
        {
            issues.Add(new(Dr15IssueSeverity.Warning, "tax_under_collected",
                $"Charged {sale.TaxCharged:0.00} but {computed:0.00} is due; the dealer owes the full amount.", sale.Reference));
        }
        else if (sale.TaxCharged > computed)
        {
            issues.Add(new(Dr15IssueSeverity.Warning, "tax_over_collected",
                $"Charged {sale.TaxCharged:0.00} against {computed:0.00} due; tax collected is held in trust and owed unless refunded (s. 213.756, F.S.).",
                sale.Reference));
        }

        decimal businessRate = FloridaSurtax.RateOn(businessCounty, sale.Date);
        Dr15SurtaxClass surtaxClass = surtaxRate == 0m ? Dr15SurtaxClass.NotSubjectToSurtax
            : surtaxRate != businessRate ? Dr15SurtaxClass.DifferentRate
            : Dr15SurtaxClass.BusinessCountyRate;
        decimal exempt = sale.ShippingCharged - shippingTaxable;

        return new Dr15SaleResult(
            sale.Reference,
            gross,
            exempt,
            taxable,
            county,
            surtaxRate,
            computed,
            surtaxClass == Dr15SurtaxClass.NotSubjectToSurtax ? 0m : surtax,
            sale.TaxCharged,
            due,
            surtaxClass == Dr15SurtaxClass.NotSubjectToSurtax ? 0m : overCap,
            surtaxClass,
            exempt > 0 ? "Taxable; optional shipping excluded" : "Taxable");
    }

    private static Dr15UseTaxResult ComputeUseTax(Dr15UseTaxInput entry, string businessCounty, List<Dr15Issue> issues)
    {
        string county = FloridaSurtax.Canonical(entry.County) ?? businessCounty;
        if (FloridaSurtax.Canonical(entry.County) is null)
        {
            issues.Add(new(Dr15IssueSeverity.Warning, "use_tax_county_defaulted",
                $"Use tax entry has no recognised county; the business county ({businessCounty}) was used.", entry.Reference));
        }

        decimal surtaxRate = FloridaSurtax.RateOn(county, entry.Date);
        decimal surtaxBase = Math.Min(entry.Amount, FloridaSurtax.ItemCap);
        decimal tax = FloridaTaxMath.Cents(entry.Amount * FloridaSurtax.StateRate + surtaxBase * surtaxRate);
        decimal businessRate = FloridaSurtax.RateOn(businessCounty, entry.Date);
        Dr15SurtaxClass surtaxClass = surtaxRate == 0m ? Dr15SurtaxClass.NotSubjectToSurtax
            : surtaxRate != businessRate ? Dr15SurtaxClass.DifferentRate
            : Dr15SurtaxClass.BusinessCountyRate;

        return new Dr15UseTaxResult(
            entry.Reference,
            entry.Amount,
            county,
            surtaxRate,
            tax,
            surtaxClass == Dr15SurtaxClass.NotSubjectToSurtax ? 0m : FloridaTaxMath.Cents(surtaxBase * surtaxRate),
            Math.Min(Math.Max(0m, entry.OtherStateTaxPaid), tax),
            surtaxClass == Dr15SurtaxClass.NotSubjectToSurtax ? 0m : Math.Max(0m, entry.Amount - FloridaSurtax.ItemCap),
            surtaxClass);
    }
}
