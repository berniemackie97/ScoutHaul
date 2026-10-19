using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Application.Tax;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Domain.Tax;
using HaulScout.Infrastructure.Persistence;
using HaulScout.Infrastructure.Tax;

namespace HaulScout.Tests;

/// <summary>The tax center end to end over the real EF store: periods owed, what lands on the return, filing and drift.</summary>
public class SalesTaxWorkflowTests
{
    // 11:00 ET on Monday Oct 19, 2026: Q3's e-pay deadline day, the day before it is late.
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 15, 0, 0, TimeSpan.Zero);
    private static readonly string Q2 = "20260401-20260630";
    private static readonly string Q3 = "20260701-20260930";

    private sealed class StubCurrentUser(Guid organizationId) : ICurrentUserContext
    {
        public Guid OrganizationId => organizationId;
        public Guid? ActiveOrganizationId => organizationId;
        public Guid UserId { get; } = Guid.NewGuid();
        public Task ResolveAsync() => Task.CompletedTask;
    }

    private sealed class Harness(HaulScoutDbContext db, Guid org, SalesTaxWorkflowService service, FakeTimeProvider clock)
    {
        public HaulScoutDbContext Db { get; } = db;
        public Guid Org { get; } = org;
        public SalesTaxWorkflowService Service { get; } = service;
        public FakeTimeProvider Clock { get; } = clock;

        public Sale AddSale(string platform, DateTimeOffset soldAt, decimal price, decimal tax = 0m, bool remitted = true, Order? order = null, decimal shipping = 0m)
        {
            var item = new InventoryItem { OrganizationId = Org, Title = $"{platform} item {price}", Status = InventoryStatus.Sold };
            Db.InventoryItems.Add(item);
            var sale = new Sale
            {
                OrganizationId = Org,
                InventoryItemId = item.Id,
                Platform = platform,
                SoldPrice = price,
                ShippingCharged = shipping,
                SalesTaxCollected = tax,
                TaxRemittedByPlatform = remitted,
                SoldAt = soldAt,
                Status = SaleStatus.Delivered,
                OrderId = order?.Id,
            };
            if (order is not null)
            {
                Db.OrderLines.Add(new OrderLine { OrganizationId = Org, OrderId = order.Id, InventoryItemId = item.Id, TitleSnapshot = item.Title, UnitPrice = price, LineTotal = price });
            }

            Db.Sales.Add(sale);
            Db.SaveChanges();
            return sale;
        }

        public Order AddOrder(string? region, bool pickup = false, string? country = "US")
        {
            var order = new Order
            {
                OrganizationId = Org,
                OrderNumber = $"SF-{Guid.NewGuid():N}"[..10],
                ShipToRegion = region,
                ShipToCountry = region is null && !pickup ? null : country,
                IsLocalPickup = pickup,
                Status = OrderStatus.Paid,
            };
            Db.Orders.Add(order);
            Db.SaveChanges();
            return order;
        }
    }

    private static async Task<Harness> NewAsync(string name, string zone = "America/New_York", bool configure = true)
    {
        Guid org = Guid.NewGuid();
        DbContextOptions<HaulScoutDbContext> options = new DbContextOptionsBuilder<HaulScoutDbContext>().UseInMemoryDatabase(name).Options;
        var tenant = new TenantContext();
        tenant.SetOrganization(org);
        var db = new HaulScoutDbContext(options, tenant);
        db.Organizations.Add(new Organization { Id = org, Name = "Overhaul Trading Company LLC", TimeZoneId = zone });
        await db.SaveChangesAsync();

        var user = new StubCurrentUser(org);
        var clock = new FakeTimeProvider(Now);
        var service = new SalesTaxWorkflowService(new EfSalesTaxStore(db), user, new EfAuditTrail(db, user), clock);
        var harness = new Harness(db, org, service, clock);
        if (configure)
        {
            AppResult<SalesTaxProfileDto> saved = await service.SaveProfileAsync(Profile(), CancellationToken.None);
            Assert.Equal(AppResultStatus.Ok, saved.Status);
        }

        return harness;
    }

    private static SaveSalesTaxProfileRequest Profile(string? certificate = "78-8012345678-9", IReadOnlyList<SalesTaxFrequencyDto>? frequencies = null) => new(
        certificate, null, "Overhaul Trading Company LLC", null, "1675 55th Avenue Cir E", null, "Bradenton", "34203", "manatee county",
        new DateOnly(2026, 5, 31), null, FilesElectronically: true, DonateCollectionAllowance: false, DeliveryChargesOptional: true, null,
        frequencies ?? [new SalesTaxFrequencyDto(new DateOnly(2026, 4, 1), SalesTaxFrequency.Quarterly, SalesTaxPeriodAlignment.Calendar)]);

    private static DateTimeOffset Et(int month, int day, int hour = 12) => new(2026, month, day, hour, 0, 0, TimeSpan.FromHours(-4));

    private static async Task<SalesTaxWorksheetDto> Worksheet(Harness harness, string key, DateOnly? filedOn = null)
    {
        AppResult<SalesTaxWorksheetDto> result = await harness.Service.GetWorksheetAsync(key, filedOn, CancellationToken.None);
        Assert.Equal(AppResultStatus.Ok, result.Status);
        return result.Value!;
    }

    private static FileSalesTaxReturnRequest Filing(SalesTaxWorksheetDto worksheet, bool amended = false, SalesTaxFilingMethod method = SalesTaxFilingMethod.Electronic) => new(
        new DateOnly(2026, 10, 19), null, method, "CONF-123", worksheet.Lines.Line14, false, amended,
        worksheet.Issues.Count(issue => issue.Severity == Dr15IssueSeverity.Warning), worksheet.SourceFingerprint, null, true, true);

    [Fact]
    public async Task The_overview_knows_which_returns_are_owed_and_which_is_overdue()
    {
        Harness harness = await NewAsync(nameof(The_overview_knows_which_returns_are_owed_and_which_is_overdue));

        SalesTaxOverviewDto overview = await harness.Service.GetOverviewAsync(CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 10, 19), overview.Today);
        Assert.Equal(["Q4 2026 (Oct–Dec 2026)", "Q3 2026 (Jul–Sep 2026)", "Q2 2026 (Apr–Jun 2026)"], overview.Periods.Select(period => period.Label));
        Assert.Equal(SalesTaxPeriodStatus.Open, overview.Periods[0].Status);
        Assert.Equal(SalesTaxPeriodStatus.DueSoon, overview.Periods[1].Status);
        Assert.Equal(1, overview.Periods[1].DaysUntilLateAfter);
        Assert.Equal(SalesTaxPeriodStatus.Overdue, overview.Periods[2].Status);
        Assert.Equal(50m, overview.Periods[2].AmountDueWithReturn);
        Assert.Equal(Q2, overview.NextDue!.Key);
        Assert.Equal(1, overview.OverdueCount);
        Assert.Contains(overview.Notices, notice => notice.Code == "overdue" && notice.PeriodKey == Q2);
        Assert.DoesNotContain(overview.Notices, notice => notice.Code == "time_zone");
    }

    [Fact]
    public async Task Marketplace_sales_are_excluded_and_listed_by_channel()
    {
        Harness harness = await NewAsync(nameof(Marketplace_sales_are_excluded_and_listed_by_channel));
        harness.AddSale("eBay", Et(8, 3), 40m, tax: 2.8m);
        harness.AddSale("Poshmark", Et(9, 30, 23), 25m);
        harness.AddSale("eBay", Et(10, 1, 0), 99m); // just past midnight Eastern: Q4

        SalesTaxWorksheetDto worksheet = await Worksheet(harness, Q3);

        Assert.Equal(0m, worksheet.Lines.Line5);
        Assert.Equal(0m, worksheet.Lines.LineA.Gross);
        Assert.Equal(2, worksheet.ExcludedSales.Count);
        Assert.Equal(["eBay", "Poshmark"], worksheet.ExcludedChannels.Select(channel => channel.Channel));
        Assert.Equal(2.8m, worksheet.ExcludedChannels[0].TaxCollectedByMarketplace);
        Assert.Equal(SalesTaxPeriodBuilder.ExclusionBasis, worksheet.ExcludedChannels[0].Basis);
        Assert.DoesNotContain(worksheet.Issues, issue => issue.Severity != Dr15IssueSeverity.Info);
    }

    [Fact]
    public async Task Storefront_sales_are_reported_with_pickup_taxed_and_out_of_state_exempt()
    {
        Harness harness = await NewAsync(nameof(Storefront_sales_are_reported_with_pickup_taxed_and_out_of_state_exempt));
        harness.AddSale("Storefront", Et(8, 10), 100m, tax: 7m, remitted: false, order: harness.AddOrder(null, pickup: true));
        harness.AddSale("Storefront", Et(8, 11), 50m, remitted: false, order: harness.AddOrder("GA"), shipping: 8m);

        SalesTaxWorksheetDto worksheet = await Worksheet(harness, Q3);

        Assert.Equal(new Dr15Row(158m, 58m, 100m, 7m), worksheet.Lines.LineA);
        Assert.Equal(1m, worksheet.Lines.Line15d);
        Assert.Equal(.18m, worksheet.Lines.Line11);
        Assert.Equal(6.82m, worksheet.Lines.Line14);
        Assert.Contains(worksheet.ReportedSales, sale => sale.DestinationSource == "Local pickup at the business" && sale.County == "Manatee");
        Assert.False(worksheet.Timeliness.ReturnLate);
    }

    [Fact]
    public async Task A_florida_shipment_without_a_county_blocks_until_decided()
    {
        Harness harness = await NewAsync(nameof(A_florida_shipment_without_a_county_blocks_until_decided));
        Sale sale = harness.AddSale("Storefront", Et(8, 10), 100m, tax: 7.5m, remitted: false, order: harness.AddOrder("FL"));

        SalesTaxWorksheetDto before = await Worksheet(harness, Q3);
        Assert.Contains(before.Issues, issue => issue.Code == "destination_unknown" && issue.SaleIds.Contains(sale.Id));
        Assert.Equal(AppResultStatus.Validation, (await harness.Service.FileAsync(Q3, Filing(before), CancellationToken.None)).Status);

        AppResult decided = await harness.Service.SaveSaleDecisionAsync(sale.Id,
            new SaveSalesTaxSaleDecisionRequest(Dr15Destination.FloridaCounty, "Hillsborough", Dr15Exemption.None, null, "Shipped to Tampa"), CancellationToken.None);
        Assert.Equal(AppResultStatus.Ok, decided.Status);

        SalesTaxWorksheetDto after = await Worksheet(harness, Q3);
        Assert.DoesNotContain(after.Issues, issue => issue.Severity == Dr15IssueSeverity.Blocking);
        Assert.Equal(7.5m, after.Lines.LineA.TaxDue);
        Assert.Equal(100m, after.Lines.Line15c);
    }

    [Fact]
    public async Task A_direct_sale_marked_as_marketplace_collected_blocks_until_corrected()
    {
        Harness harness = await NewAsync(nameof(A_direct_sale_marked_as_marketplace_collected_blocks_until_corrected));
        Sale sale = harness.AddSale("Craigslist", Et(7, 20), 60m, remitted: true);

        SalesTaxWorksheetDto before = await Worksheet(harness, Q3);
        Assert.Contains(before.Issues, issue => issue.Code == "collector_mismatch" && issue.Severity == Dr15IssueSeverity.Blocking);

        await harness.Service.SetSaleCollectorAsync(sale.Id, new SetSaleTaxCollectorRequest(false, "Cash sale, nobody collected"), CancellationToken.None);

        SalesTaxWorksheetDto after = await Worksheet(harness, Q3);
        Assert.DoesNotContain(after.Issues, issue => issue.Severity == Dr15IssueSeverity.Blocking);
        Assert.Equal(4.2m, after.Lines.LineA.TaxDue);
        Assert.Contains(after.Issues, issue => issue.Code == "tax_under_collected");
        Assert.Contains(after.Issues, issue => issue.Code == "destination_assumed");
        Assert.True(await harness.Db.AuditLogs.AnyAsync(log => log.Action == "SaleTaxCollectorChanged"));
    }

    [Fact]
    public async Task Filing_snapshots_the_return_and_later_changes_show_as_drift()
    {
        Harness harness = await NewAsync(nameof(Filing_snapshots_the_return_and_later_changes_show_as_drift));
        harness.AddSale("eBay", Et(8, 3), 40m);
        SalesTaxWorksheetDto worksheet = await Worksheet(harness, Q3);

        AppResult<SalesTaxWorksheetDto> stale = await harness.Service.FileAsync(Q3, Filing(worksheet) with { ExpectedFingerprint = "nope" }, CancellationToken.None);
        Assert.Equal(AppResultStatus.Conflict, stale.Status);

        AppResult<SalesTaxWorksheetDto> filed = await harness.Service.FileAsync(Q3, Filing(worksheet, method: SalesTaxFilingMethod.Telefile), CancellationToken.None);
        Assert.Equal(AppResultStatus.Ok, filed.Status);
        Assert.Equal(SalesTaxPeriodStatus.Filed, filed.Value!.Period.Status);
        Assert.Equal(SalesTaxReturnKind.Original, filed.Value.Filings.Single().Kind);

        AppResult<SalesTaxWorksheetDto> twice = await harness.Service.FileAsync(Q3, Filing(filed.Value), CancellationToken.None);
        Assert.Equal(AppResultStatus.Conflict, twice.Status);

        await harness.Service.SaveUseEntryAsync(null,
            new SaveSalesTaxUseEntryRequest(new DateOnly(2026, 9, 2), "Label printer from out-of-state seller, no tax", 200m, null, 0m, null, null, null),
            CancellationToken.None);

        SalesTaxWorksheetDto drifted = await Worksheet(harness, Q3);
        Assert.Equal(SalesTaxPeriodStatus.ChangedSinceFiled, drifted.Period.Status);
        Assert.Contains(drifted.ChangesSinceFiled, change => change.Line == "Line 5" && change.Filed == 0m && change.Now == 14m);

        AppResult<SalesTaxWorksheetDto> telefile = await harness.Service.FileAsync(Q3, Filing(drifted, amended: true, method: SalesTaxFilingMethod.Telefile), CancellationToken.None);
        Assert.Equal(AppResultStatus.Validation, telefile.Status);

        AppResult<SalesTaxWorksheetDto> amended = await harness.Service.FileAsync(Q3, Filing(drifted, amended: true), CancellationToken.None);
        Assert.Equal(AppResultStatus.Ok, amended.Status);
        Assert.Equal(SalesTaxPeriodStatus.Filed, amended.Value!.Period.Status);
        Assert.Equal(SalesTaxReturnKind.AmendedReplacement, amended.Value.Filings[0].Kind);
        Assert.True(amended.Value.Filings[0].IsEffective);
        Assert.Equal(14m, amended.Value.Filings[0].TotalTaxDue);
    }

    [Fact]
    public async Task A_filing_needs_both_attestations_and_keeps_them()
    {
        Harness harness = await NewAsync(nameof(A_filing_needs_both_attestations_and_keeps_them));
        SalesTaxWorksheetDto worksheet = await Worksheet(harness, Q3);

        AppResult<SalesTaxWorksheetDto> noSales = await harness.Service.FileAsync(Q3, Filing(worksheet) with { ConfirmedDirectSalesRecorded = false }, CancellationToken.None);
        AppResult<SalesTaxWorksheetDto> noUseTax = await harness.Service.FileAsync(Q3, Filing(worksheet) with { ConfirmedUseTaxReviewed = false }, CancellationToken.None);
        Assert.Equal(AppResultStatus.Validation, noSales.Status);
        Assert.Equal(AppResultStatus.Validation, noUseTax.Status);

        AppResult<SalesTaxWorksheetDto> filed = await harness.Service.FileAsync(Q3, Filing(worksheet), CancellationToken.None);
        SalesTaxFiledReturnDto record = filed.Value!.Filings.Single();
        Assert.True(record.AttestedDirectSalesRecorded);
        Assert.True(record.AttestedUseTaxReviewed);
    }

    [Fact]
    public async Task A_late_filing_records_the_penalty_and_voiding_reopens_the_period()
    {
        Harness harness = await NewAsync(nameof(A_late_filing_records_the_penalty_and_voiding_reopens_the_period));
        SalesTaxWorksheetDto worksheet = await Worksheet(harness, Q2, new DateOnly(2026, 10, 19));
        Assert.True(worksheet.Timeliness.ReturnLate);
        Assert.Equal(50m, worksheet.Lines.Line14);

        AppResult<SalesTaxWorksheetDto> filed = await harness.Service.FileAsync(Q2, Filing(worksheet) with { AmountPaid = 50m }, CancellationToken.None);
        Assert.Equal(SalesTaxPeriodStatus.FiledLate, filed.Value!.Period.Status);

        Guid id = filed.Value.Filings.Single().Id;
        Assert.Equal(AppResultStatus.Validation, (await harness.Service.VoidAsync(id, new VoidSalesTaxReturnRequest(" "), CancellationToken.None)).Status);
        Assert.Equal(AppResultStatus.Ok, (await harness.Service.VoidAsync(id, new VoidSalesTaxReturnRequest("Recorded on the wrong period"), CancellationToken.None)).Status);

        SalesTaxWorksheetDto reopened = await Worksheet(harness, Q2);
        Assert.Equal(SalesTaxPeriodStatus.Overdue, reopened.Period.Status);
        Assert.True(reopened.Filings.Single().Voided);
    }

    [Fact]
    public async Task Refunded_tax_beyond_a_period_carries_into_the_next()
    {
        Harness harness = await NewAsync(nameof(Refunded_tax_beyond_a_period_carries_into_the_next));
        Sale sale = harness.AddSale("Storefront", Et(6, 15), 100m, tax: 7m, remitted: false, order: harness.AddOrder(null, pickup: true));
        harness.Db.Refunds.Add(new Refund
        {
            OrganizationId = harness.Org,
            SaleId = sale.Id,
            Type = RefundType.Full,
            ItemAmount = 100m,
            TaxRefunded = 7m,
            Reason = "returned",
            RecordedByUserId = Guid.NewGuid(),
            RefundedAt = Et(7, 2),
        });
        harness.AddSale("Storefront", Et(8, 1), 50m, tax: 3.5m, remitted: false, order: harness.AddOrder(null, pickup: true));
        await harness.Db.SaveChangesAsync();

        SalesTaxWorksheetDto q3 = await Worksheet(harness, Q3);
        Assert.Equal(3.5m, q3.Lines.Line5);
        Assert.Equal(3.5m, q3.Lines.Line6);
        Assert.Equal(0m, q3.Lines.Line10);
        Assert.Equal(3.5m, q3.Carry.DeductionsCarriedOut);

        SalesTaxWorksheetDto q4 = await Worksheet(harness, "20261001-20261231");
        Assert.Equal(3.5m, q4.Carry.DeductionsCarriedIn);
    }

    [Fact]
    public async Task The_profile_rejects_bad_certificates_and_misaligned_frequency_changes()
    {
        Harness harness = await NewAsync(nameof(The_profile_rejects_bad_certificates_and_misaligned_frequency_changes), configure: false);

        Assert.Equal(AppResultStatus.Validation, (await harness.Service.SaveProfileAsync(Profile("12-345"), CancellationToken.None)).Status);
        Assert.Equal(AppResultStatus.Validation, (await harness.Service.SaveProfileAsync(Profile(frequencies:
        [
            new SalesTaxFrequencyDto(new DateOnly(2026, 4, 1), SalesTaxFrequency.Quarterly, SalesTaxPeriodAlignment.Calendar),
            new SalesTaxFrequencyDto(new DateOnly(2027, 2, 1), SalesTaxFrequency.Annual, SalesTaxPeriodAlignment.Calendar),
        ]), CancellationToken.None)).Status);
        Assert.Equal(AppResultStatus.Validation, (await harness.Service.SaveProfileAsync(Profile(frequencies:
            [new SalesTaxFrequencyDto(new DateOnly(2026, 4, 1), SalesTaxFrequency.Monthly, SalesTaxPeriodAlignment.Alternate)]), CancellationToken.None)).Status);

        AppResult<SalesTaxProfileDto> saved = await harness.Service.SaveProfileAsync(Profile(), CancellationToken.None);
        Assert.Equal("Manatee", saved.Value!.County);
        Assert.Equal(.01m, saved.Value.CountySurtaxRate);

        AppResult<SalesTaxProfileDto> resaved = await harness.Service.SaveProfileAsync(Profile(frequencies:
        [
            new SalesTaxFrequencyDto(new DateOnly(2026, 4, 1), SalesTaxFrequency.Quarterly, SalesTaxPeriodAlignment.Calendar),
            new SalesTaxFrequencyDto(new DateOnly(2027, 1, 1), SalesTaxFrequency.Annual, SalesTaxPeriodAlignment.Calendar),
        ]), CancellationToken.None);
        Assert.Equal(2, resaved.Value!.Frequencies.Count);
        Assert.Equal(2, await harness.Db.SalesTaxFrequencyChanges.CountAsync());
    }

    [Fact]
    public async Task Undefined_enum_values_are_refused()
    {
        Harness harness = await NewAsync(nameof(Undefined_enum_values_are_refused));
        Sale sale = harness.AddSale("Storefront", Et(8, 10), 100m, remitted: false);
        SalesTaxWorksheetDto worksheet = await Worksheet(harness, Q3);

        Assert.Equal(AppResultStatus.Validation, (await harness.Service.FileAsync(Q3, Filing(worksheet) with { Method = (SalesTaxFilingMethod)99 }, CancellationToken.None)).Status);
        Assert.Equal(AppResultStatus.Validation, (await harness.Service.AddAdjustmentAsync(
            new SaveSalesTaxAdjustmentRequest(Q3, (SalesTaxAdjustmentKind)99, 1m, "x"), CancellationToken.None)).Status);
        Assert.Equal(AppResultStatus.Validation, (await harness.Service.SaveSaleDecisionAsync(sale.Id,
            new SaveSalesTaxSaleDecisionRequest(Dr15Destination.OutsideFlorida, null, (Dr15Exemption)99, null, null), CancellationToken.None)).Status);
        Assert.Equal(AppResultStatus.Validation, (await harness.Service.SaveSaleDecisionAsync(sale.Id,
            new SaveSalesTaxSaleDecisionRequest((Dr15Destination)99, null, Dr15Exemption.None, null, null), CancellationToken.None)).Status);
    }

    [Fact]
    public async Task The_preview_shows_the_returns_a_registration_would_require_before_saving()
    {
        Harness harness = await NewAsync(nameof(The_preview_shows_the_returns_a_registration_would_require_before_saving), configure: false);
        var quarterly = new SalesTaxFrequencyDto(new DateOnly(2026, 5, 31), SalesTaxFrequency.Quarterly, SalesTaxPeriodAlignment.Calendar);

        SalesTaxSchedulePreviewDto incomplete = await harness.Service.PreviewAsync(new SalesTaxSchedulePreviewRequest(null, "Manatee", null, [quarterly]), CancellationToken.None);
        Assert.NotNull(incomplete.Problem);
        Assert.Equal(.07m, incomplete.CombinedRate);
        Assert.Empty(incomplete.Periods);

        SalesTaxSchedulePreviewDto preview = await harness.Service.PreviewAsync(
            new SalesTaxSchedulePreviewRequest(new DateOnly(2026, 5, 31), "manatee", null, [quarterly]), CancellationToken.None);
        Assert.Null(preview.Problem);
        Assert.Equal("Manatee", preview.County);
        Assert.Equal(
            [SalesTaxPeriodStatus.Open, SalesTaxPeriodStatus.DueSoon, SalesTaxPeriodStatus.Overdue],
            preview.Periods.Select(period => period.Status));
        Assert.Equal(new DateOnly(2026, 10, 20), preview.Periods[1].LateAfter);

        SalesTaxSchedulePreviewDto monthly = await harness.Service.PreviewAsync(new SalesTaxSchedulePreviewRequest(new DateOnly(2026, 5, 31), "Manatee", null,
            [quarterly with { Frequency = SalesTaxFrequency.Monthly }]), CancellationToken.None);
        Assert.Equal(6, monthly.Periods.Count);

        SalesTaxSchedulePreviewDto future = await harness.Service.PreviewAsync(
            new SalesTaxSchedulePreviewRequest(new DateOnly(2027, 1, 1), "Manatee", null, [quarterly]), CancellationToken.None);
        Assert.Equal("The registration date cannot be in the future.", future.Problem);
    }

    [Fact]
    public async Task A_business_still_on_pacific_time_is_told_to_switch()
    {
        Harness harness = await NewAsync(nameof(A_business_still_on_pacific_time_is_told_to_switch), zone: "America/Los_Angeles");
        SalesTaxOverviewDto overview = await harness.Service.GetOverviewAsync(CancellationToken.None);
        Assert.Contains(overview.Notices, notice => notice.Code == "time_zone");
    }

    [Fact]
    public async Task The_schedule_export_neutralizes_spreadsheet_formulas()
    {
        Harness harness = await NewAsync(nameof(The_schedule_export_neutralizes_spreadsheet_formulas));
        Sale sale = harness.AddSale("eBay", Et(8, 3), 40m);
        (await harness.Db.InventoryItems.FirstAsync(item => item.Id == sale.InventoryItemId)).Title = "=HYPERLINK(\"x\")";
        await harness.Db.SaveChangesAsync();

        string csv = SalesTaxCsv.Build(await Worksheet(harness, Q3));

        Assert.Contains("\"'=HYPERLINK(\"\"x\"\")\"", csv);
        Assert.Contains("A Sales/Services,0.00,0.00,0.00,0.00", csv);
    }
}
