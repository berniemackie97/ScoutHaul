using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.PlatformOrders;
using HaulScout.Domain;

namespace HaulScout.Api.Services;

/// <summary>Cancels a sale the way the operator's own cancel button does, so both routes restock and relist alike.</summary>
public sealed class SaleTransitionCanceller(SaleTransitionWorkflowService transitions) : ISaleCanceller
{
    public async Task<AppResult<bool>> CancelAsync(Guid saleId, DateTimeOffset? occurredAt, string reason, CancellationToken cancellationToken)
    {
        AppResult<SaleDto> result = await transitions.TransitionAsync(
            saleId,
            new SaleStatusTransitionRequest(SaleStatus.Cancelled, occurredAt, reason, null, null),
            cancellationToken);
        return result.Status == AppResultStatus.Ok
            ? AppResult<bool>.Ok(true)
            : AppResult<bool>.Validation(result.Message ?? "That sale could not be cancelled.");
    }
}
