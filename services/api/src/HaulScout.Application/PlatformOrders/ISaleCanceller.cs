using HaulScout.Application.Common;

namespace HaulScout.Application.PlatformOrders;

/// <summary>
/// Undoing a recorded sale. Cancelling one restocks its item, puts its listing back and takes the money off the
/// books; all of that already exists for a sale cancelled by hand, and this is how a marketplace's cancellation
/// reaches the same path instead of a second one.
/// </summary>
public interface ISaleCanceller
{
    Task<AppResult<bool>> CancelAsync(Guid saleId, DateTimeOffset? occurredAt, string reason, CancellationToken cancellationToken);
}
