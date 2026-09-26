using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Domain;
using HaulScout.Domain.Entities;

namespace HaulScout.Application.PlatformOrders;

/// <summary>
/// Settles an order the marketplace cancelled after it had already been recorded as a sale.
///
/// Undoing a sale puts an item back in stock and takes money off the books, so a scan never does it on its own — it
/// writes the cancellation down and waits. This is where a person answers: undo it, or say why it stands.
/// </summary>
public sealed class PlatformOrderCancellationService(
    IPlatformOrderStore store,
    ICurrentUserContext currentUser,
    ISaleCanceller sales,
    TimeProvider clock)
{
    public async Task<AppResult<PlatformOrderDto>> DecideAsync(
        Guid platformOrderId,
        ResolvePlatformOrderCancellationRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Outcome is not (PlatformOrderCancellationOutcome.Reversed or PlatformOrderCancellationOutcome.Kept))
        {
            return AppResult<PlatformOrderDto>.Validation("Say whether the sale is reversed or kept.");
        }

        PlatformOrder? order = await store.GetAsync(currentUser.OrganizationId, platformOrderId, cancellationToken);
        if (order is null)
        {
            return AppResult<PlatformOrderDto>.NotFound("That order was not found.");
        }

        if (order.CancelledOnPlatformAt is null)
        {
            return AppResult<PlatformOrderDto>.Validation($"{order.Platform} has not cancelled this order.");
        }

        if (order.CancellationOutcome != PlatformOrderCancellationOutcome.None)
        {
            // Already settled. Saying so beats cancelling a sale twice.
            return AppResult<PlatformOrderDto>.Ok(PlatformOrderDtoFactory.ToPlatformOrderDto(order));
        }

        if (request.Outcome == PlatformOrderCancellationOutcome.Kept && string.IsNullOrWhiteSpace(request.Note))
        {
            return AppResult<PlatformOrderDto>.Validation("Keeping a cancelled sale needs a reason.");
        }

        if (request.Outcome == PlatformOrderCancellationOutcome.Reversed
            && await ReverseSalesAsync(order, request.Note, cancellationToken) is AppResult<PlatformOrderDto> failure)
        {
            return failure;
        }

        order.CancellationOutcome = request.Outcome;
        order.CancellationDecidedAt = clock.GetUtcNow();
        order.CancellationNote = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        await store.SaveChangesAsync(cancellationToken);
        return AppResult<PlatformOrderDto>.Ok(PlatformOrderDtoFactory.ToPlatformOrderDto(order));
    }

    /// <summary>Cancels every sale this order was reconciled into, or explains why it could not.</summary>
    private async Task<AppResult<PlatformOrderDto>?> ReverseSalesAsync(
        PlatformOrder order,
        string? reason,
        CancellationToken cancellationToken)
    {
        if (order.ReconciledSaleGroupId is not Guid group)
        {
            return AppResult<PlatformOrderDto>.Validation("This order is not linked to a recorded sale, so there is nothing to reverse.");
        }

        string note = string.IsNullOrWhiteSpace(reason)
            ? $"{order.Platform} cancelled order {order.ExternalOrderId}."
            : reason.Trim();

        foreach (Guid saleId in await store.ListLiveSaleIdsInGroupAsync(currentUser.OrganizationId, group, cancellationToken))
        {
            AppResult<bool> cancelled = await sales.CancelAsync(saleId, order.CancelledOnPlatformAt, note, cancellationToken);
            if (cancelled.Status != AppResultStatus.Ok)
            {
                return AppResult<PlatformOrderDto>.Validation(cancelled.Message ?? "That sale could not be reversed.");
            }
        }

        return null;
    }
}
