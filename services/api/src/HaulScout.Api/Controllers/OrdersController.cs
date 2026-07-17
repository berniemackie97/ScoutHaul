using Microsoft.AspNetCore.Mvc;
using HaulScout.Api.Authorization;
using HaulScout.Api.Services;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;

namespace HaulScout.Api.Controllers;

[ApiController]
[Route("api/orders")]
[RequireOrganizationPermission(HaulScoutPermissions.ManageOrders)]
public sealed class OrdersController(OrdersWorkflowService orders) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<OrderSummaryDto>>> List(
        [FromQuery] string? status,
        [FromQuery] string? search,
        [FromQuery] string? channel,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<OrderSummaryDto> result = await orders.ListAsync(status, search, channel, cancellationToken);
        return result.ToArray();
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrderDetailDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        AppResult<OrderDetailDto> result = await orders.GetAsync(id, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<OrderDetailDto>> UpdateStatus(
        Guid id,
        UpdateOrderStatusRequest request,
        CancellationToken cancellationToken)
    {
        AppResult<OrderDetailDto> result = await orders.UpdateStatusAsync(id, request, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPatch("{id:guid}/tracking")]
    public async Task<ActionResult<OrderDetailDto>> UpdateTracking(
        Guid id,
        UpdateOrderTrackingRequest request,
        CancellationToken cancellationToken)
    {
        AppResult<OrderDetailDto> result = await orders.UpdateTrackingAsync(id, request, cancellationToken);
        return ToActionResult(result);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        AppResult result = await orders.DeleteAsync(id, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("merge")]
    public async Task<ActionResult<OrderDetailDto>> Merge(
        MergeOrdersRequest request,
        CancellationToken cancellationToken)
    {
        AppResult<OrderDetailDto> result = await orders.MergeAsync(request, cancellationToken);
        return ToActionResult(result);
    }

    private ActionResult<T> ToActionResult<T>(AppResult<T> result) where T : class =>
        result.Status switch
        {
            AppResultStatus.Ok when result.Value is not null => result.Value,
            AppResultStatus.NotFound => result.Message is null ? NotFound() : NotFound(result.Message),
            AppResultStatus.Conflict => Conflict(result.Message),
            AppResultStatus.Validation => BadRequest(result.Message),
            _ => BadRequest(result.Message),
        };

    private IActionResult ToActionResult(AppResult result) =>
        result.Status switch
        {
            AppResultStatus.Ok => NoContent(),
            AppResultStatus.NotFound => result.Message is null ? NotFound() : NotFound(result.Message),
            AppResultStatus.Conflict => Conflict(result.Message),
            AppResultStatus.Validation => BadRequest(result.Message),
            _ => BadRequest(result.Message),
        };
}
