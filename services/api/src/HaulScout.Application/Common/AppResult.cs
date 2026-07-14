namespace HaulScout.Application.Common;

public enum AppResultStatus
{
    Ok,
    NotFound,
    Validation,
    Conflict,
}

public sealed record AppResult(AppResultStatus Status, string? Message = null)
{
    public static AppResult Ok() => new(AppResultStatus.Ok);
    public static AppResult NotFound(string? message = null) => new(AppResultStatus.NotFound, message);
    public static AppResult Validation(string message) => new(AppResultStatus.Validation, message);
    public static AppResult Conflict(string message) => new(AppResultStatus.Conflict, message);
}

public sealed record AppResult<T>(AppResultStatus Status, T? Value = default, string? Message = null)
{
    public static AppResult<T> Ok(T value) => new(AppResultStatus.Ok, value);
    public static AppResult<T> NotFound(string? message = null) => new(AppResultStatus.NotFound, default, message);
    public static AppResult<T> Validation(string message) => new(AppResultStatus.Validation, default, message);
    public static AppResult<T> Conflict(string message) => new(AppResultStatus.Conflict, default, message);
}
