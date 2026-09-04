using Microsoft.AspNetCore.Mvc;
using HaulScout.Api.Authorization;
using HaulScout.Api.Services;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Sales;
using HaulScout.Application.Security;
using HaulScout.Domain;

namespace HaulScout.Api.Controllers;

[ApiController]
[Route("api/sales")]
[RequireOrganizationPermission(HaulScoutPermissions.ManageListings)]
public sealed class SalesController(
    SalesChannelWorkflowService salesChannels,
    SaleRecordingWorkflowService recording,
    SaleQueryService queries,
    SaleGroupingWorkflowService grouping,
    SaleDeletionWorkflowService deletion,
    SaleTransitionWorkflowService transitions,
    SaleCorrectionWorkflowService corrections,
    SaleRefundWorkflowService refunds,
    SaleReturnWorkflowService returns) : HaulScoutControllerBase
{
    // ── Reference ──────────────────────────────────────────────────────────
    [HttpGet("reference-data")]
    public async Task<ActionResult<SalesReferenceDataDto>> ReferenceData(CancellationToken cancellationToken) =>
        await salesChannels.ReferenceDataAsync(cancellationToken);

    [HttpPost("fee-estimate")]
    public async Task<ActionResult<ChannelFeeEstimateDto>> EstimateFees(EstimateChannelFeesRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await salesChannels.EstimateFeesAsync(request, cancellationToken));

    [HttpGet("candidates")]
    public async Task<ActionResult<IReadOnlyList<SaleCandidateDto>>> Candidates(CancellationToken cancellationToken) =>
        Ok(await salesChannels.CandidatesAsync(cancellationToken));

    // ── Reads ──────────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SaleDto>>> List([FromQuery] SaleStatus? status, CancellationToken cancellationToken) =>
        Ok(await queries.ListAsync(status, cancellationToken));

    /// <summary>Searchable, sortable, paged sales for the finance table, plus the P&amp;L summary over the whole
    /// filtered set. Separate from <see cref="List"/> to keep the plain array response stable for the mobile app.</summary>
    [HttpGet("browse")]
    public async Task<ActionResult<SaleListDto>> Browse(
        [FromQuery] SaleStatus? status,
        [FromQuery] string? search,
        [FromQuery] string? sort,
        [FromQuery] bool desc = true,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default) =>
        await queries.BrowseAsync(status, search, sort, desc, page, pageSize, cancellationToken);

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SaleDto>> Get(Guid id, CancellationToken cancellationToken) =>
        ToActionResult(await queries.GetAsync(id, cancellationToken));

    [HttpGet("{id:guid}/profit")]
    public async Task<ActionResult<RealizedProfitDto>> GetProfit(Guid id, CancellationToken cancellationToken) =>
        ToActionResult(await queries.GetProfitAsync(id, cancellationToken));

    [HttpGet("{id:guid}/shipment")]
    public async Task<ActionResult<SaleShipmentDto>> GetShipment(Guid id, CancellationToken cancellationToken) =>
        ToActionResult(await queries.GetShipmentAsync(id, cancellationToken));

    [HttpGet("{id:guid}/corrections")]
    public async Task<ActionResult<IReadOnlyList<SaleCorrectionDto>>> ListCorrections(Guid id, CancellationToken cancellationToken) =>
        ToActionResult(await queries.ListCorrectionsAsync(id, cancellationToken));

    [HttpGet("{id:guid}/refunds")]
    public async Task<ActionResult<IReadOnlyList<RefundDto>>> ListRefunds(Guid id, CancellationToken cancellationToken) =>
        ToActionResult(await queries.ListRefundsAsync(id, cancellationToken));

    // ── Recording ──────────────────────────────────────────────────────────
    [HttpPost]
    [RequireOrganizationPermission(HaulScoutPermissions.ManageFinance)]
    public async Task<ActionResult<SaleDto>> Record(RecordSaleRequest request, CancellationToken cancellationToken)
    {
        AppResult<SaleDto> result = await recording.RecordAsync(request, cancellationToken);
        return result.Status switch
        {
            AppResultStatus.Ok when result.Value is not null => CreatedAtAction(nameof(GetProfit), new { id = result.Value.Id }, result.Value),
            _ => ToActionResult(result),
        };
    }

    /// <summary>Record one bundled order covering several items — one package, one tracking. Creates a linked
    /// sale per item with the order's price and shared costs allocated across them, so each item keeps its own net.</summary>
    [HttpPost("bundled")]
    [RequireOrganizationPermission(HaulScoutPermissions.ManageFinance)]
    public async Task<ActionResult<BundledSaleResultDto>> RecordBundled(RecordBundledSaleRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await recording.RecordBundledAsync(request, cancellationToken));

    /// <summary>Permanently delete a recorded sale (its corrections, refunds, and shipment cascade away).</summary>
    [HttpDelete("{id:guid}")]
    [RequireOrganizationPermission(HaulScoutPermissions.ManageFinance)]
    public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        AppResult result = await deletion.DeleteAsync(id, cancellationToken);
        return result.Status switch
        {
            AppResultStatus.Ok => NoContent(),
            // A message-less NotFound stays a bare 404 — adding a null body would change the response shape.
            AppResultStatus.NotFound => result.Message is null ? NotFound() : NotFound(result.Message),
            AppResultStatus.Conflict => Conflict(result.Message),
            _ => BadRequest(result.Message),
        };
    }

    // ── Grouping ───────────────────────────────────────────────────────────
    /// <summary>Combine several already-recorded sales into one marketplace order — the way eBay groups a multi-item
    /// order (one package, one tracking). Each sale keeps its own allocated price, fees, and net.</summary>
    [HttpPost("group")]
    [RequireOrganizationPermission(HaulScoutPermissions.ManageFinance)]
    public async Task<ActionResult<SaleGroupResultDto>> GroupIntoOrder(GroupSalesRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await grouping.GroupAsync(request, cancellationToken));

    /// <summary>Split selected sale lines out of a combined order. Every sale still keeps a real Order header.</summary>
    [HttpPost("ungroup")]
    [RequireOrganizationPermission(HaulScoutPermissions.ManageFinance)]
    public async Task<ActionResult<IReadOnlyList<SaleDto>>> RemoveFromOrder(UngroupSalesRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await grouping.UngroupAsync(request, cancellationToken));

    // ── Workflow transitions ───────────────────────────────────────────────
    [HttpPatch("{id:guid}/packed")]
    public Task<ActionResult<SaleDto>> MarkPacked(Guid id, CancellationToken cancellationToken) =>
        Transition(id, new SaleStatusTransitionRequest(SaleStatus.Packed, null, null, null, null), cancellationToken);

    [HttpPatch("{id:guid}/shipped")]
    public Task<ActionResult<SaleDto>> MarkShipped(Guid id, CancellationToken cancellationToken) =>
        Transition(id, new SaleStatusTransitionRequest(SaleStatus.Shipped, null, null, null, null), cancellationToken);

    [HttpPatch("{id:guid}/delivered")]
    public Task<ActionResult<SaleDto>> MarkDelivered(Guid id, CancellationToken cancellationToken) =>
        Transition(id, new SaleStatusTransitionRequest(SaleStatus.Delivered, null, null, null, null), cancellationToken);

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<SaleDto>> Transition(Guid id, SaleStatusTransitionRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await transitions.TransitionAsync(id, request, cancellationToken));

    // ── Corrections ────────────────────────────────────────────────────────
    [HttpPut("{id:guid}")]
    [RequireOrganizationPermission(HaulScoutPermissions.ManageFinance)]
    public async Task<ActionResult<SaleDto>> Update(Guid id, UpdateSaleRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await corrections.UpdateAsync(id, request, cancellationToken));

    // ── Refunds ────────────────────────────────────────────────────────────
    /// <summary>Record a refund (full or partial) against a sale as a first-class financial event.</summary>
    [HttpPost("{id:guid}/refunds")]
    [RequireOrganizationPermission(HaulScoutPermissions.ManageFinance)]
    public async Task<ActionResult<SaleDto>> RecordRefund(Guid id, RecordRefundRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await refunds.RecordAsync(id, request, cancellationToken));

    [HttpDelete("{id:guid}/refunds/{refundId:guid}")]
    [RequireOrganizationPermission(HaulScoutPermissions.ManageFinance)]
    public async Task<ActionResult<SaleDto>> VoidRefund(Guid id, Guid refundId, CancellationToken cancellationToken) =>
        ToActionResult(await refunds.VoidAsync(id, refundId, cancellationToken));

    // ── Returns (RMA lifecycle) ────────────────────────────────────────────
    // A return is the process; the Refund is the money event issued at the end. Requested → LabelIssued (seller pays
    // the label) → Received → Refunded. An immediate refund without a return skips all of this and posts a Refund
    // directly (RecordRefund).
    [HttpGet("{id:guid}/returns")]
    public async Task<ActionResult<IReadOnlyList<ReturnDto>>> ListReturns(Guid id, CancellationToken cancellationToken) =>
        Ok(await returns.ListAsync(id, cancellationToken));

    [HttpPost("{id:guid}/returns")]
    [RequireOrganizationPermission(HaulScoutPermissions.ManageFinance)]
    public async Task<ActionResult<ReturnDto>> OpenReturn(Guid id, OpenReturnRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await returns.OpenAsync(id, request, cancellationToken));

    [HttpPost("returns/{returnId:guid}/label")]
    [RequireOrganizationPermission(HaulScoutPermissions.ManageFinance)]
    public async Task<ActionResult<ReturnDto>> IssueReturnLabel(Guid returnId, IssueReturnLabelRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await returns.IssueLabelAsync(returnId, request, cancellationToken));

    [HttpPost("returns/{returnId:guid}/receive")]
    [RequireOrganizationPermission(HaulScoutPermissions.ManageFinance)]
    public async Task<ActionResult<ReturnDto>> ReceiveReturn(Guid returnId, CancellationToken cancellationToken) =>
        ToActionResult(await returns.ReceiveAsync(returnId, cancellationToken));

    [HttpPost("returns/{returnId:guid}/resolve")]
    [RequireOrganizationPermission(HaulScoutPermissions.ManageFinance)]
    public async Task<ActionResult<ReturnDto>> ResolveReturn(Guid returnId, ResolveReturnRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await returns.ResolveAsync(returnId, request, cancellationToken));

    [HttpPost("returns/{returnId:guid}/reject")]
    [RequireOrganizationPermission(HaulScoutPermissions.ManageFinance)]
    public async Task<ActionResult<ReturnDto>> RejectReturn(Guid returnId, RejectReturnRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await returns.RejectAsync(returnId, request, cancellationToken));

    [HttpPost("returns/{returnId:guid}/cancel")]
    [RequireOrganizationPermission(HaulScoutPermissions.ManageFinance)]
    public async Task<ActionResult<ReturnDto>> CancelReturn(Guid returnId, CancellationToken cancellationToken) =>
        ToActionResult(await returns.CancelAsync(returnId, cancellationToken));

}
