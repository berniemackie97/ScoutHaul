using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Domain.Entities;

namespace HaulScout.Application.Sync.Agents;

/// <summary>A device token matched to its browser, and whether it was the token before the last rotation.</summary>
public sealed record AgentTokenMatch(SyncAgent Agent, bool UsedPreviousToken);

/// <summary>Paired browsers, their leases and scan requests — persistence port.</summary>
public interface ISyncAgentStore
{
    /// <summary>
    /// The browser holding this token. Looked up before any organization is known — this is how the
    /// organization becomes known — so it deliberately sees every organization's agents.
    /// </summary>
    Task<AgentTokenMatch?> FindAgentByTokenAsync(string tokenHash, CancellationToken cancellationToken);

    /// <summary>Whether the user still belongs to the organization — checked on every agent request.</summary>
    Task<bool> IsMemberAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken);

    Task<SyncAgent?> FindAgentAsync(Guid organizationId, Guid agentId, CancellationToken cancellationToken);

    /// <summary>Every agent ever paired, newest first, with the marketplaces each reported.</summary>
    Task<IReadOnlyList<SyncAgent>> ListAgentsAsync(Guid organizationId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, string>> UserNamesAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken);

    /// <summary>A pairing by its code's hash, across organizations — the browser presenting it has none yet.</summary>
    Task<SyncPairing?> FindPairingByCodeAsync(string codeHash, CancellationToken cancellationToken);

    Task<SyncPairing?> FindPairingAsync(Guid organizationId, Guid pairingId, CancellationToken cancellationToken);

    /// <summary>The organization's marketplace accounts, tracked for update.</summary>
    Task<IReadOnlyList<ChannelAccount>> ListAccountsAsync(Guid organizationId, CancellationToken cancellationToken);

    Task<ChannelAccount?> FindAccountAsync(Guid organizationId, Guid accountId, CancellationToken cancellationToken);

    Task<IReadOnlyList<SyncLoop>> ListLoopsAsync(Guid organizationId, CancellationToken cancellationToken);

    Task<SyncLoop?> FindLoopAsync(Guid organizationId, Guid accountId, SyncLoopKind kind, CancellationToken cancellationToken);

    /// <summary>Requests still queued or running.</summary>
    Task<IReadOnlyList<SyncScanRequest>> ListOpenScanRequestsAsync(Guid organizationId, CancellationToken cancellationToken);

    /// <summary>Open requests, and those that finished since <paramref name="finishedSince"/>, newest first.</summary>
    Task<IReadOnlyList<SyncScanRequest>> ListScanRequestsAsync(Guid organizationId, DateTimeOffset finishedSince, int take, CancellationToken cancellationToken);

    Task<SyncScanRequest?> FindScanRequestAsync(Guid organizationId, Guid requestId, CancellationToken cancellationToken);

    /// <summary>The runs these ids name, for the sweep to close the ones whose browser went away.</summary>
    Task<IReadOnlyList<SyncRun>> ListRunsAsync(Guid organizationId, IEnumerable<Guid> runIds, CancellationToken cancellationToken);

    void Add(SyncAgent agent);

    void Add(SyncAgentMarketplace marketplace);

    void Remove(SyncAgentMarketplace marketplace);

    void Add(SyncPairing pairing);

    void Add(SyncLoop loop);

    void Add(SyncScanRequest request);

    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Saves, reporting a lost race — another browser wrote the same lease first — as false instead of
    /// throwing. Nothing from the failed attempt stays pending.
    /// </summary>
    Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>Which paired browser is calling, resolved from its device token.</summary>
public interface ISyncAgentContext
{
    Guid AgentId { get; }

    /// <summary>The browser authenticated with the token from before the last rotation — it never received the new one.</summary>
    bool UsedPreviousToken { get; }
}

/// <summary>
/// Listing ingest as the sync pipeline needs it. The implementation still lives in the API layer, so
/// the agent's use case depends on this seam rather than on that layer.
/// </summary>
public interface IPlatformListingIngestion
{
    Task<AppResult<IngestPlatformListingsResultDto>> IngestAsync(IngestPlatformListingsRequest request, CancellationToken cancellationToken);
}
