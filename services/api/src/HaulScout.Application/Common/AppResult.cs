namespace HaulScout.Application.Common;

public enum AppResultStatus
{
    Ok,
    NotFound,
    Validation,
    Conflict,
}

/// <summary>
/// Stable, machine-readable reasons a request was refused.
///
/// A status code says how the caller should treat a failure; it does not say what happened. A client
/// that wants to react specifically — offer to top up a quota, point at the item that is already
/// sold — had nothing to branch on but the English message, which changes whenever someone rewords it.
///
/// Values are snake_case and permanent. Add freely; never rename or reuse one.
/// </summary>
public static class AppErrorCode
{
    /// <summary>Nothing matched what the caller asked for.</summary>
    public const string NotFound = "not_found";

    /// <summary>The request itself was wrong — bad or missing data.</summary>
    public const string ValidationFailed = "validation_failed";

    /// <summary>The request made sense but the current state does not allow it.</summary>
    public const string Conflict = "conflict";

    /// <summary>Something on this side went wrong. Never the caller's fault.</summary>
    public const string Unexpected = "unexpected";

    /// <summary>
    /// A request carrying this idempotency key is already running. The caller should ask again
    /// rather than treat it as a failure — retrying is safe, doing the work twice is not.
    /// </summary>
    public const string RequestInFlight = "request_in_flight";

    /// <summary>Another paired browser holds this account's lease; this one should leave it be.</summary>
    public const string SyncLeaseHeld = "sync_lease_held";

    /// <summary>The run's lease expired or was taken over; the agent should stop and let the next one run.</summary>
    public const string SyncLeaseLost = "sync_lease_lost";

    /// <summary>The browser is signed in to the marketplace as someone other than the bound account.</summary>
    public const string SyncWrongAccount = "sync_wrong_account";

    /// <summary>The account is not bound to a marketplace account yet.</summary>
    public const string SyncNotBound = "sync_not_bound";

    /// <summary>The marketplace has no adapter, or sync for it is switched off.</summary>
    public const string SyncUnavailable = "sync_unavailable";

    /// <summary>The person who paired the browser no longer has the permission this sync writes under.</summary>
    public const string SyncNotPermitted = "sync_not_permitted";

    /// <summary>The run already finished; the agent is holding a stale reference.</summary>
    public const string SyncRunFinished = "sync_run_finished";

    /// <summary>The pairing code is wrong, used, or expired.</summary>
    public const string SyncPairingInvalid = "sync_pairing_invalid";

    /// <summary>The default reason for a status, for callers that have nothing more specific to say.</summary>
    public static string ForStatus(AppResultStatus status) => status switch
    {
        AppResultStatus.NotFound => NotFound,
        AppResultStatus.Validation => ValidationFailed,
        AppResultStatus.Conflict => Conflict,
        _ => Unexpected,
    };
}

public sealed record AppResult(AppResultStatus Status, string? Message = null, string? Code = null)
{
    public static AppResult Ok() => new(AppResultStatus.Ok);
    public static AppResult NotFound(string? message = null, string? code = null) => new(AppResultStatus.NotFound, message, code);
    public static AppResult Validation(string message, string? code = null) => new(AppResultStatus.Validation, message, code);
    public static AppResult Conflict(string message, string? code = null) => new(AppResultStatus.Conflict, message, code);

    /// <summary>The reason to send the caller: what this result said, or the default for its status.</summary>
    public string ResolvedCode => Code ?? AppErrorCode.ForStatus(Status);
}

public sealed record AppResult<T>(AppResultStatus Status, T? Value = default, string? Message = null, string? Code = null)
{
    public static AppResult<T> Ok(T value) => new(AppResultStatus.Ok, value);
    public static AppResult<T> NotFound(string? message = null, string? code = null) => new(AppResultStatus.NotFound, default, message, code);
    public static AppResult<T> Validation(string message, string? code = null) => new(AppResultStatus.Validation, default, message, code);
    public static AppResult<T> Conflict(string message, string? code = null) => new(AppResultStatus.Conflict, default, message, code);

    /// <summary>A refusal decided without a value in mind, carried over as it is — status, words and code.</summary>
    public static AppResult<T> Refused(AppResult refusal) => new(refusal.Status, default, refusal.Message, refusal.Code);

    /// <summary>The reason to send the caller: what this result said, or the default for its status.</summary>
    public string ResolvedCode => Code ?? AppErrorCode.ForStatus(Status);
}
