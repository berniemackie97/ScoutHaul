using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HaulScout.Api.Controllers;
using HaulScout.Api.Services;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Tests;

/// <summary>
/// One rule, asserted from every direction: a sale's stored net profit always reflects its refunds.
///
/// Net used to be derived independently at seven call sites, and five of them called the no-refund calculator — so
/// correcting a sale, enriching its fees, or reallocating its cost basis silently recomputed net as if the refunds
/// had never happened, quietly handing back money that had gone out the door. These tests exist because that class of
/// bug is invisible: nothing throws, the number just becomes wrong. The aggregate now owns the derivation, and these
/// pin the behaviour so a future caller cannot reintroduce a second definition.
/// </summary>
public class SaleNetProfitInvariantTests
{
    private sealed class StubCurrentUser(Guid organizationId, Guid userId) : ICurrentUserContext
    {
        public Guid OrganizationId => organizationId;
        public Guid? ActiveOrganizationId => organizationId;
        public Guid UserId => userId;
        public Task ResolveAsync() => Task.CompletedTask;
    }

    private static (HaulScoutDbContext Db, Guid Org, Guid User) NewContext(string store)
    {
        Guid org = Guid.NewGuid();
        Guid user = Guid.NewGuid();
        DbContextOptions<HaulScoutDbContext> options = new DbContextOptionsBuilder<HaulScoutDbContext>().UseInMemoryDatabase(store).Options;
        TenantContext tenant = new();
        tenant.SetOrganization(org);
        HaulScoutDbContext db = new(options, tenant);
        db.Organizations.Add(new Organization { Id = org, Name = "Overhaul Trading Company" });
        db.Users.Add(new AppUser { Id = user, Email = "op@x.com", DisplayName = "Operator" });
        db.SaveChanges();
        return (db, org, user);
    }

    // Sold 100 + 10 shipping, COGS 30, fees 15, label 8 → net 57.
    private static Sale SeedSale(HaulScoutDbContext db, Guid org)
    {
        InventoryItem item = new() { OrganizationId = org, Title = "Brass Lamp", PurchasePrice = 30m, Status = InventoryStatus.Delivered };
        db.InventoryItems.Add(item);
        Sale sale = new()
        {
            OrganizationId = org,
            InventoryItemId = item.Id,
            Platform = "eBay",
            SoldPrice = 100m,
            ShippingCharged = 10m,
            CostOfGoods = 30m,
            PlatformFees = 15m,
            ShippingCost = 8m,
            Status = SaleStatus.Delivered,
            SoldAt = DateTimeOffset.UtcNow.AddDays(-3),
            InventoryItem = item,
        };
        sale.RecomputeNet();
        db.Sales.Add(sale);
        db.SaveChanges();
        return sale;
    }

    private static UpdateSaleRequest Correction(Sale sale, string reason) => new(
        sale.Platform, sale.SoldPrice, sale.ShippingCharged, sale.PlatformFees, sale.PaymentFees,
        sale.ShippingCost, sale.SuppliesCost, sale.OtherCosts, sale.SalesTaxCollected,
        sale.TaxRemittedByPlatform, sale.SoldAt, sale.Notes, reason);

    /// <summary>
    /// The regression that motivated the aggregate. A $40 partial refund drops net 57 → 22. Correcting an unrelated
    /// field afterwards must not resurrect the refunded $40 — the refund still happened.
    /// </summary>
    [Fact]
    public async Task Correcting_a_refunded_sale_does_not_erase_the_refund_from_net()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(Correcting_a_refunded_sale_does_not_erase_the_refund_from_net));
        await using HaulScoutDbContext _ = db;
        Sale sale = SeedSale(db, org);
        SalesController controller = SalesControllerFactory.Create(db, new StubCurrentUser(org, user));

        await controller.RecordRefund(sale.Id,
            new RecordRefundRequest(RefundType.Partial, ItemAmount: 40m, Reason: "Buyer kept it, partial refund", FeeCredit: 5m),
            CancellationToken.None);
        Sale refunded = await db.Sales.SingleAsync(item => item.Id == sale.Id);
        Assert.Equal(22m, refunded.NetProfit);   // 60 + 10 − 30 − 10 − 8

        // An unrelated correction — the operator fixes a note. Nothing about the money changed.
        ActionResult<SaleDto> result = await controller.Update(sale.Id, Correction(refunded, "Fixed the platform note"), CancellationToken.None);

        Assert.IsNotType<BadRequestObjectResult>(result.Result);
        Sale corrected = await db.Sales.SingleAsync(item => item.Id == sale.Id);
        Assert.Equal(22m, corrected.NetProfit);   // NOT 57 — the $40 refund is still gone
    }

    /// <summary>A correction that genuinely changes the money re-derives net from the corrected inputs *and* the
    /// refunds together, rather than either one overwriting the other.</summary>
    [Fact]
    public async Task A_correction_and_a_refund_both_land_in_net()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(A_correction_and_a_refund_both_land_in_net));
        await using HaulScoutDbContext _ = db;
        Sale sale = SeedSale(db, org);
        SalesController controller = SalesControllerFactory.Create(db, new StubCurrentUser(org, user));

        await controller.RecordRefund(sale.Id,
            new RecordRefundRequest(RefundType.Partial, ItemAmount: 40m, Reason: "partial", FeeCredit: 5m),
            CancellationToken.None);
        Sale refunded = await db.Sales.SingleAsync(item => item.Id == sale.Id);

        // The real label cost was 12, not 8 — $4 worse. Net must be 22 − 4 = 18, refund still applied.
        UpdateSaleRequest request = Correction(refunded, "Carrier billed the true label cost") with { ShippingCost = 12m };
        await controller.Update(sale.Id, request, CancellationToken.None);

        Sale corrected = await db.Sales.SingleAsync(item => item.Id == sale.Id);
        Assert.Equal(18m, corrected.NetProfit);
    }

    /// <summary>A sale with no refunds is unaffected — the refund-aware derivation collapses to the plain one.</summary>
    [Fact]
    public async Task Correcting_an_unrefunded_sale_still_recomputes_from_the_corrected_inputs()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(Correcting_an_unrefunded_sale_still_recomputes_from_the_corrected_inputs));
        await using HaulScoutDbContext _ = db;
        Sale sale = SeedSale(db, org);
        SalesController controller = SalesControllerFactory.Create(db, new StubCurrentUser(org, user));

        UpdateSaleRequest request = Correction(sale, "True label cost") with { ShippingCost = 12m };
        await controller.Update(sale.Id, request, CancellationToken.None);

        Sale corrected = await db.Sales.SingleAsync(item => item.Id == sale.Id);
        Assert.Equal(53m, corrected.NetProfit);   // 100 + 10 − 30 − 15 − 12
    }
}
