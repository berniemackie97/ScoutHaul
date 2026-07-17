using Microsoft.EntityFrameworkCore;
using static HaulScout.Api.Helpers.StringHelpers;
using HaulScout.Api.Mapping;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Api.Services;

/// <summary>
/// Refunds recorded directly against a sale — the no-return path (buyer keeps the item) and voiding a mistake.
/// A refund issued at the end of a return lifecycle belongs to <see cref="SaleReturnWorkflowService"/>; both derive
/// the resulting money state through <see cref="SaleFinancialStateService"/> so they can never disagree.
/// </summary>
public sealed class SaleRefundWorkflowService(
    HaulScoutDbContext db,
    ICurrentUserContext currentUser,
    SaleFinancialStateService saleState)
{
    /// <summary>Record a refund (full or partial) as a first-class financial event. Net profit is recomputed from the
    /// original figures minus every refund; the change is audited. A partial refund keeps the item sold; a full refund
    /// with <c>ItemReturned</c> releases the item back to inventory.</summary>
    public async Task<AppResult<SaleDto>> RecordAsync(Guid id, RecordRefundRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return AppResult<SaleDto>.Validation("A reason is required for a refund.");
        }

        if (request.ItemAmount < 0 || request.ShippingRefunded < 0 || request.TaxRefunded < 0 || request.FeeCredit < 0)
        {
            return AppResult<SaleDto>.Validation("Refund amounts cannot be negative.");
        }

        if (request.ItemAmount + request.ShippingRefunded + request.TaxRefunded + request.FeeCredit <= 0)
        {
            return AppResult<SaleDto>.Validation("A refund needs at least one non-zero amount.");
        }

        DateTimeOffset refundedAt = request.RefundedAt ?? DateTimeOffset.UtcNow;
        if (refundedAt > DateTimeOffset.UtcNow.AddMinutes(5))
        {
            return AppResult<SaleDto>.Validation("Refund date cannot be in the future.");
        }

        Sale? sale = await LoadAsync(id, cancellationToken);
        if (sale is null)
        {
            return AppResult<SaleDto>.NotFound();
        }

        if (sale.Status is SaleStatus.Cancelled)
        {
            return AppResult<SaleDto>.Conflict("A cancelled sale cannot be refunded.");
        }

        decimal alreadyRefundedItem = sale.Refunds.Sum(refund => refund.ItemAmount);
        if (alreadyRefundedItem + request.ItemAmount > sale.SoldPrice + 0.01m)
        {
            return AppResult<SaleDto>.Validation("Total item refunds cannot exceed the sold price.");
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
            ItemReturned = request.ItemReturned,
            Reason = request.Reason.Trim(),
            Notes = TrimToNull(request.Notes),
            RefundedAt = refundedAt,
            RecordedByUserId = currentUser.UserId,
            Sale = sale,
        };
        // Explicit Add + reference nav (the codebase's pattern); relationship fixup also puts it on sale.Refunds
        // so the recompute + DTO see it. Adding to the DbSet AND the collection would double-insert.
        db.Refunds.Add(refund);

        SaleFinancialStateService.ApplyRefundState(sale);
        await saleState.AddCorrectionAsync(sale, before,
            $"Refund recorded ({request.Type}): item {request.ItemAmount:C}, shipping {request.ShippingRefunded:C}, fee credit {request.FeeCredit:C} — {request.Reason.Trim()}",
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return AppResult<SaleDto>.Ok(sale.ToDto());
    }

    public async Task<AppResult<SaleDto>> VoidAsync(Guid id, Guid refundId, CancellationToken cancellationToken)
    {
        Sale? sale = await LoadAsync(id, cancellationToken);
        if (sale is null)
        {
            return AppResult<SaleDto>.NotFound();
        }

        Refund? refund = sale.Refunds.SingleOrDefault(item => item.Id == refundId);
        if (refund is null)
        {
            return AppResult<SaleDto>.NotFound("Refund was not found on this sale.");
        }

        string before = SaleFinancialStateService.Snapshot(sale);
        sale.Refunds.Remove(refund);
        db.Refunds.Remove(refund);

        SaleFinancialStateService.ApplyRefundState(sale);
        await saleState.AddCorrectionAsync(sale, before, $"Refund voided: item {refund.ItemAmount:C} ({refund.Reason})", cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return AppResult<SaleDto>.Ok(sale.ToDto());
    }

    private Task<Sale?> LoadAsync(Guid id, CancellationToken cancellationToken) =>
        db.Sales
            .Include(item => item.InventoryItem)
                .ThenInclude(item => item!.BundleComponents)
                    .ThenInclude(item => item.ComponentInventoryItem)
            .Include(item => item.Listing)
            .Include(item => item.Refunds)
            .SingleOrDefaultAsync(item => item.Id == id && item.OrganizationId == currentUser.OrganizationId, cancellationToken);
}
