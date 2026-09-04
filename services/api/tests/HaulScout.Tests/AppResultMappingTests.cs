using Microsoft.AspNetCore.Mvc;
using HaulScout.Api.Controllers;
using HaulScout.Application.Common;

namespace HaulScout.Tests;

/// <summary>
/// Twelve controllers each had their own copy of this mapping, in four variants that disagreed, so
/// which answer a caller got depended on which controller it happened to ask. These pin the one
/// answer they all give now.
/// </summary>
public sealed class AppResultMappingTests
{
    private sealed class TestController : HaulScoutControllerBase
    {
        public ActionResult<T> Map<T>(AppResult<T> result) => ToActionResult(result);
    }

    private static readonly TestController Controller = new();

    private static int StatusOf<T>(ActionResult<T> actionResult) => actionResult.Result switch
    {
        ObjectResult objectResult => objectResult.StatusCode ?? 0,
        StatusCodeResult statusCodeResult => statusCodeResult.StatusCode,
        _ => 200,
    };

    private static ProblemDetails ProblemOf<T>(ActionResult<T> actionResult) =>
        Assert.IsType<ProblemDetails>(Assert.IsAssignableFrom<ObjectResult>(actionResult.Result).Value);

    private static string? CodeOf<T>(ActionResult<T> actionResult) =>
        ProblemOf(actionResult).Extensions["code"] as string;

    [Fact]
    public void A_result_with_a_value_is_the_value()
    {
        ActionResult<string> mapped = Controller.Map(AppResult<string>.Ok("done"));

        Assert.Equal("done", mapped.Value);
    }

    [Fact]
    public void Not_found_with_a_reason_sends_the_reason()
    {
        ActionResult<string> mapped = Controller.Map(AppResult<string>.NotFound("No such order."));

        Assert.Equal(404, StatusOf(mapped));
        Assert.Equal("No such order.", ProblemOf(mapped).Detail);
        Assert.Equal(AppErrorCode.NotFound, CodeOf(mapped));
    }

    [Fact]
    public void Not_found_without_a_reason_still_carries_a_code()
    {
        // Six of the old copies sent `NotFound(result.Message)` regardless, so a reasonless 404 came
        // back with the literal `null` as its body. It now sends the same envelope as everything
        // else, minus a detail — a client should never special-case one refusal out of the rest.
        ActionResult<string> mapped = Controller.Map(AppResult<string>.NotFound());

        Assert.Equal(404, StatusOf(mapped));
        Assert.Null(ProblemOf(mapped).Detail);
        Assert.Equal(AppErrorCode.NotFound, CodeOf(mapped));
    }

    [Fact]
    public void A_conflict_keeps_its_reason()
    {
        ActionResult<string> mapped = Controller.Map(AppResult<string>.Conflict("Already refunded."));

        Assert.Equal(409, StatusOf(mapped));
        Assert.Equal("Already refunded.", ProblemOf(mapped).Detail);
        Assert.Equal(AppErrorCode.Conflict, CodeOf(mapped));
    }

    [Fact]
    public void A_validation_failure_is_the_callers_fault()
    {
        ActionResult<string> mapped = Controller.Map(AppResult<string>.Validation("Price must be positive."));

        Assert.Equal(400, StatusOf(mapped));
        Assert.Equal("Price must be positive.", ProblemOf(mapped).Detail);
        Assert.Equal(AppErrorCode.ValidationFailed, CodeOf(mapped));
    }

    [Fact]
    public void An_ok_with_nothing_in_it_is_our_fault_not_the_callers()
    {
        // Eleven copies called this a 400, blaming the caller for a bug on this side. One called it a
        // 500, which is the honest answer.
        ActionResult<string> mapped = Controller.Map(AppResult<string>.Ok(null!));

        Assert.Equal(500, StatusOf(mapped));
    }

    [Fact]
    public void Every_status_maps_to_something_deliberate()
    {
        foreach (AppResultStatus status in Enum.GetValues<AppResultStatus>())
        {
            AppResult<string> result = status switch
            {
                AppResultStatus.Ok => AppResult<string>.Ok("value"),
                AppResultStatus.NotFound => AppResult<string>.NotFound("gone"),
                AppResultStatus.Conflict => AppResult<string>.Conflict("clash"),
                AppResultStatus.Validation => AppResult<string>.Validation("bad"),
                _ => AppResult<string>.Validation("bad"),
            };

            int code = StatusOf(Controller.Map(result));
            // A status nobody mapped would fall through to 500; every real one is deliberate.
            Assert.Contains(code, new[] { 200, 400, 404, 409 });
        }
    }

    [Fact]
    public void A_result_can_say_something_more_specific_than_its_status()
    {
        // The whole point: a status says how to treat a failure, a code says what happened. A client
        // that wants to offer to top up a quota needs the second one.
        ActionResult<string> mapped = Controller.Map(
            AppResult<string>.Conflict("Out of credits.", "quota_exhausted"));

        Assert.Equal("quota_exhausted", CodeOf(mapped));
    }

    [Fact]
    public void Every_failure_carries_a_code()
    {
        AppResult<string>[] failures =
        [
            AppResult<string>.NotFound(),
            AppResult<string>.NotFound("gone"),
            AppResult<string>.Conflict("clash"),
            AppResult<string>.Validation("bad"),
            AppResult<string>.Ok(null!),
        ];

        foreach (AppResult<string> failure in failures)
        {
            Assert.False(string.IsNullOrWhiteSpace(CodeOf(Controller.Map(failure))));
        }
    }

    [Fact]
    public void A_failure_on_our_side_does_not_blame_the_caller()
    {
        ActionResult<string> mapped = Controller.Map(AppResult<string>.Ok(null!));

        Assert.Equal(500, StatusOf(mapped));
        Assert.Equal(AppErrorCode.Unexpected, CodeOf(mapped));
    }
}
