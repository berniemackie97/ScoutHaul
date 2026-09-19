using HaulScout.Application.Common;
using HaulScout.Application.Security;
using HaulScout.Application.Contracts;
using HaulScout.Domain;
using HaulScout.Domain.Entities;

namespace HaulScout.Application.PlatformOrders;

/// <summary>One order that can be settled without asking anyone, and the mapping that would be written.</summary>
public sealed record AutoMatchPlan(
    Guid PlatformOrderId,
    string ExternalOrderId,
    string Platform,
    IReadOnlyList<ReconcilePlatformOrderLineMap> Lines);

/// <summary>
/// Decides which imported orders leave nothing to decide, and settles them.
///
/// Planning and applying are separate on purpose. The import applies immediately — the operator is
/// already watching a file land. The backlog shows the plan first and settles only on confirmation,
/// because writing sales in bulk against records someone imported weeks ago should never be a
/// side effect of opening a page.
/// </summary>
public sealed class PlatformOrderAutoMatchService(
    PlatformOrderSuggestionService suggestionService,
    PlatformOrderWorkflowService reconciliation,
    IPlatformOrderStore store,
    ICurrentUserContext currentUser)
{
    private const int BacklogLimit = 500;

    /// <summary>What settling the existing backlog would do, without doing any of it.</summary>
    public async Task<AutoMatchPreviewDto> PreviewBacklogAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<PlatformOrder> pending = await LoadPendingAsync(cancellationToken);
        IReadOnlyList<AutoMatchPlan> plans = await PlanAsync(pending, cancellationToken);

        return new AutoMatchPreviewDto(
            pending.Count,
            plans.Count,
            plans.Take(50).Select(plan => new AutoMatchPreviewOrderDto(
                plan.PlatformOrderId, plan.ExternalOrderId, plan.Platform, plan.Lines.Count)).ToArray());
    }

    /// <summary>Settles the backlog the preview described. Re-planned at apply time, never trusting a stale list.</summary>
    public async Task<int> ApplyBacklogAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<PlatformOrder> pending = await LoadPendingAsync(cancellationToken);
        return await ApplyAsync(await PlanAsync(pending, cancellationToken), cancellationToken);
    }

    private Task<IReadOnlyList<PlatformOrder>> LoadPendingAsync(CancellationToken cancellationToken) =>
        store.ListAsync(currentUser.OrganizationId, PlatformOrderStatus.Imported, BacklogLimit, cancellationToken);

    /// <summary>
    /// The orders whose every line has an exact match on an item that is not already sold. A title
    /// resemblance never qualifies: posting real money against an item a fuzzy match picked is worse
    /// than leaving the order for a person.
    /// </summary>
    public async Task<IReadOnlyList<AutoMatchPlan>> PlanAsync(
        IReadOnlyList<PlatformOrder> orders,
        CancellationToken cancellationToken)
    {
        // An order whose fees the marketplace has not posted yet waits: settling writes money, and a
        // sale recorded before its fees are known would carry a profit that is not true. The scan
        // that brings the fees settles it.
        List<PlatformOrder> unsettled = orders
            .Where(order => order.Status == PlatformOrderStatus.Imported && order.Lines.Count > 0 && !order.FeesPending)
            .ToList();
        if (unsettled.Count == 0)
        {
            return [];
        }

        Dictionary<Guid, PlatformLineMatch> matches = await suggestionService.BuildMatchesAsync(unsettled, cancellationToken);

        List<AutoMatchPlan> plans = [];
        foreach (PlatformOrder order in unsettled)
        {
            List<ReconcilePlatformOrderLineMap> maps = [];
            bool everyLineCertain = true;
            foreach (PlatformOrderLine line in order.Lines)
            {
                if (line.InventoryItemId is Guid alreadyMapped)
                {
                    maps.Add(new ReconcilePlatformOrderLineMap(line.Id, alreadyMapped));
                    continue;
                }

                if (matches.TryGetValue(line.Id, out PlatformLineMatch? match) && match.CanAutoSettle)
                {
                    maps.Add(new ReconcilePlatformOrderLineMap(line.Id, match.InventoryItemId));
                    continue;
                }

                everyLineCertain = false;
                break;
            }

            // Two lines resolving to the same item is ambiguity, not certainty — reconciliation
            // rejects it anyway, and a person should see why.
            if (!everyLineCertain || maps.Select(map => map.InventoryItemId).Distinct().Count() != maps.Count)
            {
                continue;
            }

            plans.Add(new AutoMatchPlan(order.Id, order.ExternalOrderId, order.Platform, maps));
        }

        return plans;
    }

    /// <summary>
    /// Settles the planned orders. A refusal is not a failure — that order simply stays in the queue,
    /// so a plan that has gone stale since it was shown degrades to "settled fewer" rather than an error.
    /// </summary>
    public async Task<int> ApplyAsync(IReadOnlyList<AutoMatchPlan> plans, CancellationToken cancellationToken)
    {
        int settled = 0;
        foreach (AutoMatchPlan plan in plans)
        {
            AppResult<PlatformOrderReconcileResultDto> result = await reconciliation.ReconcileAsync(
                plan.PlatformOrderId, new ReconcilePlatformOrderRequest(plan.Lines), cancellationToken);

            if (result.Status == AppResultStatus.Ok)
            {
                settled++;
            }
        }

        return settled;
    }

    /// <summary>Plan and settle in one step — what an import does, where the operator is already present.</summary>
    public async Task<int> PlanAndApplyAsync(IReadOnlyList<PlatformOrder> orders, CancellationToken cancellationToken) =>
        await ApplyAsync(await PlanAsync(orders, cancellationToken), cancellationToken);
}
