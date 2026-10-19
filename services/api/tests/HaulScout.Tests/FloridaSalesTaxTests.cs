using HaulScout.Domain.Tax;

namespace HaulScout.Tests;

/// <summary>Pinned to the Department's own published numbers: DR-659 (2026), DR-15N, TIP 26ADM-02, DR-15DSS (2026).</summary>
public class FloridaSalesTaxTests
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    private static readonly SalesTaxFrequencySpan[] Quarterly =
        [new(D(2026, 4, 1), SalesTaxFrequency.Quarterly, SalesTaxPeriodAlignment.Calendar)];

    [Fact]
    public void Holidays_2026_match_the_DR659_schedule()
    {
        DateOnly[] expected =
        [
            D(2026, 1, 1), D(2026, 1, 19), D(2026, 2, 16), D(2026, 5, 25), D(2026, 6, 19), D(2026, 7, 3),
            D(2026, 9, 7), D(2026, 10, 12), D(2026, 11, 11), D(2026, 11, 26), D(2026, 11, 27), D(2026, 12, 25),
        ];
        var actual = new List<DateOnly>();
        for (DateOnly day = D(2026, 1, 1); day <= D(2026, 12, 31); day = day.AddDays(1))
        {
            if (FloridaHolidays.IsHoliday(day)) actual.Add(day);
        }

        Assert.Equal(expected, actual);
        Assert.True(FloridaHolidays.IsHoliday(D(2027, 1, 1)));
    }

    [Fact]
    public void Electronic_payment_deadlines_2026_match_DR659_for_every_month()
    {
        int[] dr659 = [16, 19, 19, 17, 19, 18, 17, 19, 18, 19, 19, 18];
        for (int month = 1; month <= 12; month++)
        {
            DateOnly periodEnd = D(2026, month, 1).AddDays(-1);
            SalesTaxPeriod period = FloridaFilingCalendar.Build(
                new DateOnly(periodEnd.Year, periodEnd.Month, 1), periodEnd, SalesTaxFrequency.Monthly, SalesTaxPeriodAlignment.Calendar, false);
            Assert.Equal(D(2026, month, dr659[month - 1]), period.ElectronicPaymentDeadline);
            Assert.Equal(D(2026, month, 1), period.DueDate);
        }
    }

    [Theory]
    [InlineData(2026, 5, 2026, 6, 22)] // 20th is a Saturday
    [InlineData(2026, 8, 2026, 9, 21)] // Sunday
    [InlineData(2026, 9, 2026, 10, 20)]
    [InlineData(2026, 12, 2027, 1, 20)]
    public void Late_after_moves_past_weekends(int py, int pm, int y, int m, int d)
    {
        DateOnly start = D(py, pm, 1);
        SalesTaxPeriod period = FloridaFilingCalendar.Build(start, start.AddMonths(1).AddDays(-1), SalesTaxFrequency.Monthly, SalesTaxPeriodAlignment.Calendar, false);
        Assert.Equal(D(y, m, d), period.LateAfter);
    }

    [Fact]
    public void Quarterly_periods_start_with_the_quarter_holding_the_registration_date()
    {
        IReadOnlyList<SalesTaxPeriod> periods = FloridaFilingCalendar.Periods(D(2026, 5, 31), Quarterly, D(2026, 10, 19));

        Assert.Equal(3, periods.Count); // the third is the open quarter holding today
        Assert.Equal((D(2026, 10, 1), D(2026, 12, 31)), (periods[2].Start, periods[2].End));
        Assert.Equal((D(2026, 4, 1), D(2026, 6, 30)), (periods[0].Start, periods[0].End));
        Assert.Equal(D(2026, 7, 20), periods[0].LateAfter);
        Assert.Equal((D(2026, 7, 1), D(2026, 9, 30)), (periods[1].Start, periods[1].End));
        Assert.Equal(D(2026, 10, 1), periods[1].DueDate);
        Assert.Equal(D(2026, 10, 20), periods[1].LateAfter);
        Assert.Equal(D(2026, 10, 19), periods[1].ElectronicPaymentDeadline);
    }

    [Fact]
    public void Alternate_alignment_ends_quarters_in_Feb_May_Aug_Nov_and_halves_in_May_Nov()
    {
        Assert.Equal((D(2026, 6, 1), D(2026, 8, 31)), FloridaFilingCalendar.Containing(D(2026, 8, 15), SalesTaxFrequency.Quarterly, SalesTaxPeriodAlignment.Alternate));
        Assert.Equal((D(2025, 12, 1), D(2026, 2, 28)), FloridaFilingCalendar.Containing(D(2026, 1, 5), SalesTaxFrequency.Quarterly, SalesTaxPeriodAlignment.Alternate));
        Assert.Equal((D(2026, 6, 1), D(2026, 11, 30)), FloridaFilingCalendar.Containing(D(2026, 8, 15), SalesTaxFrequency.Semiannual, SalesTaxPeriodAlignment.Alternate));
        Assert.Equal((D(2026, 7, 1), D(2026, 12, 31)), FloridaFilingCalendar.Containing(D(2026, 8, 15), SalesTaxFrequency.Semiannual, SalesTaxPeriodAlignment.Calendar));
        Assert.Equal((D(2026, 1, 1), D(2026, 12, 31)), FloridaFilingCalendar.Containing(D(2026, 8, 15), SalesTaxFrequency.Annual, SalesTaxPeriodAlignment.Calendar));
    }

    [Fact]
    public void A_frequency_change_starts_a_new_run_of_periods()
    {
        SalesTaxFrequencySpan[] schedule =
        [
            new(D(2026, 4, 1), SalesTaxFrequency.Quarterly, SalesTaxPeriodAlignment.Calendar),
            new(D(2027, 1, 1), SalesTaxFrequency.Annual, SalesTaxPeriodAlignment.Calendar),
        ];
        IReadOnlyList<SalesTaxPeriod> periods = FloridaFilingCalendar.Periods(D(2026, 5, 31), schedule, D(2027, 3, 1));

        Assert.Equal(4, periods.Count);
        Assert.Equal((D(2027, 1, 1), D(2027, 12, 31), SalesTaxFrequency.Annual), (periods[3].Start, periods[3].End, periods[3].Frequency));
        Assert.Equal(D(2028, 1, 20), periods[3].LateAfter);
    }

    [Fact]
    public void Closing_the_business_produces_a_final_return_late_fifteen_days_after()
    {
        IReadOnlyList<SalesTaxPeriod> periods = FloridaFilingCalendar.Periods(D(2026, 5, 31), Quarterly, D(2026, 12, 31), closedOn: D(2026, 8, 10));

        SalesTaxPeriod final = periods[^1];
        Assert.True(final.IsFinal);
        Assert.Equal((D(2026, 7, 1), D(2026, 8, 10)), (final.Start, final.End));
        Assert.Equal(D(2026, 8, 25), final.LateAfter);
    }

    [Theory]
    [InlineData(0, SalesTaxFrequency.Annual)]
    [InlineData(100, SalesTaxFrequency.Annual)]
    [InlineData(100.01, SalesTaxFrequency.Semiannual)]
    [InlineData(500, SalesTaxFrequency.Semiannual)]
    [InlineData(1000, SalesTaxFrequency.Quarterly)]
    [InlineData(1000.01, SalesTaxFrequency.Monthly)]
    public void Eligible_frequency_follows_s212_11(decimal tax, SalesTaxFrequency expected) =>
        Assert.Equal(expected, FloridaFilingCalendar.EligibleFrequency(tax));

    [Fact]
    public void Rounding_follows_the_DR15N_example() =>
        Assert.Equal(3.03m, FloridaTaxMath.Cents(50.45m * 0.06m));

    [Fact]
    public void Interest_matches_TIP_26ADM02_example_one()
    {
        FloridaInterest.Result result = FloridaInterest.Compute(2000m, D(2026, 2, 20), D(2026, 4, 8));
        Assert.Equal(47, result.Days);
        Assert.Equal(28.33m, result.Amount);
    }

    [Fact]
    public void Interest_matches_TIP_26ADM02_example_two_across_a_rate_change()
    {
        FloridaInterest.Result result = FloridaInterest.Compute(5000m, D(2025, 10, 20), D(2026, 2, 12));
        Assert.Equal([72, 43], result.Segments.Select(segment => segment.Days));
        Assert.Equal([118.36m, 64.79m], result.Segments.Select(segment => segment.Amount));
        Assert.Equal(183.15m, result.Amount);
        Assert.False(result.Estimated);
    }

    [Fact]
    public void Interest_past_the_published_table_is_marked_estimated()
    {
        FloridaInterest.Result result = FloridaInterest.Compute(1000m, D(2026, 12, 21), D(2027, 1, 10));
        Assert.Equal(20, result.Days);
        Assert.True(result.Estimated);
    }

    [Fact]
    public void Surtax_totals_match_DR15DSS_2026_for_all_67_counties()
    {
        var totals = new Dictionary<string, decimal>
        {
            ["Alachua"] = .015m,
            ["Baker"] = .01m,
            ["Bay"] = .01m,
            ["Bradford"] = .01m,
            ["Brevard"] = .01m,
            ["Broward"] = .01m,
            ["Calhoun"] = .015m,
            ["Charlotte"] = .01m,
            ["Citrus"] = 0m,
            ["Clay"] = .015m,
            ["Collier"] = 0m,
            ["Columbia"] = .015m,
            ["DeSoto"] = .015m,
            ["Dixie"] = .01m,
            ["Duval"] = .015m,
            ["Escambia"] = .015m,
            ["Flagler"] = .01m,
            ["Franklin"] = .015m,
            ["Gadsden"] = .015m,
            ["Gilchrist"] = .01m,
            ["Glades"] = .01m,
            ["Gulf"] = .01m,
            ["Hamilton"] = .02m,
            ["Hardee"] = .01m,
            ["Hendry"] = .015m,
            ["Hernando"] = .005m,
            ["Highlands"] = .015m,
            ["Hillsborough"] = .015m,
            ["Holmes"] = .015m,
            ["Indian River"] = .01m,
            ["Jackson"] = .015m,
            ["Jefferson"] = .01m,
            ["Lafayette"] = .01m,
            ["Lake"] = .01m,
            ["Lee"] = .005m,
            ["Leon"] = .015m,
            ["Levy"] = .01m,
            ["Liberty"] = .015m,
            ["Madison"] = .015m,
            ["Manatee"] = .01m,
            ["Marion"] = .015m,
            ["Martin"] = .005m,
            ["Miami-Dade"] = .01m,
            ["Monroe"] = .015m,
            ["Nassau"] = .01m,
            ["Okaloosa"] = .01m,
            ["Okeechobee"] = .01m,
            ["Orange"] = .005m,
            ["Osceola"] = .015m,
            ["Palm Beach"] = .005m,
            ["Pasco"] = .01m,
            ["Pinellas"] = .01m,
            ["Polk"] = .01m,
            ["Putnam"] = .01m,
            ["St. Johns"] = .005m,
            ["St. Lucie"] = .01m,
            ["Santa Rosa"] = .01m,
            ["Sarasota"] = .01m,
            ["Seminole"] = .01m,
            ["Sumter"] = .01m,
            ["Suwannee"] = .01m,
            ["Taylor"] = .01m,
            ["Union"] = .01m,
            ["Volusia"] = .005m,
            ["Wakulla"] = .015m,
            ["Walton"] = .01m,
            ["Washington"] = .015m,
        };

        Assert.Equal(67, FloridaSurtax.Counties.Count);
        Assert.Equal(totals.Keys.Order(StringComparer.Ordinal), FloridaSurtax.Counties);
        foreach ((string county, decimal rate) in totals)
        {
            Assert.True(rate == FloridaSurtax.RateOn(county, D(2026, 6, 30)), county);
        }
    }

    [Fact]
    public void Surtax_components_expire_on_their_dates()
    {
        Assert.Equal(.01m, FloridaSurtax.RateOn("Brevard", D(2026, 12, 31)));
        Assert.Equal(0m, FloridaSurtax.RateOn("Brevard", D(2027, 1, 1)));
        Assert.Equal(.005m, FloridaSurtax.RateOn("Palm Beach", D(2026, 1, 1)));
        Assert.Equal(0m, FloridaSurtax.RateOn("Palm Beach", D(2025, 12, 31)));
        Assert.Equal("Miami-Dade", FloridaSurtax.Canonical("miami dade county"));
        Assert.Equal("St. Johns", FloridaSurtax.Canonical("St Johns"));
    }

    private static SalesTaxPeriod Q3 => FloridaFilingCalendar.Periods(D(2026, 5, 31), Quarterly, D(2026, 9, 30))[^1];

    private static Dr15Timing OnTime => new(D(2026, 10, 5), null, Electronic: true);

    private static Dr15Result Compute(Dr15SaleInput[]? sales = null, Dr15UseTaxInput[]? useTax = null, Dr15Adjustments? adjustments = null, Dr15Timing? timing = null) =>
        Dr15Calculator.Compute(new Dr15Request(Q3, "Manatee", sales ?? [], useTax ?? [], adjustments ?? new(), timing ?? OnTime));

    private static Dr15SaleInput Sale(decimal item, string? county = "Manatee", decimal shipping = 0m, decimal taxCharged = -1m,
        Dr15Destination destination = Dr15Destination.FloridaCounty, Dr15Exemption exemption = Dr15Exemption.None, bool shippingTaxable = false) =>
        new("s", D(2026, 8, 1), [item], shipping, taxCharged < 0 ? 0m : taxCharged, destination, county, exemption, shippingTaxable);

    [Fact]
    public void A_period_with_only_marketplace_sales_is_a_zero_return()
    {
        Dr15Result result = Compute();
        Assert.Equal(0m, result.Lines.Line5);
        Assert.Equal(0m, result.Lines.Line14);
        Assert.Equal(.01m, result.Lines.SurtaxRate);
        Assert.False(result.HasBlockingIssues);
    }

    [Fact]
    public void A_late_zero_return_still_owes_the_50_dollar_minimum_penalty()
    {
        Dr15Result result = Compute(timing: new Dr15Timing(D(2026, 10, 21), null, true));
        Assert.True(result.Timeliness.ReturnLate);
        Assert.Equal(50m, result.Lines.Line12);
        Assert.Equal(0m, result.Lines.Line13);
        Assert.Equal(50m, result.Lines.Line14);
    }

    [Fact]
    public void A_home_county_sale_with_optional_shipping()
    {
        Dr15Result result = Compute([Sale(100m, shipping: 10m, taxCharged: 7m)]);

        Assert.Equal(new Dr15Row(110m, 10m, 100m, 7m), result.Lines.LineA);
        Assert.Equal(1m, result.Lines.Line15d);
        Assert.Equal(0m, result.Lines.Line15c);
        Assert.Equal(7m, result.Lines.Line10);
        Assert.Equal(.18m, result.Lines.Line11);
        Assert.Equal(6.82m, result.Lines.Line14);
        Assert.DoesNotContain(result.Issues, issue => issue.Severity != Dr15IssueSeverity.Info);
    }

    [Fact]
    public void A_sale_into_a_different_rate_county_lands_on_15c()
    {
        Dr15Result result = Compute([Sale(100m, "Hillsborough", taxCharged: 7.5m)]);
        Assert.Equal(7.5m, result.Lines.LineA.TaxDue);
        Assert.Equal(100m, result.Lines.Line15c);
        Assert.Equal(1.5m, result.Lines.Line15d);
    }

    [Fact]
    public void A_sale_into_a_county_without_surtax_lands_on_15b()
    {
        Dr15Result result = Compute([Sale(100m, "Collier", taxCharged: 6m)]);
        Assert.Equal(6m, result.Lines.LineA.TaxDue);
        Assert.Equal(100m, result.Lines.Line15b);
        Assert.Equal(0m, result.Lines.Line15d);
    }

    [Fact]
    public void Surtax_stops_at_5000_per_item_and_the_excess_goes_on_15a()
    {
        Dr15Result result = Compute([Sale(7000m, taxCharged: 470m)]);
        Assert.Equal(470m, result.Lines.LineA.TaxDue);
        Assert.Equal(2000m, result.Lines.Line15a);
        Assert.Equal(50m, result.Lines.Line15d);
    }

    [Fact]
    public void Out_of_state_sales_are_exempt_and_tax_charged_on_them_is_flagged()
    {
        Dr15Result result = Compute([Sale(80m, null, shipping: 5m, taxCharged: 4m, destination: Dr15Destination.OutsideFlorida)]);
        Assert.Equal(new Dr15Row(85m, 85m, 0m, 0m), result.Lines.LineA);
        Assert.Contains(result.Issues, issue => issue.Code == "tax_charged_on_exempt_sale");
    }

    [Fact]
    public void Over_collected_tax_is_owed_and_under_collected_tax_is_still_due()
    {
        Dr15Result over = Compute([Sale(100m, taxCharged: 7.25m)]);
        Assert.Equal(7.25m, over.Lines.LineA.TaxDue);
        Assert.Contains(over.Issues, issue => issue.Code == "tax_over_collected");

        Dr15Result under = Compute([Sale(100m, taxCharged: 6m)]);
        Assert.Equal(7m, under.Lines.LineA.TaxDue);
        Assert.Contains(under.Issues, issue => issue.Code == "tax_under_collected");
    }

    [Fact]
    public void A_florida_sale_without_a_county_blocks_the_return()
    {
        Dr15Result result = Compute([Sale(100m, null, destination: Dr15Destination.Unknown)]);
        Assert.True(result.HasBlockingIssues);
    }

    [Fact]
    public void Use_tax_is_line_B_at_the_business_county_rate_with_other_state_credit_on_line_6()
    {
        Dr15Result result = Compute(useTax: [new Dr15UseTaxInput("u", D(2026, 8, 3), 200m, "Manatee", 4m)]);
        Assert.Equal(new Dr15Row(null, null, 200m, 14m), result.Lines.LineB);
        Assert.Equal(14m, result.Lines.Line5);
        Assert.Equal(4m, result.Lines.Line6);
        Assert.Equal(10m, result.Lines.Line7);
    }

    [Fact]
    public void Collection_allowance_is_capped_at_30_and_needs_electronic_and_on_time()
    {
        Dr15Result big = Compute([Sale(100_000m / 1.07m, taxCharged: 0m)]);
        Assert.Equal(30m, big.Lines.Line11);

        Dr15Result paper = Compute([Sale(100m, taxCharged: 7m)], timing: new Dr15Timing(D(2026, 10, 5), null, Electronic: false));
        Assert.Equal(0m, paper.Lines.Line11);
        Assert.Equal(7m, paper.Lines.Line14);

        Dr15Result donated = Compute([Sale(100m, taxCharged: 7m)], timing: OnTime with { DonateCollectionAllowance = true });
        Assert.Equal(0m, donated.Lines.Line11);
    }

    [Fact]
    public void An_electronic_payment_on_the_late_after_date_is_late()
    {
        Dr15Result result = Compute([Sale(100m, taxCharged: 7m)], timing: new Dr15Timing(D(2026, 10, 19), D(2026, 10, 20), true));
        Assert.False(result.Timeliness.ReturnLate);
        Assert.True(result.Timeliness.PaymentLate);
        Assert.Equal(50m, result.Lines.Line12);
        Assert.Equal(0m, result.Lines.Line11);
    }

    [Fact]
    public void Late_payment_adds_ten_percent_penalty_and_daily_interest()
    {
        Dr15Result result = Compute([Sale(10_000m, taxCharged: 650m)], timing: new Dr15Timing(D(2026, 10, 30), null, true));
        Assert.Equal(650m, result.Lines.Line10);
        Assert.Equal(65m, result.Lines.Line12);
        Assert.Equal(1.96m, result.Lines.Line13); // 650 × 10 days × 0.000301370
        Assert.Equal(716.96m, result.Lines.Line14);
    }

    [Fact]
    public void Deductions_beyond_tax_due_are_capped_and_carried_forward()
    {
        Dr15Result result = Compute([Sale(100m, taxCharged: 7m)], adjustments: new Dr15Adjustments(TaxRefundedToBuyers: 10m));
        Assert.Equal(7m, result.Lines.Line6);
        Assert.Equal(0m, result.Lines.Line7);
        Assert.Equal(3m, result.DeductionsCarriedOut);
        Assert.Contains(result.Issues, issue => issue.Code == "deductions_capped");
    }
}
