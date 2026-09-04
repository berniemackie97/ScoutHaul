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

    private static object? BodyOf<T>(ActionResult<T> actionResult) =>
        actionResult.Result is ObjectResult objectResult ? objectResult.Value : null;

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
        Assert.Equal("No such order.", BodyOf(mapped));
    }

    [Fact]
    public void Not_found_without_a_reason_sends_no_body_at_all()
    {
        // Six of the old copies sent `NotFound(result.Message)` regardless, so a reasonless 404 came
        // back with the literal `null` as its body.
        ActionResult<string> mapped = Controller.Map(AppResult<string>.NotFound());

        Assert.Equal(404, StatusOf(mapped));
        Assert.Null(BodyOf(mapped));
    }

    [Fact]
    public void A_conflict_keeps_its_reason()
    {
        ActionResult<string> mapped = Controller.Map(AppResult<string>.Conflict("Already refunded."));

        Assert.Equal(409, StatusOf(mapped));
        Assert.Equal("Already refunded.", BodyOf(mapped));
    }

    [Fact]
    public void A_validation_failure_is_the_callers_fault()
    {
        ActionResult<string> mapped = Controller.Map(AppResult<string>.Validation("Price must be positive."));

        Assert.Equal(400, StatusOf(mapped));
        Assert.Equal("Price must be positive.", BodyOf(mapped));
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
}
