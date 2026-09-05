namespace HaulScout.Domain;

/// <summary>
/// Which inventory is waiting on bench work before it can be listed.
///
/// The rule lived in the admin portal as a string array and was applied to whatever rows that page
/// happened to have loaded, so the repair queue was both a duplicate definition and a partial one.
/// It belongs here, next to the other lifecycle rules, so the queue, its count and any future
/// automation all mean the same thing by "needs repair".
/// </summary>
public static class InventoryBenchWork
{
    /// <summary>
    /// Functional states that mean the item cannot be listed as-is.
    ///
    /// <see cref="InventoryFunctionalStatus.PartsOnly"/> is deliberately absent: an item kept for
    /// parts is not waiting on a repair, it is what it is going to be.
    /// </summary>
    public static readonly InventoryFunctionalStatus[] BlockingStates =
    [
        InventoryFunctionalStatus.Untested,
        InventoryFunctionalStatus.FunctionalWithIssues,
        InventoryFunctionalStatus.Intermittent,
        InventoryFunctionalStatus.NonFunctional,
    ];

    /// <summary>
    /// True when the item belongs in the bench queue — either its condition blocks listing, or it is
    /// a customer's own item that is only here to be worked on.
    /// </summary>
    public static bool IsQueued(InventoryFunctionalStatus functionalStatus, InventoryOwnershipType ownershipType) =>
        ownershipType == InventoryOwnershipType.CustomerRepair
        || BlockingStates.Contains(functionalStatus);
}
