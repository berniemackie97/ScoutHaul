using Microsoft.AspNetCore.Mvc;
using HaulScout.Application.Common;

namespace HaulScout.Api.Controllers;

/// <summary>
/// The one place an <see cref="AppResult{T}"/> becomes an HTTP response.
///
/// Twelve controllers each carried their own copy of this mapping, in four variants that did not
/// agree, so which answer a caller got depended on which controller it happened to ask.
///
/// Every failure now leaves as RFC 7807 ProblemDetails carrying a machine <c>code</c> alongside the
/// human message. Before this, one endpoint in the whole API sent a code — while the mobile client
/// already parsed for one, and the admin app compensated by copy-pasting a message extractor into
/// nineteen files. Both clients read <c>detail</c> and <c>title</c> as well, so this is additive.
/// </summary>
public abstract class HaulScoutControllerBase : ControllerBase
{
    protected ActionResult<T> ToActionResult<T>(AppResult<T> result) => result.Status switch
    {
        AppResultStatus.Ok when result.Value is not null => result.Value,
        AppResultStatus.NotFound => Failure(StatusCodes.Status404NotFound, "Not found", result.Message, result.ResolvedCode),
        AppResultStatus.Conflict => Failure(StatusCodes.Status409Conflict, "Conflict", result.Message, result.ResolvedCode),
        AppResultStatus.Validation => Failure(StatusCodes.Status400BadRequest, "Invalid request", result.Message, result.ResolvedCode),
        // Ok with no value, or a status added without updating this switch. Either is a bug here, not
        // something the caller did wrong, so it is a 500 rather than a 400 blaming them for it.
        _ => Failure(
            StatusCodes.Status500InternalServerError,
            "Unexpected result",
            $"The request produced no usable result ({result.Status}).",
            AppErrorCode.Unexpected),
    };

    /// <summary>
    /// Maps a result whose success is not a plain 200 — a create that answers 201 with a Location.
    ///
    /// The failure half is what every endpoint shares and must not diverge again; the success half is
    /// genuinely per-endpoint, so it stays at the call site instead of being flattened away.
    /// </summary>
    protected ActionResult<T> ToActionResult<T>(AppResult<T> result, Func<T, ActionResult<T>> onOk) =>
        result.Status == AppResultStatus.Ok && result.Value is not null
            ? onOk(result.Value)
            : ToActionResult(result);

    /// <summary>Maps a valueless result, for endpoints whose success is a 204.</summary>
    protected ActionResult ToActionResult(AppResult result) => result.Status switch
    {
        AppResultStatus.Ok => NoContent(),
        AppResultStatus.NotFound => Failure(StatusCodes.Status404NotFound, "Not found", result.Message, result.ResolvedCode),
        AppResultStatus.Conflict => Failure(StatusCodes.Status409Conflict, "Conflict", result.Message, result.ResolvedCode),
        AppResultStatus.Validation => Failure(StatusCodes.Status400BadRequest, "Invalid request", result.Message, result.ResolvedCode),
        _ => Failure(
            StatusCodes.Status500InternalServerError,
            "Unexpected result",
            $"The request produced no usable result ({result.Status}).",
            AppErrorCode.Unexpected),
    };

    /// <summary>A refusal the caller can act on: 400 with a reason and a machine code.</summary>
    protected ObjectResult Invalid(string detail, string code = AppErrorCode.ValidationFailed) =>
        Failure(StatusCodes.Status400BadRequest, "Invalid request", detail, code);

    /// <summary>Nothing matched, with a reason worth sending.</summary>
    protected ObjectResult Missing(string detail, string code = AppErrorCode.NotFound) =>
        Failure(StatusCodes.Status404NotFound, "Not found", detail, code);

    /// <summary>
    /// Nothing matched, and there is nothing useful to add.
    ///
    /// Still an envelope rather than an empty 404: a body-less response cannot carry a code, and a
    /// client should never have to special-case one refusal out of every other.
    /// </summary>
    protected ObjectResult Missing() =>
        Failure(StatusCodes.Status404NotFound, "Not found", "No matching record.", AppErrorCode.NotFound);

    /// <summary>The request made sense but the current state does not allow it.</summary>
    protected ObjectResult Blocked(string detail, string code = AppErrorCode.Conflict) =>
        Failure(StatusCodes.Status409Conflict, "Conflict", detail, code);

    /// <summary>This side could not serve the request, and the caller can try again later.</summary>
    protected ObjectResult Unavailable(string detail, string code) =>
        Failure(StatusCodes.Status503ServiceUnavailable, "Service unavailable", detail, code);

    /// <summary>A refusal whose status the caller decides — upstream failures that map to 429/502/503.</summary>
    protected ObjectResult Refused(int status, string title, string detail, string code) =>
        Failure(status, title, detail, code);

    /// <summary>
    /// One shape for every failure. `code` rides in the extensions, which ASP.NET serializes at the
    /// top level of the body — which is where both clients already look for it.
    /// </summary>
    private ObjectResult Failure(int status, string title, string? detail, string code)
    {
        ProblemDetails problem = new()
        {
            Status = status,
            Title = title,
            Detail = detail,
            Instance = HttpContext?.Request?.Path.Value,
        };
        problem.Extensions["code"] = code;
        return StatusCode(status, problem);
    }
}
