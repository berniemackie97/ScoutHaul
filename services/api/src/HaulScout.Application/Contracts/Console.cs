namespace HaulScout.Application.Contracts;

/// <summary>
/// One queue of work, and how many are in it.
///
/// Every count here is produced by the same query as the page the queue opens, so following a card
/// never lands on a different number of rows than the card showed. The old home page failed exactly
/// that: its "Not listed" left out items awaiting repair and its "Aged" counted by a different date
/// than the list it linked to.
/// </summary>
/// <param name="Key">Names the queue and its destination, e.g. <c>orders.toPack</c>. The portal maps it to a link and a sentence.</param>
/// <param name="Tier">
/// <c>attention</c> costs money or a customer if ignored; <c>work</c> is today's job; <c>hygiene</c>
/// is missing data that quietly weakens every other figure on the page.
/// </param>
/// <param name="Amount">Money behind the queue, where there is one — the landed cost not yet allocated, say.</param>
public sealed record ConsoleQueueDto(string Key, string Tier, int Count, decimal? Amount = null);

/// <summary>Stock on hand: the same figures the inventory page shows for active items.</summary>
public sealed record ConsoleInventoryDto(int ActiveItems, decimal CapitalDeployed, decimal EstimatedResaleValue);

/// <summary>
/// Trading over a recent window against the window before it, from the same calculation the
/// Reports page runs — so the two pages cannot disagree about how the business is doing.
/// </summary>
/// <param name="From">First day of the window, inclusive.</param>
/// <param name="To">Last day of the window, inclusive.</param>
/// <param name="Comparison">The report's own comparison with the window before, carried as-is rather than recomputed.</param>
public sealed record ConsoleMoneyDto(int Days, DateOnly From, DateOnly To, ReportKpiDto Current, ReportComparisonDto Comparison);

/// <summary>
/// The operator's home screen: what is waiting, what the stock is worth, how trading is going.
///
/// Each part is present only when the caller may open the page behind it — a packer sees their
/// packing queue, not the profit and loss — and absent rather than zero when they may not, so the
/// portal never shows a count that leads somewhere the caller cannot go.
/// </summary>
public sealed record OperatorConsoleDto(
    IReadOnlyList<ConsoleQueueDto> Queues,
    ConsoleInventoryDto? Inventory,
    ConsoleMoneyDto? Money,
    DateTimeOffset GeneratedAt);
