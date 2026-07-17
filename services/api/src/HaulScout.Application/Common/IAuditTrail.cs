namespace HaulScout.Application.Common;

/// <summary>
/// The record of who did what. Use cases state the fact; stamping it with the organization and actor, and serializing
/// the details, are mechanisms and belong to the implementation — every caller was previously doing all three by hand.
///
/// Recording only stages the entry: it is written by the same SaveChanges as the change it describes, so an audit
/// entry can never be committed for work that was rolled back, nor lost for work that was not.
/// </summary>
public interface IAuditTrail
{
    /// <param name="entityName">The type the action happened to, e.g. nameof(InventoryItem).</param>
    /// <param name="entityId">Which one, where the action is about a specific row. Null when it is not.</param>
    /// <param name="action">What happened, in past tense, e.g. "BundleCreated".</param>
    /// <param name="details">Anything worth keeping; serialized by the implementation.</param>
    Task RecordAsync(string entityName, Guid? entityId, string action, object details, CancellationToken cancellationToken);
}
