using HaulScout.Application.Common;
using HaulScout.Domain.Entities;

namespace HaulScout.Application.Sync.Agents;

/// <summary>
/// Why an account's loop cannot run at all, whoever asks. The portal and the agent share these, so a
/// scan the hub refuses and a run the agent is refused say the same thing in the same words.
/// </summary>
public static class SyncRules
{
    /// <param name="extensionVersion">The agent's version, when an agent is asking — some versions' adapters may be switched off.</param>
    public static AppResult? Refusal(ChannelAccount account, SyncLoopKind loop, SyncAgentOptions options, string? extensionVersion)
    {
        if (!account.IsActive)
        {
            return AppResult.Validation($"{account.DisplayName} is switched off in Channels.", AppErrorCode.SyncUnavailable);
        }

        if (account.IsNativelySynced)
        {
            return AppResult.Validation($"{account.DisplayName} is your own storefront, which is always in step.", AppErrorCode.SyncUnavailable);
        }

        if (SyncPlatforms.Find(account.Channel) is not SyncPlatform platform)
        {
            return AppResult.Validation($"{account.Channel} cannot be synced from a browser yet.", AppErrorCode.SyncUnavailable);
        }

        SyncPlatformSwitch toggle = options.For(platform.Platform);
        if (!toggle.AllowsVersion(extensionVersion))
        {
            return AppResult.Validation(
                toggle.DisabledReason ?? $"Sync for {platform.Platform} is switched off for now.", AppErrorCode.SyncUnavailable);
        }

        if (!platform.Supports(loop))
        {
            return AppResult.Validation(
                $"{platform.Platform} {loop.ToString().ToLowerInvariant()} cannot be scanned yet.", AppErrorCode.SyncUnavailable);
        }

        if (!account.IsBound)
        {
            return AppResult.Validation(
                $"{account.DisplayName} is not bound to a {platform.Platform} account yet. Bind it under Sync.", AppErrorCode.SyncNotBound);
        }

        return null;
    }
}
