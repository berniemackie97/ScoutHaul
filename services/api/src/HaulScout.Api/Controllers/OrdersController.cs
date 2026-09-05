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
public sealed class OrdersController(OrdersWorkflowService orders) : HaulScoutControllerBase
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

    /// <summary>
    /// A filtered, paged page of orders plus the queue summary over the whole book.
    ///
    /// Mirrors <c>/api/inventory/browse</c>. The array endpoint above stays for callers that want the
    /// recent list and nothing else.
    /// </summary>
    [HttpGet("browse")]
    public async Task<ActionResult<OrderListDto>> Browse(
        [FromQuery] string? stage,
        [FromQuery] string? search,
        [FromQuery] string? channel,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default) =>
        await orders.BrowseAsync(stage, search, channel, page, pageSize, cancellationToken);

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


}
