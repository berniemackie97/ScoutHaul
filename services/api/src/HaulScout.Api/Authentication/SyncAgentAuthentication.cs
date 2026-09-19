using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using HaulScout.Application.Sync.Agents;
using HaulScout.Domain.Entities;

namespace HaulScout.Api.Authentication;

public static class SyncAgentAuthDefaults
{
    public const string Scheme = "SyncAgent";

    public const string AgentIdClaim = "haulscout:agent_id";
    public const string OrganizationIdClaim = "haulscout:organization_id";
    public const string UserIdClaim = "haulscout:user_id";
    public const string PreviousTokenClaim = "haulscout:previous_token";

    /// <summary>The rate limit on exchanging a pairing code.</summary>
    public const string PairingRateLimitPolicy = "sync-pairing";

    /// <summary>Why a device token was refused, for the challenge to tell the extension.</summary>
    internal const string FailureItem = "haulscout:sync_agent_failure";
}

/// <summary>
/// Authenticates the sync agent by its device token.
///
/// The token names one paired browser. The browser acts as the person who paired it, so each request
/// also checks that person still belongs to the organization — removing someone disconnects every
/// browser they paired, without a separate step anyone could forget. A refusal says which of the
/// reasons it was, so the extension can tell "pair this browser again" from a network blip.
/// </summary>
public sealed class SyncAgentAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> schemeOptions,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ISyncAgentStore agents,
    IOptions<SyncAgentOptions> syncOptions,
    TimeProvider clock)
    : AuthenticationHandler<AuthenticationSchemeOptions>(schemeOptions, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? header = Request.Headers.Authorization.FirstOrDefault();
        const string bearer = "Bearer ";
        if (header is null || !header.StartsWith(bearer, StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        string token = header[bearer.Length..].Trim();
        if (!SyncSecrets.LooksLikeToken(token))
        {
            return AuthenticateResult.NoResult();
        }

        AgentTokenMatch? match = await agents.FindAgentByTokenAsync(SyncSecrets.HashToken(token), Context.RequestAborted);
        if (match is null)
        {
            return Refuse("agent_unknown", "This browser is not paired. Connect it again from HaulScout.");
        }

        SyncAgent agent = match.Agent;
        if (agent.RevokedAt is not null)
        {
            return Refuse("agent_revoked", "This browser was disconnected from HaulScout. Connect it again to resume syncing.");
        }

        if (agent.LastSeenAt < clock.GetUtcNow().AddDays(-syncOptions.Value.InactiveAgentDays))
        {
            return Refuse("agent_expired", "This browser has not synced for a long time. Connect it again from HaulScout.");
        }

        if (!await agents.IsMemberAsync(agent.OrganizationId, agent.PairedByUserId, Context.RequestAborted))
        {
            return Refuse("agent_owner_removed", "The person who paired this browser is no longer in the organization. Connect it again.");
        }

        Claim[] claims =
        [
            // The rate limiter partitions by this, so each browser gets its own allowance.
            new(ClaimTypes.NameIdentifier, $"agent:{agent.Id}"),
            new(SyncAgentAuthDefaults.AgentIdClaim, agent.Id.ToString()),
            new(SyncAgentAuthDefaults.OrganizationIdClaim, agent.OrganizationId.ToString()),
            new(SyncAgentAuthDefaults.UserIdClaim, agent.PairedByUserId.ToString()),
            new(SyncAgentAuthDefaults.PreviousTokenClaim, match.UsedPreviousToken ? "true" : "false"),
        ];
        ClaimsPrincipal principal = new(new ClaimsIdentity(claims, Scheme.Name));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        (string code, string message) = Context.Items[SyncAgentAuthDefaults.FailureItem] as (string, string)?
            ?? ("agent_unauthenticated", "Connect this browser to HaulScout first.");
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.ContentType = "application/problem+json";
        await Response.WriteAsync(JsonSerializer.Serialize(new
        {
            type = "https://tools.ietf.org/html/rfc9110#section-15.5.2",
            title = "Unauthorized",
            status = StatusCodes.Status401Unauthorized,
            detail = message,
            code,
        }));
    }

    private AuthenticateResult Refuse(string code, string message)
    {
        Context.Items[SyncAgentAuthDefaults.FailureItem] = (code, message);
        return AuthenticateResult.Fail(message);
    }
}

/// <summary>The calling browser, as its device token identified it.</summary>
public sealed class ClaimsSyncAgentContext(IHttpContextAccessor httpContextAccessor) : ISyncAgentContext
{
    public Guid AgentId => Guid.TryParse(Claim(SyncAgentAuthDefaults.AgentIdClaim), out Guid id)
        ? id
        : throw new InvalidOperationException("The request was not made by a paired browser.");

    public bool UsedPreviousToken => Claim(SyncAgentAuthDefaults.PreviousTokenClaim) == "true";

    private string? Claim(string type) => httpContextAccessor.HttpContext?.User.FindFirst(type)?.Value;
}
