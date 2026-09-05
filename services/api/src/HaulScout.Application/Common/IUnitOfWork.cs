namespace HaulScout.Application.Common;

/// <summary>
/// Runs several writes as one, so a failure part-way through leaves nothing behind.
///
/// Most operations here save once and need nothing. A few genuinely cannot: deleting a sale has to
/// persist the removal before it can tell whether the order it belonged to is now empty, and
/// publishing to the storefront has to persist the item's status before the synchroniser has a
/// listing to decorate. Those are two writes that must both happen or neither.
///
/// This is a port rather than a call to EF because Application does not reference EF, and it earns
/// its place by inverting a boundary that cannot be crossed any other way.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Runs <paramref name="work"/> inside a transaction, committing if it returns and rolling back
    /// if it throws. Nesting is safe: an inner call joins the transaction already running.
    /// </summary>
    Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken);
}
