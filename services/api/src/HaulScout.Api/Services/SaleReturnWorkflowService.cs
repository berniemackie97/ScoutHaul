using Microsoft.EntityFrameworkCore;
using HaulScout.Api.Mapping;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;
using static HaulScout.Api.Helpers.StringHelpers;

namespace HaulScout.Api.Services;

/// <summary>
/// The return (RMA) lifecycle: request → label issued → received → refunded, plus the reject/cancel exits. The
/// controller owns HTTP shape; this owns the rules — one open return per sale, the seller's return label is booked
/// as a shipping cost the moment it is issued, and the refund is only issued once the item is back (restocking it).
/// </summary>
public sealed class SaleReturnWorkflowService(
    HaulScoutDbContext db,
    ICurrentUserContext currentUser,
    SaleFinancialStateService saleState)
{
    public async Task<IReadOnlyList<ReturnDto>> ListAsync(Guid saleId, CancellationToken cancellationToken)
    {
        List<Return> items = await db.Returns
            .Include(item => item.Sale).ThenInclude(sale => sale!.InventoryItem)
            .Where(item => item.SaleId == saleId && item.OrganizationId == currentUser.OrganizationId)
            .OrderByDescending(item => item.RequestedAt)
            .ToListAsync(cancellationToken);
        return items.Select(item => item.ToDto()).ToList();
    }

    public async Task<AppResult<ReturnDto>> OpenAsync(Guid saleId, OpenReturnRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return AppResult<ReturnDto>.Validation("A reason is required to open a return.");
        }

        Sale? sale = await db.Sales
            .Include(item => item.InventoryItem)
            .SingleOrDefaultAsync(item => item.Id == saleId && item.OrganizationId == currentUser.OrganizationId, cancellationToken);
        if (sale is null)
        {
            return AppResult<ReturnDto>.NotFound();
        }
        if (sale.Status is SaleStatus.Cancelled)
        {
            return AppResult<ReturnDto>.Conflict("A cancelled sale cannot be returned.");
        }

        bool hasOpen = await db.Returns.AnyAsync(item => item.SaleId == sale.Id
            && item.OrganizationId == currentUser.OrganizationId
            && (item.Status == ReturnStatus.Requested || item.Status == ReturnStatus.LabelIssued || item.Status == ReturnStatus.Received),
            cancellationToken);
        if (hasOpen)
        {
            return AppResult<ReturnDto>.Conflict("This sale already has an open return.");
        }

        Return rma = new()
        {
            OrganizationId = currentUser.OrganizationId,
            SaleId = sale.Id,
            OrderId = sale.OrderId,
            Status = ReturnStatus.Requested,
            Reason = request.Reason.Trim(),
            Notes = TrimToNull(request.Notes),
            RequestedByUserId = currentUser.UserId,
            Sale = sale,
        };
        db.Returns.Add(rma);
        await db.SaveChangesAsync(cancellationToken);
        return AppResult<ReturnDto>.Ok(rma.ToDto());
    }

    public async Task<AppResult<ReturnDto>> IssueLabelAsync(Guid returnId, IssueReturnLabelRequest request, CancellationToken cancellationToken)
    {
        if (request.ReturnShippingCost < 0)
        {
            return AppResult<ReturnDto>.Validation("Return shipping cost cannot be negative.");
        }
        Return? rma = await LoadAsync(returnId, cancellationToken);
        if (rma is null)
        {
            return AppResult<ReturnDto>.NotFound();
        }
        if (rma.Status != ReturnStatus.Requested)
        {
            return AppResult<ReturnDto>.Conflict($"A return label can only be issued from Requested (it is {rma.Status}).");
        }

        Sale sale = rma.Sale!;
        string before = SaleFinancialStateService.Snapshot(sale);
        rma.ReturnShippingCost = request.ReturnShippingCost;
        rma.Status = ReturnStatus.LabelIssued;
        rma.LabelIssuedAt = DateTimeOffset.UtcNow;
        if (request.Notes is not null)
        {
            rma.Notes = TrimToNull(request.Notes);
        }
        // The seller pays the return label now; book it as a shipping cost so net reflects it whatever the outcome.
        sale.ShippingCost += request.ReturnShippingCost;
        SaleFinancialStateService.RecomputeNet(sale);
        await saleState.AddCorrectionAsync(sale, before, $"Return label issued: return shipping {request.ReturnShippingCost:C}", cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return AppResult<ReturnDto>.Ok(rma.ToDto());
    }

    public async Task<AppResult<ReturnDto>> ReceiveAsync(Guid returnId, CancellationToken cancellationToken)
    {
        Return? rma = await LoadAsync(returnId, cancellationToken);
        if (rma is null)
        {
            return AppResult<ReturnDto>.NotFound();
        }
        if (rma.Status is not (ReturnStatus.Requested or ReturnStatus.LabelIssued))
        {
            return AppResult<ReturnDto>.Conflict($"Only a Requested or LabelIssued return can be marked received (it is {rma.Status}).");
        }

        rma.Status = ReturnStatus.Received;
        rma.ReceivedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return AppResult<ReturnDto>.Ok(rma.ToDto());
    }

    public async Task<AppResult<ReturnDto>> ResolveAsync(Guid returnId, ResolveReturnRequest request, CancellationToken cancellationToken)
    {
        if (request.ItemAmount < 0 || request.ShippingRefunded < 0 || request.TaxRefunded < 0 || request.FeeCredit < 0)
        {
            return AppResult<ReturnDto>.Validation("Refund amounts cannot be negative.");
        }
        Return? rma = await LoadAsync(returnId, cancellationToken);
        if (rma is null)
        {
            return AppResult<ReturnDto>.NotFound();
        }
        if (rma.Status != ReturnStatus.Received)
        {
            return AppResult<ReturnDto>.Conflict($"A refund is issued once the return is Received (it is {rma.Status}). Mark it received first.");
        }

        Sale sale = rma.Sale!;
        decimal alreadyRefundedItem = sale.Refunds.Sum(refund => refund.ItemAmount);
        if (alreadyRefundedItem + request.ItemAmount > sale.SoldPrice + 0.01m)
        {
            return AppResult<ReturnDto>.Validation("Total item refunds cannot exceed the sold price.");
        }

        string before = SaleFinancialStateService.Snapshot(sale);
        Refund refund = new()
        {
            OrganizationId = currentUser.OrganizationId,
            SaleId = sale.Id,
            Type = request.Type,
            ItemAmount = request.ItemAmount,
            ShippingRefunded = request.ShippingRefunded,
            TaxRefunded = request.TaxRefunded,
            FeeCredit = request.FeeCredit,
            ItemReturned = request.Restock,
            Reason = TrimToNull(request.Reason) ?? rma.Reason,
            Notes = TrimToNull(request.Notes),
            RefundedAt = DateTimeOffset.UtcNow,
            RecordedByUserId = currentUser.UserId,
            Sale = sale,
        };
        db.Refunds.Add(refund);
        SaleFinancialStateService.ApplyRefundState(sale);

        rma.Status = ReturnStatus.Refunded;
        rma.Refund = refund;
        rma.RefundId = refund.Id;
        rma.Restocked = request.Restock;
        rma.ResolvedAt = DateTimeOffset.UtcNow;

        await saleState.AddCorrectionAsync(sale, before,
            $"Return refunded ({request.Type}): item {request.ItemAmount:C}{(request.Restock ? ", restocked" : ", item kept")} — {refund.Reason}",
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return AppResult<ReturnDto>.Ok(rma.ToDto());
    }

    public async Task<AppResult<ReturnDto>> RejectAsync(Guid returnId, RejectReturnRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return AppResult<ReturnDto>.Validation("A reason is required to reject a return.");
        }
        Return? rma = await LoadAsync(returnId, cancellationToken);
        if (rma is null)
        {
            return AppResult<ReturnDto>.NotFound();
        }
        if (!rma.IsOpen)
        {
            return AppResult<ReturnDto>.Conflict($"This return is already resolved ({rma.Status}).");
        }

        // The return shipping already paid stays booked — the seller ate that cost regardless.
        rma.Status = ReturnStatus.Rejected;
        rma.ResolvedAt = DateTimeOffset.UtcNow;
        rma.Notes = TrimToNull(request.Reason);
        await db.SaveChangesAsync(cancellationToken);
        return AppResult<ReturnDto>.Ok(rma.ToDto());
    }

    public async Task<AppResult<ReturnDto>> CancelAsync(Guid returnId, CancellationToken cancellationToken)
    {
        Return? rma = await LoadAsync(returnId, cancellationToken);
        if (rma is null)
        {
            return AppResult<ReturnDto>.NotFound();
        }
        if (rma.Status is not (ReturnStatus.Requested or ReturnStatus.LabelIssued))
        {
            return AppResult<ReturnDto>.Conflict($"Only a Requested or LabelIssued return can be cancelled (it is {rma.Status}).");
        }

        rma.Status = ReturnStatus.Cancelled;
        rma.ResolvedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return AppResult<ReturnDto>.Ok(rma.ToDto());
    }

    private Task<Return?> LoadAsync(Guid returnId, CancellationToken cancellationToken) =>
        db.Returns
            .Include(item => item.Sale).ThenInclude(sale => sale!.InventoryItem).ThenInclude(inv => inv!.BundleComponents).ThenInclude(component => component.ComponentInventoryItem)
            .Include(item => item.Sale).ThenInclude(sale => sale!.Listing)
            .Include(item => item.Sale).ThenInclude(sale => sale!.Refunds)
            .SingleOrDefaultAsync(item => item.Id == returnId && item.OrganizationId == currentUser.OrganizationId, cancellationToken);
}
