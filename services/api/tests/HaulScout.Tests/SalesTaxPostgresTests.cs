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

/// <summary>The tax store's queries, dates and unique indexes on Postgres, migrated with the production migrations.</summary>
[Collection(PostgresCollection.Name)]
public sealed class SalesTaxPostgresTests(PostgresFixture postgres)
{
    private sealed class StubCurrentUser(Guid organizationId, Guid userId) : ICurrentUserContext
    {
        public Guid OrganizationId => organizationId;
        public Guid? ActiveOrganizationId => organizationId;
        public Guid UserId => userId;
        public Task ResolveAsync() => Task.CompletedTask;
    }

    [Fact]
    public async Task The_tax_center_round_trips_on_postgres()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = await postgres.NewOrganizationAsync();
        await using HaulScoutDbContext _ = db;
        (await db.Organizations.FirstAsync(item => item.Id == org)).TimeZoneId = "America/New_York";
        await db.SaveChangesAsync();

        var current = new StubCurrentUser(org, user);
        var service = new SalesTaxWorkflowService(new EfSalesTaxStore(db), current, new EfAuditTrail(db, current),
            new FakeTimeProvider(new DateTimeOffset(2026, 10, 19, 15, 0, 0, TimeSpan.Zero)));

        SaveSalesTaxProfileRequest profile = new("78-8012345678-9", null, "Overhaul Trading Company LLC", null, null, null, null, "34203", "Manatee",
            new DateOnly(2026, 5, 31), null, true, false, true, null,
            [new SalesTaxFrequencyDto(new DateOnly(2026, 4, 1), SalesTaxFrequency.Monthly, SalesTaxPeriodAlignment.Calendar)]);
        Assert.Equal(AppResultStatus.Ok, (await service.SaveProfileAsync(profile, CancellationToken.None)).Status);
        AppResult<SalesTaxProfileDto> resaved = await service.SaveProfileAsync(profile with
        {
            Frequencies =
            [
                new SalesTaxFrequencyDto(new DateOnly(2026, 4, 1), SalesTaxFrequency.Quarterly, SalesTaxPeriodAlignment.Calendar),
                new SalesTaxFrequencyDto(new DateOnly(2027, 1, 1), SalesTaxFrequency.Annual, SalesTaxPeriodAlignment.Calendar),
            ],
        }, CancellationToken.None);
        Assert.Equal(AppResultStatus.Ok, resaved.Status);

        var item = new InventoryItem { OrganizationId = org, Title = "Vintage lamp", Status = InventoryStatus.Sold };
        var marketItem = new InventoryItem { OrganizationId = org, Title = "Jacket", Status = InventoryStatus.Sold };
        var order = new Order { OrganizationId = org, OrderNumber = "SF-TAX-1", IsLocalPickup = true, DiscountTotal = 10m, Status = OrderStatus.Paid };
        db.InventoryItems.AddRange(item, marketItem);
        db.Orders.Add(order);
        db.OrderLines.Add(new OrderLine { OrganizationId = org, OrderId = order.Id, InventoryItemId = item.Id, TitleSnapshot = item.Title, UnitPrice = 110m, LineTotal = 110m });
        var sale = new Sale
        {
            OrganizationId = org, InventoryItemId = item.Id, Platform = "Storefront", SoldPrice = 110m, SalesTaxCollected = 7m,
            TaxRemittedByPlatform = false, OrderId = order.Id, Status = SaleStatus.Delivered, SoldAt = new DateTimeOffset(2026, 8, 10, 14, 0, 0, TimeSpan.Zero),
        };
        db.Sales.AddRange(sale, new Sale
        {
            OrganizationId = org, InventoryItemId = marketItem.Id, Platform = "eBay", SoldPrice = 45m, SalesTaxCollected = 3.15m,
            Status = SaleStatus.Delivered, SoldAt = new DateTimeOffset(2026, 9, 2, 14, 0, 0, TimeSpan.Zero),
        });
        db.Refunds.Add(new Refund
        {
            OrganizationId = org, SaleId = sale.Id, Type = RefundType.Partial, ItemAmount = 10m, TaxRefunded = .7m, Reason = "scuff",
            RecordedByUserId = user, RefundedAt = new DateTimeOffset(2026, 9, 1, 14, 0, 0, TimeSpan.Zero),
        });
        await db.SaveChangesAsync();

        const string q3 = "20260701-20260930";
        Assert.Equal(AppResultStatus.Ok, (await service.SaveUseEntryAsync(null,
            new SaveSalesTaxUseEntryRequest(new DateOnly(2026, 9, 3), "Shelving, untaxed online order", 100m, "Hillsborough", 0m, null, item.Id, null), CancellationToken.None)).Status);
        Assert.Equal(AppResultStatus.Ok, (await service.AddAdjustmentAsync(
            new SaveSalesTaxAdjustmentRequest(q3, SalesTaxAdjustmentKind.CreditOrEstimatedTaxPaid, 1m, "DOR credit memo 123"), CancellationToken.None)).Status);
        Assert.Equal(AppResultStatus.Ok, (await service.SaveSaleDecisionAsync(sale.Id,
            new SaveSalesTaxSaleDecisionRequest(Dr15Destination.FloridaCounty, "Manatee", Dr15Exemption.None, null, "Picked up"), CancellationToken.None)).Status);

        AppResult<SalesTaxWorksheetDto> worksheet = await service.GetWorksheetAsync(q3, null, CancellationToken.None);
        Assert.Equal(AppResultStatus.Ok, worksheet.Status);
        Dr15Lines lines = worksheet.Value!.Lines;
        Assert.Equal(new Dr15Row(100m, 0m, 100m, 7m), lines.LineA);
        Assert.Equal(new Dr15Row(null, null, 100m, 7.5m), lines.LineB);
        Assert.Equal(14.5m, lines.Line5);
        Assert.Equal(.7m, lines.Line6);
        Assert.Equal(1m, lines.Line8);
        Assert.Equal(12.8m, lines.Line10);
        Assert.Equal(100m, lines.Line15c);
        Assert.Equal(2.5m, lines.Line15d);
        Assert.Equal("eBay", Assert.Single(worksheet.Value.ExcludedChannels).Channel);

        AppResult<SalesTaxWorksheetDto> filed = await service.FileAsync(q3, new FileSalesTaxReturnRequest(
            new DateOnly(2026, 10, 19), null, SalesTaxFilingMethod.Electronic, "EF123", lines.Line14, false, false,
            worksheet.Value.Issues.Count(issue => issue.Severity == Dr15IssueSeverity.Warning), worksheet.Value.SourceFingerprint, null), CancellationToken.None);
        Assert.Equal(AppResultStatus.Ok, filed.Status);
        Assert.Equal(SalesTaxPeriodStatus.Filed, filed.Value!.Period.Status);

        SalesTaxOverviewDto overview = await service.GetOverviewAsync(CancellationToken.None);
        Assert.Equal(SalesTaxPeriodStatus.Filed, overview.Periods.Single(period => period.Key == q3).Status);
        Assert.True(await db.AuditLogs.AnyAsync(log => log.OrganizationId == org && log.Action == "SalesTaxReturnFiled"));
    }
}
