using Microsoft.AspNetCore.Mvc;
using HaulScout.Application.Common;

namespace HaulScout.Api.Controllers;

/// <summary>
/// The one place an <see cref="AppResult{T}"/> becomes an HTTP response.
///
/// Twelve controllers each carried their own copy of this mapping, in four variants that did not
/// agree. A not-found with no message came back as an empty 404 in four of them, as a 404 whose body
/// was the literal <c>null</c> in six, and in one the reason was dropped even when there was one. An
/// unmapped status was a 400 in eleven and a 500 in one. Which answer a caller got depended on which
/// controller it happened to ask.
/// </summary>
public abstract class HaulScoutControllerBase : ControllerBase
{
    protected ActionResult<T> ToActionResult<T>(AppResult<T> result) => result.Status switch
    {
        AppResultStatus.Ok when result.Value is not null => result.Value,
        // Send the reason when there is one, and nothing at all when there is not — never a body of `null`.
        AppResultStatus.NotFound => result.Message is null ? NotFound() : NotFound(result.Message),
        AppResultStatus.Conflict => Conflict(result.Message),
        AppResultStatus.Validation => BadRequest(result.Message),
        // Ok with no value, or a status added without updating this switch. Either is a bug here, not
        // something the caller did wrong, so it is a 500 rather than a 400 blaming them for it.
        _ => Problem($"The request produced no usable result ({result.Status})."),
    };
}
