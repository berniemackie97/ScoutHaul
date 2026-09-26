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
    IUnitOfWork unitOfWork,
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

        string? note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        if (note is not null && note.Length > PlatformOrder.CancellationNoteLimit)
        {
            // Checked before anything is undone: a note the column cannot hold would otherwise fail the save after
            // the sale had already been cancelled, leaving the sale reversed and the order still asking.
            return AppResult<PlatformOrderDto>.Validation(
                $"That reason is too long. Keep it under {PlatformOrder.CancellationNoteLimit} characters.");
        }

        // The reversal and the decision are one act. Either the sale is cancelled and the order says so, or neither
        // happened — an order recorded as kept while its sale is gone is the one outcome worse than doing nothing.
        try
        {
            return await unitOfWork.InTransactionAsync(async token =>
            {
                order.CancellationOutcome = request.Outcome;
                order.CancellationDecidedAt = clock.GetUtcNow();
                order.CancellationNote = note;
                if (!await store.TryClaimCancellationDecisionAsync(order, token))
                {
                    return AppResult<PlatformOrderDto>.Validation("Someone settled this cancellation a moment ago. Reload to see what they decided.");
                }

                if (request.Outcome == PlatformOrderCancellationOutcome.Reversed
                    && await ReverseSalesAsync(order, note, token) is AppResult<PlatformOrderDto> failure)
                {
                    throw new ReversalFailed(failure.Message ?? "That sale could not be reversed.");
                }

                return AppResult<PlatformOrderDto>.Ok(PlatformOrderDtoFactory.ToPlatformOrderDto(order));
            }, cancellationToken);
        }
        catch (ReversalFailed failed)
        {
            // Thrown to roll the transaction back; the order is left asking rather than half-settled.
            return AppResult<PlatformOrderDto>.Validation(failed.Message);
        }
    }

    private sealed class ReversalFailed(string message) : Exception(message);

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
