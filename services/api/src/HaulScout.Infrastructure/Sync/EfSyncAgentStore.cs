using Microsoft.EntityFrameworkCore;
using Npgsql;
using HaulScout.Application.Sync.Agents;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Infrastructure.Sync;

public sealed class EfSyncAgentStore(HaulScoutDbContext db) : ISyncAgentStore
{
    public async Task<AgentTokenMatch?> FindAgentByTokenAsync(string tokenHash, CancellationToken cancellationToken)
    {
        // Before this lookup no organization is known — this is how it becomes known — so the tenant
        // filter, which would hide every row, is deliberately set aside for this one query.
        SyncAgent? agent = await db.SyncAgents
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(item => item.TokenHash == tokenHash || item.PreviousTokenHash == tokenHash, cancellationToken);
        return agent is null ? null : new AgentTokenMatch(agent, UsedPreviousToken: agent.TokenHash != tokenHash);
    }

    public Task<bool> IsMemberAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken) =>
        db.OrganizationMembers.AnyAsync(member => member.OrganizationId == organizationId && member.UserId == userId, cancellationToken);

    public Task<SyncAgent?> FindAgentAsync(Guid organizationId, Guid agentId, CancellationToken cancellationToken) =>
        db.SyncAgents
            .Include(agent => agent.Marketplaces)
            .SingleOrDefaultAsync(agent => agent.OrganizationId == organizationId && agent.Id == agentId, cancellationToken);

    public async Task<IReadOnlyList<SyncAgent>> ListAgentsAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await db.SyncAgents
            .Include(agent => agent.Marketplaces)
            .Where(agent => agent.OrganizationId == organizationId)
            .OrderByDescending(agent => agent.PairedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, string>> UserNamesAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken)
    {
        Guid[] ids = userIds.Distinct().ToArray();
        return ids.Length == 0
            ? new Dictionary<Guid, string>()
            : await db.Users
                .Where(user => ids.Contains(user.Id))
                .ToDictionaryAsync(user => user.Id, user => user.DisplayName, cancellationToken);
    }

    public Task<SyncPairing?> FindPairingByCodeAsync(string codeHash, CancellationToken cancellationToken) =>
        // The browser presenting a code belongs to no organization yet; the code is what says which.
        db.SyncPairings.IgnoreQueryFilters().SingleOrDefaultAsync(pairing => pairing.CodeHash == codeHash, cancellationToken);

    public Task<SyncPairing?> FindPairingAsync(Guid organizationId, Guid pairingId, CancellationToken cancellationToken) =>
        db.SyncPairings.SingleOrDefaultAsync(pairing => pairing.OrganizationId == organizationId && pairing.Id == pairingId, cancellationToken);

    public async Task<IReadOnlyList<ChannelAccount>> ListAccountsAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await db.ChannelAccounts.Where(account => account.OrganizationId == organizationId).ToListAsync(cancellationToken);

    public Task<ChannelAccount?> FindAccountAsync(Guid organizationId, Guid accountId, CancellationToken cancellationToken) =>
        db.ChannelAccounts.SingleOrDefaultAsync(account => account.OrganizationId == organizationId && account.Id == accountId, cancellationToken);

    public async Task<IReadOnlyList<SyncLoop>> ListLoopsAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await db.SyncLoops.Where(loop => loop.OrganizationId == organizationId).ToListAsync(cancellationToken);

    public Task<SyncLoop?> FindLoopAsync(Guid organizationId, Guid accountId, SyncLoopKind kind, CancellationToken cancellationToken) =>
        db.SyncLoops.SingleOrDefaultAsync(
            loop => loop.OrganizationId == organizationId && loop.ChannelAccountId == accountId && loop.Kind == kind,
            cancellationToken);

    public async Task<IReadOnlyList<SyncScanRequest>> ListOpenScanRequestsAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await db.SyncScanRequests
            .Where(request => request.OrganizationId == organizationId
                && (request.Status == SyncScanStatus.Queued || request.Status == SyncScanStatus.Running))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SyncScanRequest>> ListScanRequestsAsync(
        Guid organizationId, DateTimeOffset finishedSince, int take, CancellationToken cancellationToken) =>
        await db.SyncScanRequests
            .Where(request => request.OrganizationId == organizationId
                && (request.Status == SyncScanStatus.Queued
                    || request.Status == SyncScanStatus.Running
                    || request.FinishedAt >= finishedSince))
            .OrderByDescending(request => request.RequestedAt)
            .Take(take)
            .ToListAsync(cancellationToken);

    public Task<SyncScanRequest?> FindScanRequestAsync(Guid organizationId, Guid requestId, CancellationToken cancellationToken) =>
        db.SyncScanRequests.SingleOrDefaultAsync(request => request.OrganizationId == organizationId && request.Id == requestId, cancellationToken);

    public async Task<IReadOnlyList<SyncRun>> ListRunsAsync(Guid organizationId, IEnumerable<Guid> runIds, CancellationToken cancellationToken)
    {
        Guid[] ids = runIds.Distinct().ToArray();
        return ids.Length == 0
            ? []
            : await db.SyncRuns.Where(run => run.OrganizationId == organizationId && ids.Contains(run.Id)).ToListAsync(cancellationToken);
    }

    public void Add(SyncAgent agent) => db.SyncAgents.Add(agent);

    public void Add(SyncAgentMarketplace marketplace) => db.SyncAgentMarketplaces.Add(marketplace);

    public void Remove(SyncAgentMarketplace marketplace) => db.SyncAgentMarketplaces.Remove(marketplace);

    public void Add(SyncPairing pairing) => db.SyncPairings.Add(pairing);

    public void Add(SyncLoop loop) => db.SyncLoops.Add(loop);

    public void Add(SyncScanRequest request) => db.SyncScanRequests.Add(request);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    public async Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (Exception exception) when (exception is DbUpdateConcurrencyException
            || (exception is DbUpdateException { InnerException: PostgresException postgres } && postgres.SqlState == PostgresErrorCodes.UniqueViolation))
        {
            // Another browser won. Drop what this attempt wrote so nothing half-applied is saved later.
            db.ChangeTracker.Clear();
            return false;
        }
    }
}
