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

    /// <summary>The reason to send the caller: what this result said, or the default for its status.</summary>
    public string ResolvedCode => Code ?? AppErrorCode.ForStatus(Status);
}
