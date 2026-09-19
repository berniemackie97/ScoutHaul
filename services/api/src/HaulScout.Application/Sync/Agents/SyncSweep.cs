using HaulScout.Domain.Entities;

namespace HaulScout.Application.Sync.Agents;

/// <summary>
/// Closes out work whose browser went away. A laptop shut mid-scan leaves a lease that expires on its
/// own; this finishes the story — the run is marked failed with a reason, the loop records it, and a
/// request a person made says what happened instead of spinning forever. Queued requests no browser
/// picks up expire, and say so.
///
/// Runs on every heartbeat and before any new request, so the hub is truthful whether or not a
/// browser is online.
/// </summary>
public static class SyncSweep
{
    public const string AbandonedMessage = "The browser running this stopped responding before it finished. It runs again on its next turn.";

    public static async Task RunAsync(
        ISyncAgentStore store,
        Guid organizationId,
        DateTimeOffset now,
        SyncAgentOptions options,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<SyncLoop> loops = await store.ListLoopsAsync(organizationId, cancellationToken);
        List<SyncLoop> abandoned = loops
            .Where(loop => loop.LeaseRunId is not null && loop.LeaseExpiresAt <= now)
            .ToList();
        IReadOnlyList<SyncScanRequest> open = await store.ListOpenScanRequestsAsync(organizationId, cancellationToken);

        if (abandoned.Count > 0)
        {
            IReadOnlyList<SyncRun> runs = await store.ListRunsAsync(
                organizationId, abandoned.Select(loop => loop.LeaseRunId!.Value), cancellationToken);

            foreach (SyncLoop loop in abandoned)
            {
                Guid runId = loop.LeaseRunId!.Value;
                if (runs.FirstOrDefault(run => run.Id == runId) is SyncRun run && run.Status == SyncRunStatus.Running)
                {
                    run.Status = SyncRunStatus.Failed;
                    run.Summary = AbandonedMessage;
                    run.FinishedAt = now;
                }

                loop.LastFinishedAt = now;
                loop.LastError = AbandonedMessage;
                loop.ConsecutiveFailures++;
                ReleaseLease(loop);

                foreach (SyncScanRequest request in open.Where(request => request.SyncRunId == runId && request.Status == SyncScanStatus.Running))
                {
                    Finish(request, SyncScanStatus.Failed, AbandonedMessage, now);
                }
            }
        }

        DateTimeOffset expiredBefore = now.AddHours(-options.ScanRequestExpiryHours);
        foreach (SyncScanRequest request in open.Where(request => request.Status == SyncScanStatus.Queued && request.RequestedAt <= expiredBefore))
        {
            Finish(request, SyncScanStatus.Expired,
                $"No browser signed in to this account picked it up within {options.ScanRequestExpiryHours} hours.", now);
        }
    }

    /// <summary>
    /// Disconnects a browser: its token stops working at once, and whatever it was running is closed
    /// out so another browser can take over. The same whether the portal revokes it or it leaves.
    /// </summary>
    public static async Task DisconnectAsync(
        ISyncAgentStore store, SyncAgent agent, Guid byUserId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (agent.RevokedAt is not null)
        {
            return;
        }

        agent.RevokedAt = now;
        agent.RevokedByUserId = byUserId;

        const string disconnected = "The browser running this was disconnected from HaulScout.";
        List<SyncLoop> held = (await store.ListLoopsAsync(agent.OrganizationId, cancellationToken))
            .Where(loop => loop.LeaseAgentId == agent.Id)
            .ToList();
        IReadOnlyList<SyncRun> runs = await store.ListRunsAsync(
            agent.OrganizationId, held.Where(loop => loop.LeaseRunId is not null).Select(loop => loop.LeaseRunId!.Value), cancellationToken);
        IReadOnlyList<SyncScanRequest> open = await store.ListOpenScanRequestsAsync(agent.OrganizationId, cancellationToken);
        foreach (SyncLoop loop in held)
        {
            if (runs.FirstOrDefault(run => run.Id == loop.LeaseRunId) is SyncRun run && run.Status == SyncRunStatus.Running)
            {
                run.Status = SyncRunStatus.Failed;
                run.Summary = disconnected;
                run.FinishedAt = now;
                foreach (SyncScanRequest request in open.Where(request => request.SyncRunId == run.Id && request.Status == SyncScanStatus.Running))
                {
                    Finish(request, SyncScanStatus.Failed, disconnected, now);
                }
            }

            loop.LastFinishedAt = now;
            loop.LastError = disconnected;
            ReleaseLease(loop);
        }
    }

    public static void ReleaseLease(SyncLoop loop)
    {
        loop.LeaseAgentId = null;
        loop.LeaseExpiresAt = null;
        loop.LeaseRunId = null;
        loop.Version = Guid.NewGuid();
    }

    public static void Finish(SyncScanRequest request, SyncScanStatus status, string? message, DateTimeOffset now)
    {
        request.Status = status;
        request.Message = message is null ? null : message.Length <= 500 ? message : message[..500];
        request.FinishedAt = now;
    }
}
