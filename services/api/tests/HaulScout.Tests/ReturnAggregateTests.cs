using HaulScout.Domain;
using HaulScout.Domain.Common;
using HaulScout.Domain.Entities;

namespace HaulScout.Tests;

/// <summary>
/// The return lifecycle, tested as pure rules — no database, no HTTP, no clock. That is the point of putting the
/// stages on the aggregate: the thing worth protecting (a refund is never issued before the item is back) is now
/// checkable in microseconds and cannot be bypassed by a future caller who forgets to look.
/// </summary>
public class ReturnAggregateTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);

    private static Sale NewSale() => new()
    {
        OrganizationId = Guid.NewGuid(),
        InventoryItemId = Guid.NewGuid(),
        Platform = "eBay",
        SoldPrice = 120m,
        Status = SaleStatus.Delivered,
        SoldAt = T0.AddDays(-7),
    };

    private static Refund NewRefund(Sale sale) => new()
    {
        OrganizationId = sale.OrganizationId,
        SaleId = sale.Id,
        Type = RefundType.Full,
        ItemAmount = sale.SoldPrice,
        Reason = "returned",
        RecordedByUserId = Guid.NewGuid(),
    };

    private static Return Opened() => Return.Open(Guid.NewGuid(), NewSale(), "Doesn't fit", null, Guid.NewGuid(), T0);

    [Fact]
    public void A_return_opens_requested_and_carries_its_reason()
    {
        Return rma = Opened();

        Assert.Equal(ReturnStatus.Requested, rma.Status);
        Assert.Equal("Doesn't fit", rma.Reason);
        Assert.Equal(T0, rma.RequestedAt);
        Assert.True(rma.IsOpen);
    }

    [Fact]
    public void A_return_cannot_be_opened_without_a_reason()
    {
        DomainException e = Assert.Throws<DomainException>(() =>
            Return.Open(Guid.NewGuid(), NewSale(), "   ", null, Guid.NewGuid(), T0));

        Assert.Equal("A reason is required to open a return.", e.Message);
    }

    /// <summary>The rule the whole design exists for: money never moves before the item is back.</summary>
    [Fact]
    public void A_refund_cannot_be_issued_before_the_item_is_received()
    {
        Sale sale = NewSale();
        Return rma = Return.Open(Guid.NewGuid(), sale, "damaged", null, Guid.NewGuid(), T0);
        rma.IssueLabel(8m, null, T0.AddHours(1));   // label sent, item still with the buyer

        DomainException e = Assert.Throws<DomainException>(() => rma.Resolve(NewRefund(sale), restocked: true, T0.AddHours(2)));

        Assert.Contains("Mark it received first", e.Message);
        Assert.Null(rma.RefundId);
        Assert.Equal(ReturnStatus.LabelIssued, rma.Status);
    }

    [Fact]
    public void The_happy_path_walks_requested_to_refunded_and_records_each_moment()
    {
        Sale sale = NewSale();
        Return rma = Return.Open(Guid.NewGuid(), sale, "damaged", null, Guid.NewGuid(), T0);

        rma.IssueLabel(8m, null, T0.AddHours(1));
        Assert.Equal(ReturnStatus.LabelIssued, rma.Status);
        Assert.Equal(8m, rma.ReturnShippingCost);
        Assert.Equal(T0.AddHours(1), rma.LabelIssuedAt);

        rma.Receive(T0.AddDays(3));
        Assert.Equal(ReturnStatus.Received, rma.Status);
        Assert.Equal(T0.AddDays(3), rma.ReceivedAt);

        Refund refund = NewRefund(sale);
        rma.Resolve(refund, restocked: true, T0.AddDays(3).AddHours(1));
        Assert.Equal(ReturnStatus.Refunded, rma.Status);
        Assert.Equal(refund.Id, rma.RefundId);
        Assert.True(rma.Restocked);
        Assert.False(rma.IsOpen);
    }

    [Fact]
    public void A_label_cannot_be_issued_twice()
    {
        Return rma = Opened();
        rma.IssueLabel(8m, null, T0);

        DomainException e = Assert.Throws<DomainException>(() => rma.IssueLabel(8m, null, T0));

        Assert.Contains("can only be issued from Requested", e.Message);
        Assert.Equal(8m, rma.ReturnShippingCost);   // unchanged
    }

    [Fact]
    public void A_return_shipping_cost_cannot_be_negative()
    {
        Return rma = Opened();

        Assert.Throws<DomainException>(() => rma.IssueLabel(-1m, null, T0));
        Assert.Equal(ReturnStatus.Requested, rma.Status);   // rejected, not half-applied
    }

    [Fact]
    public void A_resolved_return_cannot_be_resolved_again()
    {
        Sale sale = NewSale();
        Return rma = Return.Open(Guid.NewGuid(), sale, "damaged", null, Guid.NewGuid(), T0);
        rma.Receive(T0.AddDays(1));
        rma.Resolve(NewRefund(sale), restocked: true, T0.AddDays(1));

        Assert.Throws<DomainException>(() => rma.Resolve(NewRefund(sale), restocked: true, T0.AddDays(2)));
    }

    [Fact]
    public void A_received_return_cannot_be_cancelled_the_item_is_already_back()
    {
        Return rma = Opened();
        rma.Receive(T0.AddDays(1));

        DomainException e = Assert.Throws<DomainException>(() => rma.Cancel(T0.AddDays(2)));

        Assert.Contains("can be cancelled", e.Message);
        Assert.Equal(ReturnStatus.Received, rma.Status);
    }

    [Fact]
    public void A_rejected_return_keeps_the_label_cost_the_seller_already_ate_it()
    {
        Return rma = Opened();
        rma.IssueLabel(8m, null, T0);
        rma.Receive(T0.AddDays(1));

        rma.Reject("not the item we sold", T0.AddDays(1));

        Assert.Equal(ReturnStatus.Rejected, rma.Status);
        Assert.Equal(8m, rma.ReturnShippingCost);   // still booked
        Assert.Equal("not the item we sold", rma.Notes);
        Assert.False(rma.IsOpen);
    }

    [Fact]
    public void An_already_resolved_return_cannot_be_rejected()
    {
        Return rma = Opened();
        rma.Cancel(T0);

        DomainException e = Assert.Throws<DomainException>(() => rma.Reject("too late", T0));

        Assert.Contains("already resolved", e.Message);
    }
}
