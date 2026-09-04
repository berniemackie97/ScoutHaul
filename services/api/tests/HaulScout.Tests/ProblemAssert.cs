using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HaulScout.Tests;

/// <summary>
/// Asserts a controller refused a request, and refused it the one way the API refuses things.
///
/// These replace assertions on concrete result types — BadRequestObjectResult, NotFoundResult and so
/// on. Those only ever checked which helper the controller happened to call; they said nothing about
/// what reached the caller, and there were six different mappings producing them. This checks the
/// status and the machine code that clients actually branch on.
/// </summary>
internal static class ProblemAssert
{
    public static ProblemDetails Refused(object? actionResult, int expectedStatus, string? expectedCode = null)
    {
        object? result = actionResult switch
        {
            ActionResult wrapped => wrapped,
            _ => Unwrap(actionResult),
        };

        ObjectResult objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(expectedStatus, objectResult.StatusCode);

        ProblemDetails problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal(expectedStatus, problem.Status);

        // Every failure carries one, so a client never has to read the English to know what happened.
        object? code = Assert.Contains("code", problem.Extensions);
        Assert.False(string.IsNullOrWhiteSpace(code as string), "the problem carried an empty code");
        if (expectedCode is not null) Assert.Equal(expectedCode, code);

        return problem;
    }

    public static ProblemDetails Invalid(object? result, string? code = null) =>
        Refused(result, StatusCodes.Status400BadRequest, code);

    public static ProblemDetails Missing(object? result, string? code = null) =>
        Refused(result, StatusCodes.Status404NotFound, code);

    public static ProblemDetails Blocked(object? result, string? code = null) =>
        Refused(result, StatusCodes.Status409Conflict, code);

    /// <summary>Reaches the ActionResult inside an ActionResult&lt;T&gt;, whatever T is.</summary>
    private static object? Unwrap(object? value) =>
        value?.GetType().GetProperty("Result")?.GetValue(value) ?? value;
}
